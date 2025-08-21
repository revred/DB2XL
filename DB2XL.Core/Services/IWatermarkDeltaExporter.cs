using DB2XL.Core.Models;

namespace DB2XL.Core.Services;

/// <summary>
/// Service for incremental data export using watermark-based change detection.
/// Tracks changes using timestamp columns and maintains checkpoints for efficient delta operations.
/// </summary>
public interface IWatermarkDeltaExporter
{
    /// <summary>
    /// Exports changes since the last checkpoint using watermark column comparison.
    /// </summary>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="tableName">Table to export changes from</param>
    /// <param name="watermarkColumn">Column to use for change detection (typically updated_at)</param>
    /// <param name="lastCheckpoint">Previous checkpoint to resume from (null for initial export)</param>
    /// <param name="options">Export options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Delta export result with new checkpoint information</returns>
    Task<DeltaExportResult> ExportDeltaAsync(
        string connectionString,
        string tableName,
        string watermarkColumn,
        DeltaCheckpoint? lastCheckpoint,
        DeltaExportOptions options,
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// Gets the current maximum watermark value for establishing initial checkpoint.
    /// </summary>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="tableName">Table to analyze</param>
    /// <param name="watermarkColumn">Watermark column name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current checkpoint representing the latest state</returns>
    Task<DeltaCheckpoint> GetCurrentCheckpointAsync(
        string connectionString,
        string tableName,
        string watermarkColumn,
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// Validates that a table and column are suitable for watermark-based delta export.
    /// </summary>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="tableName">Table to validate</param>
    /// <param name="watermarkColumn">Column to validate</param>
    /// <returns>Validation result with any issues found</returns>
    Task<DeltaValidationResult> ValidateWatermarkSetupAsync(
        string connectionString,
        string tableName,
        string watermarkColumn);
}

/// <summary>
/// Checkpoint information for resuming delta exports.
/// </summary>
public sealed record DeltaCheckpoint
{
    /// <summary>Table name this checkpoint applies to.</summary>
    public required string TableName { get; init; }
    
    /// <summary>Watermark column used for change detection.</summary>
    public required string WatermarkColumn { get; init; }
    
    /// <summary>Last watermark value processed (timestamp or version number).</summary>
    public object? LastWatermarkValue { get; init; }
    
    /// <summary>Last primary key value for tie-breaking rows with same watermark.</summary>
    public object? LastPrimaryKeyValue { get; init; }
    
    /// <summary>When this checkpoint was created.</summary>
    public DateTime CheckpointTimestamp { get; init; } = DateTime.UtcNow;
    
    /// <summary>Total rows processed up to this checkpoint.</summary>
    public long RowsProcessed { get; init; }
    
    /// <summary>Hash of the selection/filter criteria for validation.</summary>
    public string SelectionHash { get; init; } = string.Empty;
}

/// <summary>
/// Result of a delta export operation.
/// </summary>
public sealed record DeltaExportResult
{
    /// <summary>Whether the export completed successfully.</summary>
    public bool IsSuccess { get; init; }
    
    /// <summary>Number of new/changed rows exported.</summary>
    public long RowsExported { get; init; }
    
    /// <summary>New checkpoint for next incremental run.</summary>
    public DeltaCheckpoint? NewCheckpoint { get; init; }
    
    /// <summary>Paths to exported files.</summary>
    public IReadOnlyList<string> ExportedFiles { get; init; } = Array.Empty<string>();
    
    /// <summary>Time range of exported data.</summary>
    public DateTimeRange? DataTimeRange { get; init; }
    
    /// <summary>Export duration.</summary>
    public TimeSpan Duration { get; init; }
    
    /// <summary>Any errors encountered.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    
    /// <summary>Any warnings generated.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Options for delta export operations.
/// </summary>
public sealed record DeltaExportOptions
{
    /// <summary>Output directory for delta files.</summary>
    public required string OutputDirectory { get; init; }
    
    /// <summary>Export format (JSONL, Parquet, etc.).</summary>
    public ExportFormat Format { get; init; } = ExportFormat.Jsonl;
    
    /// <summary>Primary key columns for tie-breaking (auto-detected if not specified).</summary>
    public IReadOnlyList<string> PrimaryKeyColumns { get; init; } = Array.Empty<string>();
    
    /// <summary>Batch size for reading changes.</summary>
    public int BatchSize { get; init; } = 10_000;
    
    /// <summary>Include deleted rows if change tracking is available.</summary>
    public bool IncludeDeleted { get; init; } = false;
    
    /// <summary>Maximum rows to export (0 = unlimited).</summary>
    public int MaxRows { get; init; } = 0;
    
    /// <summary>File naming pattern for delta exports.</summary>
    public string FileNamePattern { get; init; } = "{table}_delta_{timestamp}_{sequence}";
    
    /// <summary>Whether to validate checkpoint consistency before export.</summary>
    public bool ValidateCheckpoint { get; init; } = true;
}

/// <summary>
/// Validation result for watermark setup.
/// </summary>
public sealed record DeltaValidationResult
{
    /// <summary>Whether the setup is valid for delta export.</summary>
    public bool IsValid { get; init; }
    
    /// <summary>Detected primary key columns.</summary>
    public IReadOnlyList<string> PrimaryKeyColumns { get; init; } = Array.Empty<string>();
    
    /// <summary>Watermark column data type.</summary>
    public string WatermarkColumnType { get; init; } = string.Empty;
    
    /// <summary>Whether the watermark column has an index.</summary>
    public bool WatermarkColumnIndexed { get; init; }
    
    /// <summary>Validation errors that must be fixed.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    
    /// <summary>Warnings about potential issues.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    
    /// <summary>Suggestions for optimization.</summary>
    public IReadOnlyList<string> Suggestions { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Represents a time range for exported data.
/// </summary>
public sealed record DateTimeRange(DateTime Start, DateTime End)
{
    /// <summary>Duration of the range.</summary>
    public TimeSpan Duration => End - Start;
}

/// <summary>
/// Export format options.
/// </summary>
public enum ExportFormat
{
    /// <summary>JSON Lines format.</summary>
    Jsonl,
    
    /// <summary>Apache Parquet format.</summary>
    Parquet,
    
    /// <summary>Comma-separated values.</summary>
    Csv,
    
    /// <summary>Excel workbook.</summary>
    Excel
}