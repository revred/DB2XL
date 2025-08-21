using DB2XL.Core.Models;
using DB2XL.Core.Services;
using DB2XL.Export.Bundle.Extensions;

namespace DB2XL.Export.Bundle.Services;

/// <summary>
/// Partitions table data based on row count limits.
/// Creates partitions of approximately equal size for predictable memory usage.
/// </summary>
public sealed class SizeBasedPartitioner : ISizeBasedPartitioner
{
    public int RowsPerPartition { get; }

    public SizeBasedPartitioner(int rowsPerPartition = 200_000)
    {
        if (rowsPerPartition <= 0)
            throw new ArgumentException("Rows per partition must be greater than 0", nameof(rowsPerPartition));

        RowsPerPartition = rowsPerPartition;
    }

    /// <summary>
    /// Partitions data into fixed-size chunks based on row count.
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

        var partitionIndex = 0;
        var partitionRows = new List<IReadOnlyDictionary<string, object?>>(RowsPerPartition);
        var totalProcessedRows = 0L;
        
        await foreach (var row in tableData.WithCancellation(cancellationToken))
        {
            partitionRows.Add(row);
            totalProcessedRows++;

            if (partitionRows.Count >= RowsPerPartition)
            {
                var partition = CreatePartition(
                    partitionRows,
                    tableName,
                    partitionIndex,
                    totalProcessedRows,
                    isFinal: false);

                yield return partition;

                partitionIndex++;
                partitionRows.Clear();
            }
        }

        // Yield final partition if any rows remain
        if (partitionRows.Count > 0)
        {
            var finalPartition = CreatePartition(
                partitionRows,
                tableName,
                partitionIndex,
                totalProcessedRows,
                isFinal: true);

            yield return finalPartition;
        }

        // Handle empty table case
        if (partitionIndex == 0 && partitionRows.Count == 0)
        {
            var emptyPartition = CreateEmptyPartition(tableName, totalProcessedRows);
            yield return emptyPartition;
        }
    }

    /// <summary>
    /// Estimates partition count based on table row count and partition size.
    /// </summary>
    public int EstimatePartitionCount(TableMetadata tableMetadata, TablePartitionConfig config)
    {
        ArgumentNullException.ThrowIfNull(tableMetadata);
        ArgumentNullException.ThrowIfNull(config);

        if (tableMetadata.EstimatedRowCount <= 0)
            return 1; // At least one partition even for empty tables

        var partitionSize = config.RowsPerPartition > 0 ? config.RowsPerPartition : RowsPerPartition;
        var estimatedPartitions = (int)Math.Ceiling((double)tableMetadata.EstimatedRowCount / partitionSize);
        
        return Math.Max(1, estimatedPartitions);
    }

    /// <summary>
    /// Validates that size-based partitioning configuration is reasonable.
    /// </summary>
    public PartitionValidationResult ValidatePartitionConfig(TableMetadata tableMetadata, TablePartitionConfig config)
    {
        ArgumentNullException.ThrowIfNull(tableMetadata);
        ArgumentNullException.ThrowIfNull(config);

        var errors = new List<string>();
        var warnings = new List<string>();
        var recommendations = new List<string>();

        var partitionSize = config.RowsPerPartition > 0 ? config.RowsPerPartition : RowsPerPartition;

        // Validate partition size
        if (partitionSize <= 0)
        {
            errors.Add("Partition size must be greater than 0");
        }
        else if (partitionSize < 1_000)
        {
            warnings.Add("Very small partition size may create excessive number of files");
            recommendations.Add("Consider increasing partition size to at least 10,000 rows");
        }
        else if (partitionSize > 1_000_000)
        {
            warnings.Add("Large partition size may impact memory usage and processing time");
            recommendations.Add("Consider reducing partition size to under 500,000 rows");
        }

        // Check if partitioning is necessary
        if (tableMetadata.EstimatedRowCount > 0 && tableMetadata.EstimatedRowCount <= partitionSize)
        {
            recommendations.Add($"Table has only {tableMetadata.EstimatedRowCount:N0} rows - partitioning may not be necessary");
        }

        // Estimate partition count and warn if excessive
        var estimatedPartitions = EstimatePartitionCount(tableMetadata, config);
        if (estimatedPartitions > 1000)
        {
            warnings.Add($"Configuration will create {estimatedPartitions:N0} partitions - this may impact performance");
            recommendations.Add("Consider increasing partition size to reduce file count");
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
    /// Recommends size-based partitioning configuration based on table characteristics.
    /// </summary>
    public TablePartitionConfig GetRecommendedPartitioning(TableMetadata tableMetadata, BundleExportOptions exportOptions)
    {
        ArgumentNullException.ThrowIfNull(tableMetadata);
        ArgumentNullException.ThrowIfNull(exportOptions);

        // Calculate optimal partition size based on table characteristics
        var optimalSize = CalculateOptimalPartitionSize(tableMetadata);

        return new TablePartitionConfig
        {
            TableName = tableMetadata.TableName,
            Strategy = PartitionStrategy.RowCount,
            RowsPerPartition = optimalSize
        };
    }

    #region Private Helper Methods

    private DataPartition CreatePartition(
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        string tableName,
        int partitionIndex,
        long totalProcessedRows,
        bool isFinal)
    {
        var partitionLabel = $"p{partitionIndex + 1:D5}"; // p00001, p00002, etc.
        
        var partitionInfo = new PartitionInfo
        {
            TableName = tableName,
            PartitionLabel = partitionLabel,
            Strategy = "by=size,rows_per_partition=" + RowsPerPartition,
            RowCount = rows.Count,
            RelativePath = $"tables/{SanitizeTableName(tableName)}/{SanitizeTableName(tableName)}_{partitionLabel}.jsonl",
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
            Strategy = PartitionStrategy.RowCount
        };
    }

    private DataPartition CreateEmptyPartition(string tableName, long totalProcessedRows)
    {
        var partitionInfo = new PartitionInfo
        {
            TableName = tableName,
            PartitionLabel = "p00001",
            Strategy = "by=size,rows_per_partition=" + RowsPerPartition,
            RowCount = 0,
            RelativePath = $"tables/{SanitizeTableName(tableName)}/{SanitizeTableName(tableName)}_p00001.jsonl",
            Format = "jsonl"
        };

        return new DataPartition
        {
            Data = AsyncEnumerableExtensions.EmptyAsync<IReadOnlyDictionary<string, object?>>(),
            Info = partitionInfo,
            EstimatedRowCount = 0,
            IsFinalPartition = true,
            PartitionIndex = 0,
            Strategy = PartitionStrategy.RowCount
        };
    }

    private int CalculateOptimalPartitionSize(TableMetadata tableMetadata)
    {
        // Base partition size
        var baseSize = 200_000;

        // Adjust based on table characteristics
        if (tableMetadata.EstimatedRowCount <= 50_000)
        {
            // Small tables: single partition
            return (int)Math.Max(tableMetadata.EstimatedRowCount, 1);
        }

        // Adjust for wide tables (many columns)
        if (tableMetadata.Columns.Count > 50)
        {
            baseSize = 100_000; // Smaller partitions for wide tables
        }

        // Adjust for tables with BLOB columns
        var blobColumns = tableMetadata.Columns.Count(c => c.IsBlobColumn);
        if (blobColumns > 0)
        {
            baseSize = Math.Max(50_000, baseSize / (blobColumns + 1)); // Reduce size for BLOB columns
        }

        // Ensure reasonable bounds
        return Math.Max(10_000, Math.Min(500_000, baseSize));
    }

    private static string? ExtractPrimaryKeyValue(IReadOnlyDictionary<string, object?>? row)
    {
        if (row == null) return null;

        // Try common primary key column names
        var pkCandidates = new[] { "id", "ID", "Id", "rowid", "ROWID" };
        
        foreach (var candidate in pkCandidates)
        {
            if (row.TryGetValue(candidate, out var value) && value != null)
            {
                return value.ToString();
            }
        }

        // Fallback to first column value
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