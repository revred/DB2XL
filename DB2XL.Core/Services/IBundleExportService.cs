using DB2XL.Core.Models;
using ValidationResult = DB2XL.Core.Validation.ValidationResult;

namespace DB2XL.Core.Services;

/// <summary>
/// Main service interface for orchestrating bundle export operations.
/// Coordinates SQLite data extraction, partitioning, and multi-format output generation.
/// </summary>
public interface IBundleExportService
{
    /// <summary>
    /// Exports a SQLite database to a structured bundle with multiple output formats.
    /// Creates manifest files, partition tables into JSONL/Parquet, and generates Excel index.
    /// </summary>
    /// <param name="sqliteFilePath">Absolute path to the source SQLite database file</param>
    /// <param name="options">Bundle export configuration and options</param>
    /// <param name="cancellationToken">Cancellation token for long-running operations</param>
    /// <returns>Bundle export result with layout, statistics, and file paths</returns>
    /// <exception cref="ValidationException">When options validation fails</exception>
    /// <exception cref="BundleExportException">When export process encounters errors</exception>
    Task<BundleExportResult> ExportAsync(
        string sqliteFilePath,
        BundleExportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates bundle export options without performing the export.
    /// Useful for configuration validation and error reporting.
    /// </summary>
    /// <param name="options">Bundle export options to validate</param>
    /// <returns>Validation result with success status and detailed error messages</returns>
    ValidationResult ValidateOptions(BundleExportOptions options);

    /// <summary>
    /// Estimates the export operation complexity and resource requirements.
    /// Provides statistics for planning and progress tracking.
    /// </summary>
    /// <param name="sqliteFilePath">Path to SQLite database for analysis</param>
    /// <param name="options">Export options for estimation context</param>
    /// <param name="cancellationToken">Cancellation token for database analysis</param>
    /// <returns>Export estimation with table counts, row estimates, and size projections</returns>
    Task<BundleExportEstimate> EstimateAsync(
        string sqliteFilePath,
        BundleExportOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Comprehensive result of a bundle export operation.
/// Contains layout information, statistics, and file paths for verification.
/// </summary>
public sealed record BundleExportResult
{
    /// <summary>Bundle directory layout with all generated paths.</summary>
    public BundleLayout Layout { get; init; } = new();
    
    /// <summary>Export operation statistics and performance metrics.</summary>
    public BundleExportStatistics Statistics { get; init; } = new();
    
    /// <summary>Collection of all partition information for manifest generation.</summary>
    public IReadOnlyList<PartitionInfo> Partitions { get; init; } = Array.Empty<PartitionInfo>();
    
    /// <summary>Tables that were successfully exported.</summary>
    public IReadOnlyList<string> ExportedTables { get; init; } = Array.Empty<string>();
    
    /// <summary>Tables that were skipped due to filters or errors.</summary>
    public IReadOnlyList<string> SkippedTables { get; init; } = Array.Empty<string>();
    
    /// <summary>Path to the generated Excel index workbook (if created).</summary>
    public string? IndexWorkbookPath { get; init; }
    
    /// <summary>Paths to generated manifest files (schema.json, provenance.json, etc.).</summary>
    public IReadOnlyDictionary<string, string> ManifestPaths { get; init; } = 
        new Dictionary<string, string>();
    
    /// <summary>Total export operation duration.</summary>
    public TimeSpan Duration { get; init; }
    
    /// <summary>UTC timestamp when export completed.</summary>
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>Success indicator - true if all tables exported without errors.</summary>
    public bool IsSuccess => SkippedTables.Count == 0;
}

/// <summary>
/// Performance and operational statistics from bundle export.
/// Provides metrics for monitoring, optimization, and reporting.
/// </summary>
public sealed record BundleExportStatistics
{
    /// <summary>Total number of tables discovered in the database.</summary>
    public int TablesDiscovered { get; init; }
    
    /// <summary>Number of tables successfully exported.</summary>
    public int TablesExported { get; init; }
    
    /// <summary>Number of tables skipped (filtered out or errors).</summary>
    public int TablesSkipped { get; init; }
    
    /// <summary>Total number of data rows exported across all tables.</summary>
    public long TotalRowsExported { get; init; }
    
    /// <summary>Total number of partition files created.</summary>
    public int PartitionFilesCreated { get; init; }
    
    /// <summary>Total size of all generated files in bytes.</summary>
    public long TotalFileSizeBytes { get; init; }
    
    /// <summary>Peak memory usage during export operation (approximate).</summary>
    public long PeakMemoryUsageBytes { get; init; }
    
    /// <summary>Average rows processed per second across all tables.</summary>
    public double RowsPerSecond { get; init; }
    
    /// <summary>Time spent reading from SQLite database.</summary>
    public TimeSpan DatabaseReadTime { get; init; }
    
    /// <summary>Time spent writing output files (JSONL, Parquet, Excel).</summary>
    public TimeSpan FileWriteTime { get; init; }
    
    /// <summary>Time spent on hash calculation and verification.</summary>
    public TimeSpan HashingTime { get; init; }
    
    /// <summary>Number of warnings generated during export.</summary>
    public int WarningsGenerated { get; init; }
    
    /// <summary>Collection of warning messages for troubleshooting.</summary>
    public IReadOnlyList<string> WarningMessages { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Pre-export estimation of operation complexity and resource requirements.
/// Helps with planning, progress tracking, and resource allocation.
/// </summary>
public sealed record BundleExportEstimate
{
    /// <summary>Estimated total number of tables to be processed.</summary>
    public int EstimatedTableCount { get; init; }
    
    /// <summary>Estimated total number of rows across all tables.</summary>
    public long EstimatedTotalRows { get; init; }
    
    /// <summary>Estimated number of partition files that will be created.</summary>
    public int EstimatedPartitionCount { get; init; }
    
    /// <summary>Estimated total output size in bytes (JSONL + Parquet + Excel).</summary>
    public long EstimatedOutputSizeBytes { get; init; }
    
    /// <summary>Estimated operation duration based on row count and complexity.</summary>
    public TimeSpan EstimatedDuration { get; init; }
    
    /// <summary>Estimated peak memory usage during export.</summary>
    public long EstimatedMemoryUsageBytes { get; init; }
    
    /// <summary>Per-table breakdown of row counts and estimated sizes.</summary>
    public IReadOnlyList<TableSizeEstimate> TableEstimates { get; init; } = Array.Empty<TableSizeEstimate>();
    
    /// <summary>Source database file information.</summary>
    public DatabaseInfo DatabaseInfo { get; init; } = new();
    
    /// <summary>Complexity rating: Simple, Moderate, Complex, VeryComplex.</summary>
    public ExportComplexity Complexity { get; init; } = ExportComplexity.Simple;
    
    /// <summary>Recommended batch/chunk sizes for optimal performance.</summary>
    public PerformanceRecommendations Recommendations { get; init; } = new();
}

/// <summary>
/// Size and complexity estimation for an individual table.
/// </summary>
public sealed record TableSizeEstimate
{
    /// <summary>Name of the database table.</summary>
    public string TableName { get; init; } = string.Empty;
    
    /// <summary>Estimated number of rows in this table.</summary>
    public long EstimatedRows { get; init; }
    
    /// <summary>Estimated size of this table's data in bytes.</summary>
    public long EstimatedSizeBytes { get; init; }
    
    /// <summary>Number of columns in this table.</summary>
    public int ColumnCount { get; init; }
    
    /// <summary>Estimated number of partitions for this table.</summary>
    public int EstimatedPartitions { get; init; }
    
    /// <summary>Indicates if table has BLOB columns (affects processing time).</summary>
    public bool HasBlobColumns { get; init; }
    
    /// <summary>Estimated processing time for this table alone.</summary>
    public TimeSpan EstimatedProcessingTime { get; init; }
}

/// <summary>
/// Information about the source SQLite database file.
/// </summary>
public sealed record DatabaseInfo
{
    /// <summary>Absolute path to the SQLite database file.</summary>
    public string FilePath { get; init; } = string.Empty;
    
    /// <summary>Database file size in bytes.</summary>
    public long FileSizeBytes { get; init; }
    
    /// <summary>SQLite database version from PRAGMA user_version.</summary>
    public int UserVersion { get; init; }
    
    /// <summary>SQLite schema version from PRAGMA schema_version.</summary>
    public int SchemaVersion { get; init; }
    
    /// <summary>Journal mode (DELETE, WAL, etc.).</summary>
    public string JournalMode { get; init; } = string.Empty;
    
    /// <summary>Page size in bytes.</summary>
    public int PageSize { get; init; }
    
    /// <summary>Total number of pages in database.</summary>
    public long PageCount { get; init; }
    
    /// <summary>Database last modified timestamp.</summary>
    public DateTime LastModified { get; init; }
}

/// <summary>
/// Export operation complexity classification.
/// </summary>
public enum ExportComplexity
{
    /// <summary>Few tables, small row counts, minimal partitioning.</summary>
    Simple,
    
    /// <summary>Moderate number of tables and rows, some partitioning.</summary>
    Moderate,
    
    /// <summary>Many tables or large row counts, extensive partitioning.</summary>
    Complex,
    
    /// <summary>Very large dataset, complex partitioning, high resource requirements.</summary>
    VeryComplex
}

/// <summary>
/// Performance optimization recommendations based on database analysis.
/// </summary>
public sealed record PerformanceRecommendations
{
    /// <summary>Recommended batch size for reading rows from SQLite.</summary>
    public int RecommendedBatchSize { get; init; } = 25_000;
    
    /// <summary>Recommended number of tables to process concurrently.</summary>
    public int RecommendedConcurrency { get; init; } = 1;
    
    /// <summary>Whether to enable Parquet output based on data characteristics.</summary>
    public bool RecommendParquet { get; init; } = true;
    
    /// <summary>Whether to enable sample generation based on table sizes.</summary>
    public bool RecommendSamples { get; init; } = true;
    
    /// <summary>Recommended partitioning strategy for large tables.</summary>
    public PartitionStrategy RecommendedPartitioning { get; init; } = PartitionStrategy.None;
    
    /// <summary>Performance warnings or optimization suggestions.</summary>
    public IReadOnlyList<string> Suggestions { get; init; } = Array.Empty<string>();
}