using DB2XL.Core.Models;

namespace DB2XL.Core.Services;

/// <summary>
/// Service for exporting SQLite data to Apache Parquet format.
/// Provides high-performance columnar storage optimized for analytical workloads.
/// </summary>
public interface IParquetExportEngine
{
    /// <summary>
    /// Exports a data partition to Parquet format with advanced compression and optimization.
    /// </summary>
    /// <param name="partition">Data partition containing rows to export</param>
    /// <param name="outputPath">Output path for the Parquet file</param>
    /// <param name="options">Parquet export configuration options</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Parquet export result with file metadata and statistics</returns>
    Task<ParquetExportResult> ExportPartitionAsync(
        DataPartition partition,
        string outputPath,
        ParquetExportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports table data directly from SQLite to Parquet format with streaming processing.
    /// </summary>
    /// <param name="connectionString">SQLite database connection string</param>
    /// <param name="tableName">Name of the table to export</param>
    /// <param name="outputPath">Output path for the Parquet file</param>
    /// <param name="options">Parquet export configuration options</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Parquet export result with file metadata and statistics</returns>
    Task<ParquetExportResult> ExportTableAsync(
        string connectionString,
        string tableName,
        string outputPath,
        ParquetExportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates Parquet export options for correctness and compatibility.
    /// </summary>
    /// <param name="options">Parquet export options to validate</param>
    /// <returns>Validation result with any errors or warnings</returns>
    ParquetExportValidation ValidateOptions(ParquetExportOptions options);

    /// <summary>
    /// Estimates the output file size and processing requirements for a Parquet export.
    /// </summary>
    /// <param name="rowCount">Number of rows to export</param>
    /// <param name="columns">Table column information</param>
    /// <param name="averageRowSizeBytes">Estimated average row size in bytes</param>
    /// <param name="options">Parquet export options</param>
    /// <returns>Export estimation with size and performance projections</returns>
    ParquetExportEstimation EstimateExport(
        long rowCount,
        IReadOnlyList<ColumnInfo> columns,
        double averageRowSizeBytes,
        ParquetExportOptions options);
}

/// <summary>
/// Configuration options for Parquet export operations.
/// </summary>
public sealed record ParquetExportOptions
{
    /// <summary>
    /// Parquet compression algorithm to use.
    /// </summary>
    public ParquetCompression Compression { get; init; } = ParquetCompression.Snappy;

    /// <summary>
    /// Parquet file format version.
    /// </summary>
    public ParquetVersion Version { get; init; } = ParquetVersion.V2_4;

    /// <summary>
    /// Number of rows per row group for optimal I/O performance.
    /// </summary>
    public int RowGroupSize { get; init; } = 100_000;

    /// <summary>
    /// Maximum size of each row group in bytes.
    /// </summary>
    public long MaxRowGroupSizeBytes { get; init; } = 128 * 1024 * 1024; // 128MB

    /// <summary>
    /// Data page size for column chunks.
    /// </summary>
    public int PageSize { get; init; } = 1024 * 1024; // 1MB

    /// <summary>
    /// Enable dictionary encoding for string columns.
    /// </summary>
    public bool EnableDictionaryEncoding { get; init; } = true;

    /// <summary>
    /// Enable statistics collection for columns.
    /// </summary>
    public bool EnableStatistics { get; init; } = true;

    /// <summary>
    /// Write schema metadata to the Parquet file.
    /// </summary>
    public bool IncludeSchemaMetadata { get; init; } = true;

    /// <summary>
    /// Custom metadata to include in the Parquet file.
    /// </summary>
    public IReadOnlyDictionary<string, string> CustomMetadata { get; init; } = 
        new Dictionary<string, string>();

    /// <summary>
    /// Batch size for reading data from source.
    /// </summary>
    public int BatchSize { get; init; } = 10_000;

    /// <summary>
    /// Type conversion strategy for SQLite to Parquet mapping.
    /// </summary>
    public ParquetTypeStrategy TypeStrategy { get; init; } = ParquetTypeStrategy.Optimized;

    /// <summary>
    /// Decimal precision for numeric columns.
    /// </summary>
    public int DecimalPrecision { get; init; } = 18;

    /// <summary>
    /// Decimal scale for numeric columns.
    /// </summary>
    public int DecimalScale { get; init; } = 4;

    /// <summary>
    /// Enable bloom filters for supported column types.
    /// </summary>
    public bool EnableBloomFilters { get; init; } = false;

    /// <summary>
    /// Bloom filter false positive probability.
    /// </summary>
    public double BloomFilterFpp { get; init; } = 0.01;
}

/// <summary>
/// Parquet compression algorithm options.
/// </summary>
public enum ParquetCompression
{
    /// <summary>No compression - fastest write, largest size.</summary>
    None,
    
    /// <summary>Snappy compression - good balance of speed and size.</summary>
    Snappy,
    
    /// <summary>GZIP compression - smaller size, slower processing.</summary>
    Gzip,
    
    /// <summary>LZ4 compression - fast compression with good ratio.</summary>
    Lz4,
    
    /// <summary>ZSTD compression - excellent compression ratio.</summary>
    Zstd,
    
    /// <summary>Brotli compression - high compression ratio.</summary>
    Brotli
}

/// <summary>
/// Parquet file format version options.
/// </summary>
public enum ParquetVersion
{
    /// <summary>Parquet format version 1.0.</summary>
    V1_0,
    
    /// <summary>Parquet format version 2.4 - recommended.</summary>
    V2_4,
    
    /// <summary>Parquet format version 2.6 - latest features.</summary>
    V2_6
}

/// <summary>
/// Type conversion strategy for SQLite to Parquet mapping.
/// </summary>
public enum ParquetTypeStrategy
{
    /// <summary>Preserve SQLite types as closely as possible.</summary>
    Preserve,
    
    /// <summary>Optimize types for Parquet performance and storage.</summary>
    Optimized,
    
    /// <summary>Use strict type conversion with validation.</summary>
    Strict
}

/// <summary>
/// Result of a Parquet export operation.
/// </summary>
public sealed record ParquetExportResult
{
    /// <summary>
    /// Whether the export completed successfully.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Path to the exported Parquet file.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Number of rows exported to the Parquet file.
    /// </summary>
    public long RowsExported { get; init; }

    /// <summary>
    /// Size of the exported Parquet file in bytes.
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// Number of row groups created in the Parquet file.
    /// </summary>
    public int RowGroupCount { get; init; }

    /// <summary>
    /// Compression ratio achieved (original size / compressed size).
    /// </summary>
    public double CompressionRatio { get; init; }

    /// <summary>
    /// Time taken to export the data.
    /// </summary>
    public TimeSpan ExportDuration { get; init; }

    /// <summary>
    /// Parquet file metadata and schema information.
    /// </summary>
    public ParquetFileMetadata Metadata { get; init; } = new() 
    { 
        Schema = string.Empty,
        Version = string.Empty, 
        Compression = string.Empty 
    };

    /// <summary>
    /// Export errors encountered during processing.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Export warnings for review.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Column-level export statistics.
    /// </summary>
    public IReadOnlyList<ParquetColumnStats> ColumnStatistics { get; init; } = Array.Empty<ParquetColumnStats>();
}

/// <summary>
/// Parquet file metadata and schema information.
/// </summary>
public sealed record ParquetFileMetadata
{
    /// <summary>
    /// Parquet schema definition.
    /// </summary>
    public required string Schema { get; init; } = string.Empty;

    /// <summary>
    /// Number of columns in the schema.
    /// </summary>
    public int ColumnCount { get; init; }

    /// <summary>
    /// Total number of rows across all row groups.
    /// </summary>
    public long TotalRows { get; init; }

    /// <summary>
    /// Parquet format version used.
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Compression algorithm used.
    /// </summary>
    public string Compression { get; init; } = string.Empty;

    /// <summary>
    /// Custom metadata included in the file.
    /// </summary>
    public IReadOnlyDictionary<string, string> CustomMetadata { get; init; } = 
        new Dictionary<string, string>();

    /// <summary>
    /// Created timestamp for the Parquet file.
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Column-level statistics for Parquet export.
/// </summary>
public sealed record ParquetColumnStats
{
    /// <summary>
    /// Column name.
    /// </summary>
    public required string ColumnName { get; init; }

    /// <summary>
    /// Parquet data type for the column.
    /// </summary>
    public required string ParquetType { get; init; }

    /// <summary>
    /// Number of non-null values in the column.
    /// </summary>
    public long NonNullCount { get; init; }

    /// <summary>
    /// Number of distinct values in the column (if statistics enabled).
    /// </summary>
    public long? DistinctCount { get; init; }

    /// <summary>
    /// Minimum value in the column (if applicable).
    /// </summary>
    public string? MinValue { get; init; }

    /// <summary>
    /// Maximum value in the column (if applicable).
    /// </summary>
    public string? MaxValue { get; init; }

    /// <summary>
    /// Average size of values in the column (in bytes).
    /// </summary>
    public double AverageSize { get; init; }

    /// <summary>
    /// Whether dictionary encoding was used for this column.
    /// </summary>
    public bool DictionaryEncoded { get; init; }

    /// <summary>
    /// Compression ratio achieved for this column.
    /// </summary>
    public double CompressionRatio { get; init; }
}

/// <summary>
/// Parquet export options validation result.
/// </summary>
public sealed record ParquetExportValidation
{
    /// <summary>
    /// Whether the options are valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Validation errors that must be addressed.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Validation warnings for consideration.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Performance recommendations based on options.
    /// </summary>
    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Estimation for Parquet export operation.
/// </summary>
public sealed record ParquetExportEstimation
{
    /// <summary>
    /// Estimated output file size in bytes.
    /// </summary>
    public long EstimatedFileSizeBytes { get; init; }

    /// <summary>
    /// Estimated number of row groups.
    /// </summary>
    public int EstimatedRowGroups { get; init; }

    /// <summary>
    /// Estimated processing time.
    /// </summary>
    public TimeSpan EstimatedDuration { get; init; }

    /// <summary>
    /// Expected compression ratio.
    /// </summary>
    public double ExpectedCompressionRatio { get; init; }

    /// <summary>
    /// Estimated memory usage during export.
    /// </summary>
    public long EstimatedMemoryUsageBytes { get; init; }

    /// <summary>
    /// Performance characteristics of the export.
    /// </summary>
    public IReadOnlyList<string> PerformanceNotes { get; init; } = Array.Empty<string>();
}

