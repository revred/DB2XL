using DB2XL.Core.Models;

namespace DB2XL.Core.Services;

/// <summary>
/// Core interface for exporting data partitions to JSONL format.
/// Provides schema tracking, deterministic serialization, and streaming output.
/// </summary>
public interface IJsonlExportEngine
{
    /// <summary>
    /// Exports a data partition to a JSONL file with comprehensive metadata tracking.
    /// </summary>
    /// <param name="partition">Data partition to export</param>
    /// <param name="outputFilePath">Target JSONL file path</param>
    /// <param name="options">Export configuration options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Export result with metadata and statistics</returns>
    Task<JsonlExportResult> ExportPartitionAsync(
        DataPartition partition,
        string outputFilePath,
        JsonlExportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports multiple partitions in parallel with coordinated schema tracking.
    /// </summary>
    /// <param name="partitions">Partitions to export</param>
    /// <param name="outputDirectory">Base output directory</param>
    /// <param name="options">Export configuration options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of export results</returns>
    Task<IReadOnlyList<JsonlExportResult>> ExportPartitionsAsync(
        IAsyncEnumerable<DataPartition> partitions,
        string outputDirectory,
        JsonlExportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates schema manifest from exported data for LLM processing.
    /// </summary>
    /// <param name="exportResults">Results from partition exports</param>
    /// <param name="tableMetadata">Original table metadata</param>
    /// <returns>Schema manifest with field definitions and statistics</returns>
    Task<JsonlSchemaManifest> GenerateSchemaManifestAsync(
        IReadOnlyList<JsonlExportResult> exportResults,
        TableMetadata tableMetadata);

    /// <summary>
    /// Validates JSONL file integrity and schema consistency.
    /// </summary>
    /// <param name="filePath">JSONL file to validate</param>
    /// <param name="expectedSchema">Expected schema for validation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with detailed diagnostics</returns>
    Task<JsonlValidationResult> ValidateJsonlFileAsync(
        string filePath,
        JsonlSchemaManifest expectedSchema,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration options for JSONL export operations.
/// </summary>
public sealed class JsonlExportOptions
{
    /// <summary>
    /// JSON serialization settings (indented vs compact).
    /// </summary>
    public JsonSerializationMode SerializationMode { get; init; } = JsonSerializationMode.Compact;

    /// <summary>
    /// Include schema information in each JSONL file header.
    /// </summary>
    public bool IncludeSchemaHeader { get; init; } = true;

    /// <summary>
    /// Maximum file size before splitting (0 = no limit).
    /// </summary>
    public long MaxFileSizeBytes { get; init; } = 0;

    /// <summary>
    /// Include row checksums for data integrity verification.
    /// </summary>
    public bool IncludeRowChecksums { get; init; } = false;

    /// <summary>
    /// Null value representation in JSON.
    /// </summary>
    public JsonNullHandling NullHandling { get; init; } = JsonNullHandling.Null;

    /// <summary>
    /// Date/time format for serialization.
    /// </summary>
    public JsonDateTimeFormat DateTimeFormat { get; init; } = JsonDateTimeFormat.ISO8601;

    /// <summary>
    /// Include provenance metadata (export timestamp, table info).
    /// </summary>
    public bool IncludeProvenance { get; init; } = true;

    /// <summary>
    /// Enable parallel processing for large partitions.
    /// </summary>
    public bool EnableParallelProcessing { get; init; } = true;

    /// <summary>
    /// Maximum degree of parallelism for concurrent exports.
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// Buffer size for streaming writes (bytes).
    /// </summary>
    public int WriteBufferSize { get; init; } = 64 * 1024; // 64KB

    /// <summary>
    /// File encoding for JSONL output.
    /// </summary>
    public JsonlEncoding Encoding { get; init; } = JsonlEncoding.UTF8;

    /// <summary>
    /// Compression mode for output files.
    /// </summary>
    public JsonlCompression Compression { get; init; } = JsonlCompression.None;

    /// <summary>
    /// Include data type annotations in JSON objects.
    /// </summary>
    public bool IncludeTypeAnnotations { get; init; } = false;
}

/// <summary>
/// Result of a JSONL export operation with comprehensive metadata.
/// </summary>
public sealed record JsonlExportResult
{
    /// <summary>
    /// Path to the exported JSONL file.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// Original partition information.
    /// </summary>
    public PartitionInfo PartitionInfo { get; init; } = new();

    /// <summary>
    /// Number of records successfully exported.
    /// </summary>
    public long RecordCount { get; init; }

    /// <summary>
    /// Size of the exported file in bytes.
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// SHA-256 checksum of the file content.
    /// </summary>
    public string FileChecksum { get; init; } = string.Empty;

    /// <summary>
    /// Export start timestamp (UTC).
    /// </summary>
    public DateTime ExportStartTime { get; init; }

    /// <summary>
    /// Export completion timestamp (UTC).
    /// </summary>
    public DateTime ExportEndTime { get; init; }

    /// <summary>
    /// Schema information discovered during export.
    /// </summary>
    public JsonlSchemaInfo SchemaInfo { get; init; } = new();

    /// <summary>
    /// Any warnings or issues encountered during export.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Export performance metrics.
    /// </summary>
    public JsonlExportMetrics Metrics { get; init; } = new();

    /// <summary>
    /// Whether the export completed successfully.
    /// </summary>
    public bool IsSuccessful { get; init; } = true;

    /// <summary>
    /// Error message if export failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Schema information discovered from exported JSONL data.
/// </summary>
public sealed record JsonlSchemaInfo
{
    /// <summary>
    /// Field definitions with types and statistics.
    /// </summary>
    public IReadOnlyList<JsonlFieldDefinition> Fields { get; init; } = Array.Empty<JsonlFieldDefinition>();

    /// <summary>
    /// Primary key fields identified in the data.
    /// </summary>
    public IReadOnlyList<string> PrimaryKeyFields { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Estimated data types for each field.
    /// </summary>
    public IReadOnlyDictionary<string, JsonDataType> FieldTypes { get; init; } = new Dictionary<string, JsonDataType>();

    /// <summary>
    /// Field nullability statistics.
    /// </summary>
    public IReadOnlyDictionary<string, double> NullPercentages { get; init; } = new Dictionary<string, double>();

    /// <summary>
    /// Unique value counts per field.
    /// </summary>
    public IReadOnlyDictionary<string, long> UniqueValueCounts { get; init; } = new Dictionary<string, long>();

    /// <summary>
    /// Sample values for each field (for schema documentation).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<object?>> SampleValues { get; init; } = new Dictionary<string, IReadOnlyList<object?>>();
}

/// <summary>
/// Performance metrics for JSONL export operations.
/// </summary>
public sealed record JsonlExportMetrics
{
    /// <summary>
    /// Records processed per second.
    /// </summary>
    public double RecordsPerSecond { get; init; }

    /// <summary>
    /// Bytes written per second.
    /// </summary>
    public double BytesPerSecond { get; init; }

    /// <summary>
    /// Peak memory usage during export (bytes).
    /// </summary>
    public long PeakMemoryUsage { get; init; }

    /// <summary>
    /// Total CPU time consumed.
    /// </summary>
    public TimeSpan CpuTime { get; init; }

    /// <summary>
    /// Time spent on I/O operations.
    /// </summary>
    public TimeSpan IoTime { get; init; }

    /// <summary>
    /// Time spent on JSON serialization.
    /// </summary>
    public TimeSpan SerializationTime { get; init; }

    /// <summary>
    /// Number of garbage collections triggered.
    /// </summary>
    public int GarbageCollections { get; init; }
}

/// <summary>
/// JSONL field definition with type information and statistics.
/// </summary>
public sealed record JsonlFieldDefinition
{
    /// <summary>
    /// Field name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Detected JSON data type.
    /// </summary>
    public JsonDataType DataType { get; init; }

    /// <summary>
    /// Whether the field can contain null values.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    /// Whether this field is part of the primary key.
    /// </summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>
    /// Field description or documentation.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Example values for documentation.
    /// </summary>
    public IReadOnlyList<object?> Examples { get; init; } = Array.Empty<object?>();

    /// <summary>
    /// Value distribution statistics.
    /// </summary>
    public JsonlFieldStatistics Statistics { get; init; } = new();
}

/// <summary>
/// Statistical information about a JSONL field.
/// </summary>
public sealed record JsonlFieldStatistics
{
    /// <summary>
    /// Total number of non-null values.
    /// </summary>
    public long NonNullCount { get; init; }

    /// <summary>
    /// Number of unique values.
    /// </summary>
    public long UniqueCount { get; init; }

    /// <summary>
    /// Most common values and their frequencies.
    /// </summary>
    public IReadOnlyDictionary<string, long> ValueFrequencies { get; init; } = new Dictionary<string, long>();

    /// <summary>
    /// Minimum value (for numeric/string fields).
    /// </summary>
    public object? MinValue { get; init; }

    /// <summary>
    /// Maximum value (for numeric/string fields).
    /// </summary>
    public object? MaxValue { get; init; }

    /// <summary>
    /// Average length for string fields.
    /// </summary>
    public double? AverageLength { get; init; }
}

/// <summary>
/// Schema manifest for a table exported to JSONL format.
/// </summary>
public sealed record JsonlSchemaManifest
{
    /// <summary>
    /// Table name.
    /// </summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>
    /// Schema version for compatibility tracking.
    /// </summary>
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>
    /// Export timestamp.
    /// </summary>
    public DateTime ExportTimestamp { get; init; }

    /// <summary>
    /// Field definitions.
    /// </summary>
    public IReadOnlyList<JsonlFieldDefinition> Fields { get; init; } = Array.Empty<JsonlFieldDefinition>();

    /// <summary>
    /// Partition file paths and metadata.
    /// </summary>
    public IReadOnlyList<JsonlPartitionManifest> Partitions { get; init; } = Array.Empty<JsonlPartitionManifest>();

    /// <summary>
    /// Total record count across all partitions.
    /// </summary>
    public long TotalRecordCount { get; init; }

    /// <summary>
    /// Table-level metadata and statistics.
    /// </summary>
    public IReadOnlyDictionary<string, object> TableMetadata { get; init; } = new Dictionary<string, object>();

    /// <summary>
    /// Recommended LLM processing strategies.
    /// </summary>
    public JsonlProcessingRecommendations ProcessingRecommendations { get; init; } = new();
}

/// <summary>
/// Partition information within a schema manifest.
/// </summary>
public sealed record JsonlPartitionManifest
{
    /// <summary>
    /// Relative path to the JSONL file.
    /// </summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>
    /// Partition label/identifier.
    /// </summary>
    public string PartitionLabel { get; init; } = string.Empty;

    /// <summary>
    /// Record count in this partition.
    /// </summary>
    public long RecordCount { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// File checksum for integrity verification.
    /// </summary>
    public string Checksum { get; init; } = string.Empty;

    /// <summary>
    /// Partition-specific metadata.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Recommendations for LLM processing of the exported data.
/// </summary>
public sealed record JsonlProcessingRecommendations
{
    /// <summary>
    /// Recommended batch size for LLM processing.
    /// </summary>
    public int RecommendedBatchSize { get; init; }

    /// <summary>
    /// Fields that may contain sensitive information.
    /// </summary>
    public IReadOnlyList<string> SensitiveFields { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Fields suitable for semantic search/embedding.
    /// </summary>
    public IReadOnlyList<string> SearchableFields { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Suggested data sampling strategy.
    /// </summary>
    public string? SamplingStrategy { get; init; }

    /// <summary>
    /// Estimated token count for LLM context estimation.
    /// </summary>
    public long EstimatedTokenCount { get; init; }

    /// <summary>
    /// Processing complexity score (1-10).
    /// </summary>
    public int ComplexityScore { get; init; }
}

/// <summary>
/// Result of JSONL file validation.
/// </summary>
public sealed record JsonlValidationResult
{
    /// <summary>
    /// Whether the file passed validation.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Validation errors found.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Validation warnings.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Detailed validation metrics.
    /// </summary>
    public JsonlValidationMetrics Metrics { get; init; } = new();
}

/// <summary>
/// Detailed metrics from JSONL validation.
/// </summary>
public sealed record JsonlValidationMetrics
{
    /// <summary>
    /// Number of lines validated.
    /// </summary>
    public long LinesValidated { get; init; }

    /// <summary>
    /// Number of valid JSON objects.
    /// </summary>
    public long ValidObjects { get; init; }

    /// <summary>
    /// Number of schema violations.
    /// </summary>
    public long SchemaViolations { get; init; }

    /// <summary>
    /// Time taken for validation.
    /// </summary>
    public TimeSpan ValidationTime { get; init; }
}

/// <summary>
/// JSON data types detected in JSONL fields.
/// </summary>
public enum JsonDataType
{
    Null,
    Boolean,
    Integer,
    Number,
    String,
    Array,
    Object,
    Mixed
}

/// <summary>
/// JSON serialization modes.
/// </summary>
public enum JsonSerializationMode
{
    Compact,
    Indented
}

/// <summary>
/// JSON null value handling strategies.
/// </summary>
public enum JsonNullHandling
{
    Null,        // Standard JSON null
    EmptyString, // Empty string ""
    Skip         // Omit the field entirely
}

/// <summary>
/// Date/time serialization formats.
/// </summary>
public enum JsonDateTimeFormat
{
    ISO8601,     // "2025-01-15T10:30:00Z"
    Unix,        // Unix timestamp (seconds)
    UnixMillis,  // Unix timestamp (milliseconds)
    Ticks        // .NET ticks
}

/// <summary>
/// JSONL file encoding options.
/// </summary>
public enum JsonlEncoding
{
    UTF8,
    UTF8NoBOM,
    ASCII
}

/// <summary>
/// JSONL compression options.
/// </summary>
public enum JsonlCompression
{
    None,
    Gzip,
    Brotli
}