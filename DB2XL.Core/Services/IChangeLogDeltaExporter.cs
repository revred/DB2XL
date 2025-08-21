using DB2XL.Core.Models;

namespace DB2XL.Core.Services;

/// <summary>
/// Service for incremental data export using trigger-based change log detection.
/// Uses a __changes table to track all data modifications with primary key references.
/// </summary>
public interface IChangeLogDeltaExporter
{
    /// <summary>
    /// Exports changes since the last checkpoint using change log entries.
    /// </summary>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="tableName">Table to export changes from</param>
    /// <param name="lastCheckpoint">Previous checkpoint to resume from (null for initial export)</param>
    /// <param name="options">Export options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Delta export result with new checkpoint information</returns>
    Task<DeltaExportResult> ExportDeltaAsync(
        string connectionString,
        string tableName,
        DeltaCheckpoint? lastCheckpoint,
        ChangeLogDeltaExportOptions options,
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// Gets the current maximum change log ID for establishing initial checkpoint.
    /// </summary>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="tableName">Table to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current checkpoint representing the latest change log state</returns>
    Task<DeltaCheckpoint> GetCurrentChangeLogCheckpointAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// Validates that change log infrastructure is properly configured for a table.
    /// </summary>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="tableName">Table to validate</param>
    /// <returns>Validation result with any issues found</returns>
    Task<ChangeLogValidationResult> ValidateChangeLogSetupAsync(
        string connectionString,
        string tableName);
        
    /// <summary>
    /// Sets up change log infrastructure (triggers and __changes table) for a table.
    /// </summary>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="tableName">Table to enable change tracking for</param>
    /// <param name="options">Setup options</param>
    /// <returns>Setup result</returns>
    Task<ChangeLogSetupResult> SetupChangeLogAsync(
        string connectionString,
        string tableName,
        ChangeLogSetupOptions? options = null);
        
    /// <summary>
    /// Removes change log infrastructure for a table.
    /// </summary>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="tableName">Table to disable change tracking for</param>
    /// <returns>True if successfully removed</returns>
    Task<bool> RemoveChangeLogAsync(
        string connectionString,
        string tableName);
}

/// <summary>
/// Options for change log-based delta export operations.
/// </summary>
public sealed record ChangeLogDeltaExportOptions
{
    /// <summary>Output directory for delta files.</summary>
    public required string OutputDirectory { get; init; }
    
    /// <summary>Export format (JSONL, Parquet, etc.).</summary>
    public ExportFormat Format { get; init; } = ExportFormat.Jsonl;
    
    /// <summary>Batch size for reading changes.</summary>
    public int BatchSize { get; init; } = 10_000;
    
    /// <summary>Include deleted rows in export.</summary>
    public bool IncludeDeleted { get; init; } = true;
    
    /// <summary>Maximum rows to export (0 = unlimited).</summary>
    public int MaxRows { get; init; } = 0;
    
    /// <summary>File naming pattern for delta exports.</summary>
    public string FileNamePattern { get; init; } = "{table}_changlog_{timestamp}_{sequence}";
    
    /// <summary>Whether to validate checkpoint consistency before export.</summary>
    public bool ValidateCheckpoint { get; init; } = true;
    
    /// <summary>Name of the change log table.</summary>
    public string ChangeLogTableName { get; init; } = "__changes";
    
    /// <summary>Whether to clean up processed change log entries.</summary>
    public bool CleanupProcessedEntries { get; init; } = false;
    
    /// <summary>Operation types to include (INSERT, UPDATE, DELETE).</summary>
    public IReadOnlyList<ChangeOperation> IncludeOperations { get; init; } = 
        new[] { ChangeOperation.Insert, ChangeOperation.Update, ChangeOperation.Delete };
}

/// <summary>
/// Options for setting up change log infrastructure.
/// </summary>
public sealed record ChangeLogSetupOptions
{
    /// <summary>Name of the change log table.</summary>
    public string ChangeLogTableName { get; init; } = "__changes";
    
    /// <summary>Whether to create triggers for INSERT operations.</summary>
    public bool TrackInserts { get; init; } = true;
    
    /// <summary>Whether to create triggers for UPDATE operations.</summary>
    public bool TrackUpdates { get; init; } = true;
    
    /// <summary>Whether to create triggers for DELETE operations.</summary>
    public bool TrackDeletes { get; init; } = true;
    
    /// <summary>Whether to store the full row data in change log (vs. just primary key).</summary>
    public bool StoreFul­lRowData { get; init; } = false;
    
    /// <summary>Maximum age of change log entries before cleanup (null = no cleanup).</summary>
    public TimeSpan? MaxChangeLogAge { get; init; } = TimeSpan.FromDays(30);
}

/// <summary>
/// Change operation types tracked in change log.
/// </summary>
public enum ChangeOperation
{
    /// <summary>Row was inserted.</summary>
    Insert,
    
    /// <summary>Row was updated.</summary>
    Update,
    
    /// <summary>Row was deleted.</summary>
    Delete
}

/// <summary>
/// Validation result for change log setup.
/// </summary>
public sealed record ChangeLogValidationResult
{
    /// <summary>Whether change log is properly configured.</summary>
    public bool IsValid { get; init; }
    
    /// <summary>Whether __changes table exists.</summary>
    public bool ChangeLogTableExists { get; init; }
    
    /// <summary>Detected primary key columns for the table.</summary>
    public IReadOnlyList<string> PrimaryKeyColumns { get; init; } = Array.Empty<string>();
    
    /// <summary>Triggers present for the table.</summary>
    public IReadOnlyList<string> ExistingTriggers { get; init; } = Array.Empty<string>();
    
    /// <summary>Operations currently being tracked.</summary>
    public IReadOnlyList<ChangeOperation> TrackedOperations { get; init; } = Array.Empty<ChangeOperation>();
    
    /// <summary>Validation errors that must be fixed.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    
    /// <summary>Warnings about potential issues.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    
    /// <summary>Suggestions for optimization.</summary>
    public IReadOnlyList<string> Suggestions { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Result of change log setup operation.
/// </summary>
public sealed record ChangeLogSetupResult
{
    /// <summary>Whether setup was successful.</summary>
    public bool IsSuccess { get; init; }
    
    /// <summary>Components that were created.</summary>
    public IReadOnlyList<string> CreatedComponents { get; init; } = Array.Empty<string>();
    
    /// <summary>Any errors encountered during setup.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    
    /// <summary>Any warnings generated during setup.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Represents a change log entry.
/// </summary>
public sealed record ChangeLogEntry
{
    /// <summary>Unique ID of the change log entry.</summary>
    public long ChangeId { get; init; }
    
    /// <summary>Name of the table that changed.</summary>
    public required string TableName { get; init; }
    
    /// <summary>Type of operation (INSERT, UPDATE, DELETE).</summary>
    public ChangeOperation Operation { get; init; }
    
    /// <summary>Primary key value(s) of the affected row.</summary>
    public required object PrimaryKeyValue { get; init; }
    
    /// <summary>Timestamp when the change occurred.</summary>
    public DateTime Timestamp { get; init; }
    
    /// <summary>Transaction ID (if available).</summary>
    public string? TransactionId { get; init; }
    
    /// <summary>Full row data (if StoreFul­lRowData was enabled).</summary>
    public string? RowData { get; init; }
}