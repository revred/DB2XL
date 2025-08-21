using DB2XL.Core.Models;
using DB2XL.Core.Services;
using DB2XL.Export.Bundle.Extensions;

namespace DB2XL.Export.Bundle.Services;

/// <summary>
/// Coordinates different partitioning strategies and provides factory functionality.
/// Acts as the main entry point for all data partitioning operations.
/// </summary>
public sealed class PartitionCoordinator : IPartitionerFactory
{
    private readonly Dictionary<PartitionStrategy, Func<TablePartitionConfig, IDataPartitioner>> _partitionerFactories;

    public PartitionCoordinator()
    {
        _partitionerFactories = new Dictionary<PartitionStrategy, Func<TablePartitionConfig, IDataPartitioner>>
        {
            [PartitionStrategy.None] = config => new NoPartitioner(),
            [PartitionStrategy.RowCount] = config => new SizeBasedPartitioner(config.RowsPerPartition),
            [PartitionStrategy.TimeBased] = config => new TimeBasedPartitioner(
                config.TimeColumn ?? throw new ArgumentException("TimeColumn is required for time-based partitioning"), 
                config.TimeGranularity),
            [PartitionStrategy.FilterBased] = config => new FilterBasedPartitioner(
                config.FilterExpression ?? throw new ArgumentException("FilterExpression is required for filter-based partitioning"),
                config.FilterLabel ?? "custom")
        };
    }

    /// <summary>
    /// Creates the appropriate partitioner for the specified strategy.
    /// </summary>
    public IDataPartitioner CreatePartitioner(PartitionStrategy strategy, TablePartitionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!_partitionerFactories.TryGetValue(strategy, out var factory))
        {
            throw new NotSupportedException($"Partitioning strategy '{strategy}' is not supported");
        }

        try
        {
            return factory(config);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create partitioner for strategy '{strategy}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets all supported partitioning strategies.
    /// </summary>
    public IReadOnlyList<PartitionStrategy> GetSupportedStrategies()
    {
        return _partitionerFactories.Keys.ToList().AsReadOnly();
    }

    /// <summary>
    /// Determines the best partitioning strategy for a table based on its characteristics.
    /// </summary>
    /// <param name="tableMetadata">Table metadata for analysis</param>
    /// <param name="exportOptions">Bundle export options for context</param>
    /// <returns>Recommended partitioning configuration</returns>
    public TablePartitionConfig RecommendPartitioningStrategy(TableMetadata tableMetadata, BundleExportOptions exportOptions)
    {
        ArgumentNullException.ThrowIfNull(tableMetadata);
        ArgumentNullException.ThrowIfNull(exportOptions);

        // For small tables, no partitioning needed
        if (tableMetadata.EstimatedRowCount <= 50_000)
        {
            return new TablePartitionConfig
            {
                TableName = tableMetadata.TableName,
                Strategy = PartitionStrategy.None
            };
        }

        // Check for time-based partitioning opportunities
        var timeBasedPartitioner = new TimeBasedPartitioner("dummy"); // Temporary for recommendation
        var timeBasedConfig = timeBasedPartitioner.GetRecommendedPartitioning(tableMetadata, exportOptions);
        
        if (timeBasedConfig.Strategy == PartitionStrategy.TimeBased)
        {
            var validation = timeBasedPartitioner.ValidatePartitionConfig(tableMetadata, timeBasedConfig);
            if (validation.IsValid && validation.Warnings.Count == 0)
            {
                return timeBasedConfig;
            }
        }

        // Fall back to size-based partitioning
        var sizeBasedPartitioner = new SizeBasedPartitioner();
        return sizeBasedPartitioner.GetRecommendedPartitioning(tableMetadata, exportOptions);
    }

    /// <summary>
    /// Validates a partitioning configuration against table metadata.
    /// </summary>
    /// <param name="tableMetadata">Table metadata</param>
    /// <param name="config">Partitioning configuration to validate</param>
    /// <returns>Validation result</returns>
    public PartitionValidationResult ValidatePartitioningConfiguration(TableMetadata tableMetadata, TablePartitionConfig config)
    {
        ArgumentNullException.ThrowIfNull(tableMetadata);
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            var partitioner = CreatePartitioner(config.Strategy, config);
            return partitioner.ValidatePartitionConfig(tableMetadata, config);
        }
        catch (Exception ex)
        {
            return PartitionValidationResult.Failure($"Configuration validation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Estimates the total number of partitions across all tables.
    /// </summary>
    /// <param name="tableConfigs">Table configurations with partitioning strategies</param>
    /// <param name="tableMetadata">Table metadata for estimation</param>
    /// <returns>Total estimated partition count</returns>
    public int EstimateTotalPartitions(
        IReadOnlyList<TablePartitionConfig> tableConfigs,
        IReadOnlyDictionary<string, TableMetadata> tableMetadata)
    {
        ArgumentNullException.ThrowIfNull(tableConfigs);
        ArgumentNullException.ThrowIfNull(tableMetadata);

        var totalPartitions = 0;

        foreach (var config in tableConfigs)
        {
            if (!tableMetadata.TryGetValue(config.TableName, out var metadata))
                continue;

            try
            {
                var partitioner = CreatePartitioner(config.Strategy, config);
                totalPartitions += partitioner.EstimatePartitionCount(metadata, config);
            }
            catch
            {
                // If estimation fails, assume single partition
                totalPartitions += 1;
            }
        }

        return totalPartitions;
    }
}

/// <summary>
/// No-op partitioner that returns all data as a single partition.
/// Used for small tables or when partitioning is explicitly disabled.
/// </summary>
internal sealed class NoPartitioner : IDataPartitioner
{
    public async IAsyncEnumerable<DataPartition> PartitionDataAsync(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> tableData,
        string tableName,
        TablePartitionConfig config,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        
        await foreach (var row in tableData.WithCancellation(cancellationToken))
        {
            rows.Add(row);
        }

        var partitionInfo = new PartitionInfo
        {
            TableName = tableName,
            PartitionLabel = "full",
            Strategy = "by=none,single_partition=true",
            RowCount = rows.Count,
            RelativePath = $"tables/{SanitizeTableName(tableName)}/{SanitizeTableName(tableName)}_full.jsonl",
            Format = "jsonl",
            FirstPrimaryKey = ExtractPrimaryKeyValue(rows.FirstOrDefault()),
            LastPrimaryKey = ExtractPrimaryKeyValue(rows.LastOrDefault())
        };

        yield return new DataPartition
        {
            Data = rows.ToAsyncEnumerable(),
            Info = partitionInfo,
            EstimatedRowCount = rows.Count,
            IsFinalPartition = true,
            PartitionIndex = 0,
            Strategy = PartitionStrategy.None
        };
    }

    public int EstimatePartitionCount(TableMetadata tableMetadata, TablePartitionConfig config)
    {
        return 1; // Always single partition
    }

    public PartitionValidationResult ValidatePartitionConfig(TableMetadata tableMetadata, TablePartitionConfig config)
    {
        return PartitionValidationResult.Success(); // No partitioning is always valid
    }

    public TablePartitionConfig GetRecommendedPartitioning(TableMetadata tableMetadata, BundleExportOptions exportOptions)
    {
        return new TablePartitionConfig
        {
            TableName = tableMetadata.TableName,
            Strategy = PartitionStrategy.None
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
}

/// <summary>
/// Simple filter-based partitioner for custom WHERE clause partitioning.
/// </summary>
internal sealed class FilterBasedPartitioner : IFilterBasedPartitioner
{
    public IReadOnlyDictionary<string, string> FilterExpressions { get; }

    public FilterBasedPartitioner(string filterExpression, string filterLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filterExpression);
        ArgumentException.ThrowIfNullOrWhiteSpace(filterLabel);

        FilterExpressions = new Dictionary<string, string>
        {
            [filterLabel] = filterExpression
        }.AsReadOnly();
    }

    public FilterBasedPartitioner(IReadOnlyDictionary<string, string> filterExpressions)
    {
        ArgumentNullException.ThrowIfNull(filterExpressions);
        
        if (!filterExpressions.Any())
            throw new ArgumentException("At least one filter expression is required", nameof(filterExpressions));

        FilterExpressions = filterExpressions;
    }

    public async IAsyncEnumerable<DataPartition> PartitionDataAsync(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> tableData,
        string tableName,
        TablePartitionConfig config,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Note: Filter-based partitioning requires SQL-level filtering
        // This implementation is a placeholder - actual filtering would happen in SQL query
        var allRows = new List<IReadOnlyDictionary<string, object?>>();
        
        await foreach (var row in tableData.WithCancellation(cancellationToken))
        {
            allRows.Add(row);
        }

        var partitionIndex = 0;
        foreach (var (label, expression) in FilterExpressions)
        {
            var partitionInfo = new PartitionInfo
            {
                TableName = tableName,
                PartitionLabel = label,
                Strategy = $"by=filter,expression={expression}",
                RowCount = allRows.Count, // Placeholder - would be actual filtered count
                RelativePath = $"tables/{SanitizeTableName(tableName)}/{SanitizeTableName(tableName)}_{label}.jsonl",
                Format = "jsonl"
            };

            yield return new DataPartition
            {
                Data = allRows.ToAsyncEnumerable(), // Placeholder - would be filtered data
                Info = partitionInfo,
                EstimatedRowCount = allRows.Count,
                IsFinalPartition = partitionIndex == FilterExpressions.Count - 1,
                PartitionIndex = partitionIndex,
                Strategy = PartitionStrategy.FilterBased
            };

            partitionIndex++;
        }
    }

    public int EstimatePartitionCount(TableMetadata tableMetadata, TablePartitionConfig config)
    {
        return FilterExpressions.Count;
    }

    public PartitionValidationResult ValidatePartitionConfig(TableMetadata tableMetadata, TablePartitionConfig config)
    {
        // Basic validation - would need more sophisticated SQL parsing in production
        if (string.IsNullOrWhiteSpace(config.FilterExpression))
        {
            return PartitionValidationResult.Failure("Filter expression is required for filter-based partitioning");
        }

        return PartitionValidationResult.Success();
    }

    public TablePartitionConfig GetRecommendedPartitioning(TableMetadata tableMetadata, BundleExportOptions exportOptions)
    {
        // Filter-based partitioning cannot be auto-recommended
        return new TablePartitionConfig
        {
            TableName = tableMetadata.TableName,
            Strategy = PartitionStrategy.RowCount,
            RowsPerPartition = 200_000
        };
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
}