using DB2XL.Core.Models;

namespace DB2XL.Core.Services;

/// <summary>
/// Service interface for partitioning table data into manageable chunks.
/// Supports multiple partitioning strategies: size-based, time-based, and filter-based.
/// </summary>
public interface IDataPartitioner
{
    /// <summary>
    /// Partitions table data using the configured strategy.
    /// Returns an async enumerable of partitions with metadata.
    /// </summary>
    /// <param name="tableData">Source table data as async enumerable</param>
    /// <param name="tableName">Name of the table being partitioned</param>
    /// <param name="config">Partitioning configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of data partitions</returns>
    IAsyncEnumerable<DataPartition> PartitionDataAsync(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> tableData,
        string tableName,
        TablePartitionConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimates the number of partitions that will be created for a table.
    /// Used for progress tracking and resource planning.
    /// </summary>
    /// <param name="tableMetadata">Table metadata including row count estimates</param>
    /// <param name="config">Partitioning configuration</param>
    /// <returns>Estimated number of partitions</returns>
    int EstimatePartitionCount(TableMetadata tableMetadata, TablePartitionConfig config);

    /// <summary>
    /// Validates that a partitioning configuration is valid for the given table.
    /// </summary>
    /// <param name="tableMetadata">Table metadata</param>
    /// <param name="config">Partitioning configuration to validate</param>
    /// <returns>Validation result with any errors or warnings</returns>
    PartitionValidationResult ValidatePartitionConfig(TableMetadata tableMetadata, TablePartitionConfig config);

    /// <summary>
    /// Gets the recommended partitioning strategy for a table based on its characteristics.
    /// </summary>
    /// <param name="tableMetadata">Table metadata</param>
    /// <param name="exportOptions">Bundle export options for context</param>
    /// <returns>Recommended partitioning configuration</returns>
    TablePartitionConfig GetRecommendedPartitioning(TableMetadata tableMetadata, BundleExportOptions exportOptions);
}

/// <summary>
/// Contains data for a single partition along with its metadata.
/// </summary>
public sealed record DataPartition
{
    /// <summary>Partition data as enumerable rows.</summary>
    public IAsyncEnumerable<IReadOnlyDictionary<string, object?>> Data { get; init; } = null!;

    /// <summary>Partition metadata for manifest generation.</summary>
    public PartitionInfo Info { get; init; } = new();

    /// <summary>Estimated number of rows in this partition.</summary>
    public long EstimatedRowCount { get; init; }

    /// <summary>Whether this is the final partition for the table.</summary>
    public bool IsFinalPartition { get; init; }

    /// <summary>Partition sequence number (0-based).</summary>
    public int PartitionIndex { get; init; }

    /// <summary>Partitioning strategy used to create this partition.</summary>
    public PartitionStrategy Strategy { get; init; }
}

/// <summary>
/// Result of validating a partition configuration.
/// </summary>
public sealed record PartitionValidationResult
{
    /// <summary>Whether the configuration is valid.</summary>
    public bool IsValid { get; init; }

    /// <summary>Validation error messages.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>Validation warning messages.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Recommended configuration adjustments.</summary>
    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();

    /// <summary>Creates a successful validation result.</summary>
    public static PartitionValidationResult Success() => new() { IsValid = true };

    /// <summary>Creates a failed validation result with errors.</summary>
    public static PartitionValidationResult Failure(params string[] errors) => new()
    {
        IsValid = false,
        Errors = errors.ToList().AsReadOnly()
    };

    /// <summary>Creates a validation result with warnings but no errors.</summary>
    public static PartitionValidationResult WithWarnings(params string[] warnings) => new()
    {
        IsValid = true,
        Warnings = warnings.ToList().AsReadOnly()
    };
}

/// <summary>
/// Factory interface for creating specific partitioner implementations.
/// </summary>
public interface IPartitionerFactory
{
    /// <summary>
    /// Creates a partitioner for the specified strategy.
    /// </summary>
    /// <param name="strategy">Partitioning strategy to use</param>
    /// <param name="config">Configuration for the partitioner</param>
    /// <returns>Configured partitioner instance</returns>
    IDataPartitioner CreatePartitioner(PartitionStrategy strategy, TablePartitionConfig config);

    /// <summary>
    /// Gets all supported partitioning strategies.
    /// </summary>
    /// <returns>List of supported strategies</returns>
    IReadOnlyList<PartitionStrategy> GetSupportedStrategies();
}

/// <summary>
/// Partitioning strategy implementations.
/// </summary>
public interface ISizeBasedPartitioner : IDataPartitioner
{
    /// <summary>Maximum rows per partition.</summary>
    int RowsPerPartition { get; }
}

public interface ITimeBasedPartitioner : IDataPartitioner  
{
    /// <summary>Column containing datetime values for partitioning.</summary>
    string TimeColumn { get; }

    /// <summary>Time granularity for partitions.</summary>
    TimePartitionGranularity Granularity { get; }
}

public interface IFilterBasedPartitioner : IDataPartitioner
{
    /// <summary>Filter expressions for each partition.</summary>
    IReadOnlyDictionary<string, string> FilterExpressions { get; }
}