using Microsoft.Data.Sqlite;
using DB2XL.Query;

namespace DB2XL.DeltaExport;

/// <summary>
/// Strategy for detecting changes in database tables for delta exports
/// </summary>
public enum DeltaStrategy
{
    /// <summary>
    /// Use timestamp-based watermark filtering (requires timestamp column)
    /// </summary>
    Watermark,
    
    /// <summary>
    /// Use change log table with triggers to track modifications
    /// </summary>
    ChangeLog,
    
    /// <summary>
    /// Export all data (full export, no delta)
    /// </summary>
    Full
}

/// <summary>
/// Represents a checkpoint in time for delta exports
/// </summary>
public sealed record DeltaCheckpoint
{
    /// <summary>
    /// Unique identifier for this checkpoint
    /// </summary>
    public string CheckpointId { get; init; } = string.Empty;
    
    /// <summary>
    /// Table this checkpoint applies to
    /// </summary>
    public string TableName { get; init; } = string.Empty;
    
    /// <summary>
    /// Strategy used for this checkpoint
    /// </summary>
    public DeltaStrategy Strategy { get; init; }
    
    /// <summary>
    /// Timestamp when this checkpoint was created
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// Last processed value(s) for watermark strategy
    /// Key = column name, Value = last processed value
    /// </summary>
    public Dictionary<string, object?> WatermarkValues { get; init; } = new();
    
    /// <summary>
    /// Last processed change log sequence ID for changelog strategy
    /// </summary>
    public long? LastChangeLogId { get; init; }
    
    /// <summary>
    /// Number of rows processed in this checkpoint
    /// </summary>
    public long RowsProcessed { get; init; }
    
    /// <summary>
    /// Additional metadata for the checkpoint
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Configuration for delta export operations
/// </summary>
public sealed record DeltaExportConfig
{
    /// <summary>
    /// Strategy to use for delta detection
    /// </summary>
    public DeltaStrategy Strategy { get; init; } = DeltaStrategy.Watermark;
    
    /// <summary>
    /// Column name(s) to use for watermark strategy
    /// For composite watermarks, order matters
    /// </summary>
    public IReadOnlyList<string> WatermarkColumns { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Selection grammar for filtering the delta query
    /// Applied after delta filtering
    /// </summary>
    public ISelectionGrammar? AdditionalFilter { get; init; }
    
    /// <summary>
    /// Maximum number of rows to return in a single delta export
    /// </summary>
    public int? MaxRows { get; init; }
    
    /// <summary>
    /// Whether to include deleted rows (only applies to ChangeLog strategy)
    /// </summary>
    public bool IncludeDeletes { get; init; } = true;
    
    /// <summary>
    /// Custom ordering for delta results (applied after delta filtering)
    /// If null, uses discovered primary key ordering
    /// </summary>
    public IReadOnlyList<IOrderByClause>? CustomOrdering { get; init; }
    
    /// <summary>
    /// Configuration for change log strategy
    /// </summary>
    public ChangeLogConfig? ChangeLogConfig { get; init; }
}

/// <summary>
/// Configuration for change log delta strategy
/// </summary>
public sealed record ChangeLogConfig
{
    /// <summary>
    /// Name of the change log table (default: __changes)
    /// </summary>
    public string ChangeLogTableName { get; init; } = "__changes";
    
    /// <summary>
    /// Whether to automatically install triggers if they don't exist
    /// </summary>
    public bool AutoInstallTriggers { get; init; } = true;
    
    /// <summary>
    /// Whether to capture the full row data in change log
    /// If false, only captures primary key and operation type
    /// </summary>
    public bool CaptureFullRowData { get; init; } = false;
    
    /// <summary>
    /// Maximum age of change log entries to retain (in days)
    /// Older entries will be cleaned up
    /// </summary>
    public int? RetentionDays { get; init; } = 30;
}

/// <summary>
/// Result of a delta export operation
/// </summary>
public sealed record DeltaExportResult
{
    /// <summary>
    /// Checkpoint representing the state after this export
    /// </summary>
    public DeltaCheckpoint Checkpoint { get; init; } = new();
    
    /// <summary>
    /// Number of rows exported
    /// </summary>
    public long RowsExported { get; init; }
    
    /// <summary>
    /// Whether more data is available (pagination)
    /// </summary>
    public bool HasMoreData { get; init; }
    
    /// <summary>
    /// Time taken for the export operation
    /// </summary>
    public TimeSpan ElapsedTime { get; init; }
    
    /// <summary>
    /// SQL query that was executed for the delta export
    /// </summary>
    public string ExecutedQuery { get; init; } = string.Empty;
    
    /// <summary>
    /// Parameters used in the executed query
    /// </summary>
    public Dictionary<string, object?> QueryParameters { get; init; } = new();
}

/// <summary>
/// Service for managing delta export checkpoints
/// </summary>
public interface IDeltaCheckpointService
{
    /// <summary>
    /// Saves a checkpoint to persistent storage
    /// </summary>
    /// <param name="checkpoint">Checkpoint to save</param>
    /// <returns>Task representing the save operation</returns>
    Task SaveCheckpointAsync(DeltaCheckpoint checkpoint);
    
    /// <summary>
    /// Loads the latest checkpoint for a table
    /// </summary>
    /// <param name="tableName">Table name</param>
    /// <param name="strategy">Delta strategy</param>
    /// <returns>Latest checkpoint, or null if none exists</returns>
    Task<DeltaCheckpoint?> GetLatestCheckpointAsync(string tableName, DeltaStrategy strategy);
    
    /// <summary>
    /// Lists all checkpoints for a table
    /// </summary>
    /// <param name="tableName">Table name</param>
    /// <returns>List of checkpoints ordered by creation time</returns>
    Task<IReadOnlyList<DeltaCheckpoint>> GetCheckpointsAsync(string tableName);
    
    /// <summary>
    /// Deletes old checkpoints based on retention policy
    /// </summary>
    /// <param name="retentionDays">Number of days to retain</param>
    /// <returns>Number of checkpoints deleted</returns>
    Task<int> CleanupOldCheckpointsAsync(int retentionDays);
}

/// <summary>
/// Service for watermark-based delta exports
/// </summary>
public interface IWatermarkDeltaService
{
    /// <summary>
    /// Discovers suitable watermark columns for a table
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="tableName">Table to analyze</param>
    /// <returns>Recommended watermark columns</returns>
    IReadOnlyList<string> DiscoverWatermarkColumns(SqliteConnection connection, string tableName);
    
    /// <summary>
    /// Executes a watermark-based delta export
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="tableName">Table to export</param>
    /// <param name="config">Delta export configuration</param>
    /// <param name="checkpoint">Previous checkpoint (null for initial export)</param>
    /// <returns>Results of the delta export</returns>
    Task<DeltaExportResult> ExecuteDeltaExportAsync(
        SqliteConnection connection, 
        string tableName, 
        DeltaExportConfig config, 
        DeltaCheckpoint? checkpoint = null);
    
    /// <summary>
    /// Validates that the specified watermark columns are suitable
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="tableName">Table name</param>
    /// <param name="watermarkColumns">Columns to validate</param>
    /// <returns>Validation result with any issues found</returns>
    ValidationResult ValidateWatermarkColumns(
        SqliteConnection connection, 
        string tableName, 
        IReadOnlyList<string> watermarkColumns);
}

/// <summary>
/// Service for change log-based delta exports using triggers
/// </summary>
public interface IChangeLogDeltaService
{
    /// <summary>
    /// Installs change tracking triggers for a table
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="tableName">Table to track</param>
    /// <param name="config">Change log configuration</param>
    /// <returns>True if triggers were installed successfully</returns>
    Task<bool> InstallChangeTrackingAsync(
        SqliteConnection connection, 
        string tableName, 
        ChangeLogConfig config);
    
    /// <summary>
    /// Removes change tracking triggers for a table
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="tableName">Table to stop tracking</param>
    /// <param name="config">Change log configuration</param>
    /// <returns>True if triggers were removed successfully</returns>
    Task<bool> RemoveChangeTrackingAsync(
        SqliteConnection connection, 
        string tableName, 
        ChangeLogConfig config);
    
    /// <summary>
    /// Executes a change log-based delta export
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="tableName">Table to export</param>
    /// <param name="config">Delta export configuration</param>
    /// <param name="checkpoint">Previous checkpoint (null for initial export)</param>
    /// <returns>Results of the delta export</returns>
    Task<DeltaExportResult> ExecuteDeltaExportAsync(
        SqliteConnection connection, 
        string tableName, 
        DeltaExportConfig config, 
        DeltaCheckpoint? checkpoint = null);
    
    /// <summary>
    /// Cleans up old change log entries
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="config">Change log configuration</param>
    /// <param name="retentionDays">Days to retain</param>
    /// <returns>Number of entries cleaned up</returns>
    Task<int> CleanupChangeLogAsync(
        SqliteConnection connection, 
        ChangeLogConfig config, 
        int retentionDays);
    
    /// <summary>
    /// Checks if change tracking is installed for a table
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="tableName">Table to check</param>
    /// <param name="config">Change log configuration</param>
    /// <returns>True if change tracking is installed</returns>
    bool IsChangeTrackingInstalled(
        SqliteConnection connection, 
        string tableName, 
        ChangeLogConfig config);
}

/// <summary>
/// Main service for delta exports - coordinates different strategies
/// </summary>
public interface IDeltaExportService
{
    /// <summary>
    /// Executes a delta export using the specified strategy
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="tableName">Table to export</param>
    /// <param name="config">Delta export configuration</param>
    /// <returns>Results of the delta export including new checkpoint</returns>
    Task<DeltaExportResult> ExecuteDeltaExportAsync(
        SqliteConnection connection, 
        string tableName, 
        DeltaExportConfig config);
    
    /// <summary>
    /// Recommends a delta strategy for a table based on its structure
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="tableName">Table to analyze</param>
    /// <returns>Recommended delta strategy and configuration</returns>
    Task<(DeltaStrategy strategy, DeltaExportConfig config)> RecommendDeltaStrategyAsync(
        SqliteConnection connection, 
        string tableName);
    
    /// <summary>
    /// Lists all tables that have delta export checkpoints
    /// </summary>
    /// <returns>List of table names with active delta exports</returns>
    Task<IReadOnlyList<string>> GetTrackedTablesAsync();
    
    /// <summary>
    /// Resets delta tracking for a table (removes all checkpoints)
    /// </summary>
    /// <param name="tableName">Table to reset</param>
    /// <returns>True if reset was successful</returns>
    Task<bool> ResetDeltaTrackingAsync(string tableName);
}

/// <summary>
/// Validation result for delta export operations
/// </summary>
public sealed record ValidationResult
{
    /// <summary>
    /// Whether the validation passed
    /// </summary>
    public bool IsValid { get; init; }
    
    /// <summary>
    /// List of validation errors
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// List of validation warnings
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Additional metadata about the validation
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
    
    /// <summary>
    /// Creates a successful validation result
    /// </summary>
    public static ValidationResult Success() => new() { IsValid = true };
    
    /// <summary>
    /// Creates a failed validation result with errors
    /// </summary>
    public static ValidationResult Failure(params string[] errors) => new() 
    { 
        IsValid = false, 
        Errors = errors 
    };
    
    /// <summary>
    /// Creates a validation result with warnings but no errors
    /// </summary>
    public static ValidationResult WithWarnings(params string[] warnings) => new() 
    { 
        IsValid = true, 
        Warnings = warnings 
    };
}

/// <summary>
/// Query executor specifically for delta export operations
/// Extends the base query executor with delta-specific functionality
/// </summary>
public interface IDeltaQueryExecutor
{
    /// <summary>
    /// Executes a delta query and returns results with row count tracking
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="query">Parameterized SQL query</param>
    /// <param name="maxRows">Maximum rows to return</param>
    /// <returns>Query results with metadata</returns>
    Task<(IEnumerable<Dictionary<string, object?>> rows, long totalCount, bool hasMore)> ExecuteDeltaQueryAsync(
        SqliteConnection connection, 
        ParameterizedSql query, 
        int? maxRows = null);
    
    /// <summary>
    /// Gets the current maximum value(s) for watermark columns
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="tableName">Table name</param>
    /// <param name="watermarkColumns">Watermark columns</param>
    /// <returns>Maximum values for each watermark column</returns>
    Task<Dictionary<string, object?>> GetCurrentWatermarkValuesAsync(
        SqliteConnection connection, 
        string tableName, 
        IReadOnlyList<string> watermarkColumns);
}