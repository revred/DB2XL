using DB2XL.Core.Models;
using DB2XL.Core.Services;
using DB2XL.Export.Bundle.Extensions;
using System.Globalization;

namespace DB2XL.Export.Bundle.Services;

/// <summary>
/// Partitions table data based on datetime column values.
/// Creates partitions by time periods (day, week, month, quarter, year).
/// </summary>
public sealed class TimeBasedPartitioner : ITimeBasedPartitioner
{
    public string TimeColumn { get; }
    public TimePartitionGranularity Granularity { get; }

    public TimeBasedPartitioner(string timeColumn, TimePartitionGranularity granularity = TimePartitionGranularity.Month)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeColumn);
        TimeColumn = timeColumn;
        Granularity = granularity;
    }

    /// <summary>
    /// Partitions data based on time periods extracted from the specified datetime column.
    /// </summary>
    public async IAsyncEnumerable<DataPartition> PartitionDataAsync(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> tableData,
        string tableName,
        TablePartitionConfig config,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tableData);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(config);

        var partitions = new Dictionary<string, List<IReadOnlyDictionary<string, object?>>>();
        var partitionStats = new Dictionary<string, (DateTime minDate, DateTime maxDate, long count)>();

        // Group rows by time period
        await foreach (var row in tableData.WithCancellation(cancellationToken))
        {
            var timeValue = ExtractTimeValue(row, TimeColumn);
            if (timeValue == null)
            {
                // Handle rows with null/invalid time values
                var nullKey = "null_dates";
                if (!partitions.ContainsKey(nullKey))
                {
                    partitions[nullKey] = new List<IReadOnlyDictionary<string, object?>>();
                }
                partitions[nullKey].Add(row);
                continue;
            }

            var periodKey = GetTimePeriodKey(timeValue.Value, Granularity);
            
            if (!partitions.ContainsKey(periodKey))
            {
                partitions[periodKey] = new List<IReadOnlyDictionary<string, object?>>();
                partitionStats[periodKey] = (timeValue.Value, timeValue.Value, 0);
            }

            partitions[periodKey].Add(row);
            
            // Update stats
            var (minDate, maxDate, count) = partitionStats[periodKey];
            partitionStats[periodKey] = (
                timeValue.Value < minDate ? timeValue.Value : minDate,
                timeValue.Value > maxDate ? timeValue.Value : maxDate,
                count + 1
            );
        }

        // Create partitions sorted by time period
        var sortedKeys = partitions.Keys.Where(k => k != "null_dates").OrderBy(k => k).ToList();
        if (partitions.ContainsKey("null_dates"))
        {
            sortedKeys.Add("null_dates"); // Put null dates last
        }

        for (int i = 0; i < sortedKeys.Count; i++)
        {
            var key = sortedKeys[i];
            var rows = partitions[key];
            var isFinal = i == sortedKeys.Count - 1;

            var partition = CreateTimePartition(
                rows,
                tableName,
                key,
                i,
                partitionStats.GetValueOrDefault(key),
                isFinal);

            yield return partition;
        }

        // Handle empty table case
        if (!partitions.Any())
        {
            var emptyPartition = CreateEmptyTimePartition(tableName);
            yield return emptyPartition;
        }
    }

    /// <summary>
    /// Estimates partition count based on date range and granularity.
    /// </summary>
    public int EstimatePartitionCount(TableMetadata tableMetadata, TablePartitionConfig config)
    {
        ArgumentNullException.ThrowIfNull(tableMetadata);
        ArgumentNullException.ThrowIfNull(config);

        // Without analyzing actual data, provide conservative estimates
        return config.TimeGranularity switch
        {
            TimePartitionGranularity.Day => Math.Min(365, (int)(tableMetadata.EstimatedRowCount / 1000) + 1),
            TimePartitionGranularity.Week => Math.Min(52, (int)(tableMetadata.EstimatedRowCount / 5000) + 1),
            TimePartitionGranularity.Month => Math.Min(120, (int)(tableMetadata.EstimatedRowCount / 20000) + 1), // 10 years max
            TimePartitionGranularity.Quarter => Math.Min(40, (int)(tableMetadata.EstimatedRowCount / 50000) + 1), // 10 years max
            TimePartitionGranularity.Year => Math.Min(20, (int)(tableMetadata.EstimatedRowCount / 100000) + 1),
            _ => 1
        };
    }

    /// <summary>
    /// Validates time-based partitioning configuration.
    /// </summary>
    public PartitionValidationResult ValidatePartitionConfig(TableMetadata tableMetadata, TablePartitionConfig config)
    {
        ArgumentNullException.ThrowIfNull(tableMetadata);
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();
        var warnings = new List<string>();
        var recommendations = new List<string>();

        // Validate time column exists
        var timeColumn = config.TimeColumn ?? TimeColumn;
        var column = tableMetadata.Columns.FirstOrDefault(c => 
            string.Equals(c.Name, timeColumn, StringComparison.OrdinalIgnoreCase));

        if (column == null)
        {
            errors.Add($"Time column '{timeColumn}' not found in table '{tableMetadata.TableName}'");
            return PartitionValidationResult.Failure(errors.ToArray());
        }

        // Check if column is likely to contain datetime values
        if (!IsLikelyDateTimeColumn(column))
        {
            warnings.Add($"Column '{timeColumn}' may not contain datetime values (declared type: {column.DeclaredType})");
            recommendations.Add("Verify that the column contains valid datetime values");
        }

        // Check for nullable time column
        if (column.IsNullable)
        {
            warnings.Add($"Time column '{timeColumn}' allows NULL values - rows with NULL dates will be grouped separately");
        }

        // Validate granularity choice
        var estimatedPartitions = EstimatePartitionCount(tableMetadata, config);
        if (estimatedPartitions > 500)
        {
            warnings.Add($"Time-based partitioning may create {estimatedPartitions} partitions - consider using coarser granularity");
            recommendations.Add($"Consider using {GetCoarserGranularity(config.TimeGranularity)} granularity to reduce partition count");
        }

        if (errors.Count > 0)
            return PartitionValidationResult.Failure(errors.ToArray());

        return new PartitionValidationResult
        {
            IsValid = true,
            Warnings = warnings.AsReadOnly(),
            Recommendations = recommendations.AsReadOnly()
        };
    }

    /// <summary>
    /// Recommends time-based partitioning if suitable datetime columns are found.
    /// </summary>
    public TablePartitionConfig GetRecommendedPartitioning(TableMetadata tableMetadata, BundleExportOptions exportOptions)
    {
        ArgumentNullException.ThrowIfNull(tableMetadata);
        ArgumentNullException.ThrowIfNull(exportOptions);

        // Find potential datetime columns
        var dateTimeColumns = tableMetadata.Columns
            .Where(IsLikelyDateTimeColumn)
            .OrderBy(c => c.Name.Contains("created", StringComparison.OrdinalIgnoreCase) ? 0 : 1) // Prefer "created" columns
            .ToList();

        if (!dateTimeColumns.Any())
        {
            // No suitable columns found, fall back to size-based
            return new TablePartitionConfig
            {
                TableName = tableMetadata.TableName,
                Strategy = PartitionStrategy.RowCount,
                RowsPerPartition = 200_000
            };
        }

        var recommendedColumn = dateTimeColumns.First();
        var recommendedGranularity = RecommendGranularity(tableMetadata.EstimatedRowCount);

        return new TablePartitionConfig
        {
            TableName = tableMetadata.TableName,
            Strategy = PartitionStrategy.TimeBased,
            TimeColumn = recommendedColumn.Name,
            TimeGranularity = recommendedGranularity
        };
    }

    #region Private Helper Methods

    private static DateTime? ExtractTimeValue(IReadOnlyDictionary<string, object?> row, string timeColumn)
    {
        if (!row.TryGetValue(timeColumn, out var value) || value == null)
            return null;

        // Handle various datetime representations
        return value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.DateTime,
            string str when DateTime.TryParse(str, out var parsed) => parsed,
            long ticks when ticks > 0 => TryParseTicksOrUnix(ticks),
            int unixSeconds when unixSeconds > 0 => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).DateTime,
            _ => null
        };
    }

    private static DateTime? TryParseTicksOrUnix(long value)
    {
        try
        {
            // Try .NET ticks first (if very large number)
            if (value > 3155378975999999999L) // Year 10000
                return new DateTime(value);

            // Try Unix timestamp (seconds)
            if (value > 946684800L && value < 4102444800L) // 2000-2100 range
                return DateTimeOffset.FromUnixTimeSeconds(value).DateTime;

            // Try Unix timestamp (milliseconds)
            if (value > 946684800000L && value < 4102444800000L)
                return DateTimeOffset.FromUnixTimeMilliseconds(value).DateTime;

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string GetTimePeriodKey(DateTime dateTime, TimePartitionGranularity granularity)
    {
        return granularity switch
        {
            TimePartitionGranularity.Day => dateTime.ToString("yyyy-MM-dd"),
            TimePartitionGranularity.Week => $"{dateTime.Year}-W{GetWeekOfYear(dateTime):D2}",
            TimePartitionGranularity.Month => dateTime.ToString("yyyy-MM"),
            TimePartitionGranularity.Quarter => $"{dateTime.Year}-Q{GetQuarter(dateTime)}",
            TimePartitionGranularity.Year => dateTime.ToString("yyyy"),
            _ => dateTime.ToString("yyyy-MM")
        };
    }

    private static int GetWeekOfYear(DateTime dateTime)
    {
        var calendar = CultureInfo.InvariantCulture.Calendar;
        return calendar.GetWeekOfYear(dateTime, CalendarWeekRule.FirstDay, DayOfWeek.Monday);
    }

    private static int GetQuarter(DateTime dateTime)
    {
        return (dateTime.Month - 1) / 3 + 1;
    }

    private DataPartition CreateTimePartition(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string tableName,
        string periodKey,
        int partitionIndex,
        (DateTime minDate, DateTime maxDate, long count) stats,
        bool isFinal)
    {
        var partitionInfo = new PartitionInfo
        {
            TableName = tableName,
            PartitionLabel = periodKey,
            Strategy = $"by=time,column={TimeColumn},granularity={Granularity}",
            RowCount = rows.Count,
            RelativePath = $"tables/{SanitizeTableName(tableName)}/{SanitizeTableName(tableName)}_{periodKey}.jsonl",
            Format = "jsonl",
            FirstPrimaryKey = ExtractPrimaryKeyValue(rows.FirstOrDefault()),
            LastPrimaryKey = ExtractPrimaryKeyValue(rows.LastOrDefault())
        };

        return new DataPartition
        {
            Data = rows.ToAsyncEnumerable(),
            Info = partitionInfo,
            EstimatedRowCount = rows.Count,
            IsFinalPartition = isFinal,
            PartitionIndex = partitionIndex,
            Strategy = PartitionStrategy.TimeBased
        };
    }

    private DataPartition CreateEmptyTimePartition(string tableName)
    {
        var partitionInfo = new PartitionInfo
        {
            TableName = tableName,
            PartitionLabel = "empty",
            Strategy = $"by=time,column={TimeColumn},granularity={Granularity}",
            RowCount = 0,
            RelativePath = $"tables/{SanitizeTableName(tableName)}/{SanitizeTableName(tableName)}_empty.jsonl",
            Format = "jsonl"
        };

        return new DataPartition
        {
            Data = AsyncEnumerableExtensions.EmptyAsync<IReadOnlyDictionary<string, object?>>(),
            Info = partitionInfo,
            EstimatedRowCount = 0,
            IsFinalPartition = true,
            PartitionIndex = 0,
            Strategy = PartitionStrategy.TimeBased
        };
    }

    private static bool IsLikelyDateTimeColumn(ColumnMetadata column)
    {
        var name = column.Name.ToLowerInvariant();
        var type = column.DeclaredType.ToLowerInvariant();

        // Check for datetime-like column names
        var dateTimeNames = new[] { "date", "time", "created", "modified", "updated", "timestamp", "when" };
        if (dateTimeNames.Any(dt => name.Contains(dt)))
            return true;

        // Check for datetime-like types
        var dateTimeTypes = new[] { "datetime", "date", "time", "timestamp" };
        if (dateTimeTypes.Any(dt => type.Contains(dt)))
            return true;

        return false;
    }

    private static TimePartitionGranularity RecommendGranularity(long estimatedRowCount)
    {
        return estimatedRowCount switch
        {
            < 100_000 => TimePartitionGranularity.Month,
            < 1_000_000 => TimePartitionGranularity.Quarter,
            < 10_000_000 => TimePartitionGranularity.Year,
            _ => TimePartitionGranularity.Year
        };
    }

    private static TimePartitionGranularity GetCoarserGranularity(TimePartitionGranularity current)
    {
        return current switch
        {
            TimePartitionGranularity.Day => TimePartitionGranularity.Week,
            TimePartitionGranularity.Week => TimePartitionGranularity.Month,
            TimePartitionGranularity.Month => TimePartitionGranularity.Quarter,
            TimePartitionGranularity.Quarter => TimePartitionGranularity.Year,
            TimePartitionGranularity.Year => TimePartitionGranularity.Year,
            _ => TimePartitionGranularity.Month
        };
    }

    private static string? ExtractPrimaryKeyValue(IReadOnlyDictionary<string, object?>? row)
    {
        if (row == null) return null;

        var pkCandidates = new[] { "id", "ID", "Id", "rowid", "ROWID" };
        
        foreach (var candidate in pkCandidates)
        {
            if (row.TryGetValue(candidate, out var value) && value != null)
            {
                return value.ToString();
            }
        }

        var firstValue = row.Values.FirstOrDefault();
        return firstValue?.ToString();
    }

    private static string SanitizeTableName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            return "_empty_";

        var invalidChars = Path.GetInvalidFileNameChars();
        var result = new char[tableName.Length];
        var resultIndex = 0;

        foreach (char c in tableName)
        {
            if (invalidChars.Contains(c) || c == ' ')
            {
                result[resultIndex++] = '_';
            }
            else
            {
                result[resultIndex++] = c;
            }
        }

        var sanitized = new string(result, 0, resultIndex).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "_sanitized_" : sanitized;
    }

    #endregion
}