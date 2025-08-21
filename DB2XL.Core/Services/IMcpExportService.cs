using DB2XL.Core.Models;

namespace DB2XL.Core.Services;

/// <summary>
/// MCP (Model Context Protocol) service interface for AI-friendly database exports.
/// Provides structured access to export, preview, and delta operations.
/// </summary>
public interface IMcpExportService
{
    /// <summary>
    /// Preview database structure and data for AI analysis.
    /// </summary>
    /// <param name="request">Preview request parameters</param>
    /// <returns>Structured database preview for AI consumption</returns>
    Task<McpPreviewResult> PreviewDatabaseAsync(McpPreviewRequest request);
    
    /// <summary>
    /// Export database to various formats with AI-optimized output.
    /// </summary>
    /// <param name="request">Export request parameters</param>
    /// <returns>Export result with AI-readable metadata</returns>
    Task<McpExportResult> ExportDatabaseAsync(McpExportRequest request);
    
    /// <summary>
    /// Perform delta export with checkpoint management.
    /// </summary>
    /// <param name="request">Delta export request parameters</param>
    /// <returns>Delta export result with incremental data</returns>
    Task<McpDeltaResult> ExportDeltaAsync(McpDeltaRequest request);
    
    /// <summary>
    /// Get database schema information in AI-friendly format.
    /// </summary>
    /// <param name="request">Schema request parameters</param>
    /// <returns>Structured schema information</returns>
    Task<McpSchemaResult> GetSchemaAsync(McpSchemaRequest request);
    
    /// <summary>
    /// Execute SQL query with safety constraints for AI interaction.
    /// </summary>
    /// <param name="request">Query request parameters</param>
    /// <returns>Query results with metadata</returns>
    Task<McpQueryResult> ExecuteQueryAsync(McpQueryRequest request);
}

/// <summary>
/// Request for database preview operations.
/// </summary>
public sealed record McpPreviewRequest
{
    /// <summary>Path to SQLite database file.</summary>
    public required string DatabasePath { get; init; }
    
    /// <summary>Tables to include (null = all tables).</summary>
    public IReadOnlyList<string>? IncludeTables { get; init; }
    
    /// <summary>Maximum rows to preview per table.</summary>
    public int MaxPreviewRows { get; init; } = 5;
    
    /// <summary>Include database statistics.</summary>
    public bool IncludeStatistics { get; init; } = true;
    
    /// <summary>Include sample data for AI analysis.</summary>
    public bool IncludeSampleData { get; init; } = true;
    
    /// <summary>Include foreign key relationships.</summary>
    public bool IncludeRelationships { get; init; } = true;
}

/// <summary>
/// Result of database preview operation.
/// </summary>
public sealed record McpPreviewResult
{
    /// <summary>Whether the preview was successful.</summary>
    public bool IsSuccess { get; init; }
    
    /// <summary>Database information summary.</summary>
    public DatabaseSummary Summary { get; init; } = new();
    
    /// <summary>Table previews with schema and sample data.</summary>
    public IReadOnlyList<TablePreview> Tables { get; init; } = Array.Empty<TablePreview>();
    
    /// <summary>Database relationships for AI understanding.</summary>
    public IReadOnlyList<RelationshipInfo> Relationships { get; init; } = Array.Empty<RelationshipInfo>();
    
    /// <summary>Any errors encountered.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    
    /// <summary>Processing duration.</summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Database summary information for AI context.
/// </summary>
public sealed record DatabaseSummary
{
    /// <summary>Database file path.</summary>
    public string FilePath { get; init; } = string.Empty;
    
    /// <summary>Database file size in bytes.</summary>
    public long FileSizeBytes { get; init; }
    
    /// <summary>Number of tables.</summary>
    public int TableCount { get; init; }
    
    /// <summary>Number of views.</summary>
    public int ViewCount { get; init; }
    
    /// <summary>Number of indexes.</summary>
    public int IndexCount { get; init; }
    
    /// <summary>Total estimated rows across all tables.</summary>
    public long TotalEstimatedRows { get; init; }
    
    /// <summary>SQLite version information.</summary>
    public string SqliteVersion { get; init; } = string.Empty;
    
    /// <summary>Database schema version.</summary>
    public long SchemaVersion { get; init; }
    
    /// <summary>Database creation time (if available).</summary>
    public DateTime? CreatedAt { get; init; }
    
    /// <summary>Last modification time.</summary>
    public DateTime LastModified { get; init; }
}

/// <summary>
/// Table preview with schema and sample data for AI analysis.
/// </summary>
public sealed record TablePreview
{
    /// <summary>Table name.</summary>
    public required string Name { get; init; }
    
    /// <summary>Table type (table, view).</summary>
    public string Type { get; init; } = "table";
    
    /// <summary>Column definitions.</summary>
    public IReadOnlyList<McpColumnPreview> Columns { get; init; } = Array.Empty<McpColumnPreview>();
    
    /// <summary>Primary key columns.</summary>
    public IReadOnlyList<string> PrimaryKeys { get; init; } = Array.Empty<string>();
    
    /// <summary>Estimated row count.</summary>
    public long EstimatedRows { get; init; }
    
    /// <summary>Sample data rows for AI pattern recognition.</summary>
    public IReadOnlyList<Dictionary<string, object?>> SampleData { get; init; } = Array.Empty<Dictionary<string, object?>>();
    
    /// <summary>Table creation SQL (if available).</summary>
    public string? CreateSql { get; init; }
    
    /// <summary>Data patterns detected for AI insights.</summary>
    public TableDataPatterns DataPatterns { get; init; } = new();
}

/// <summary>
/// Column information for AI understanding with enhanced metadata.
/// </summary>
public sealed record McpColumnPreview
{
    /// <summary>Base column information.</summary>
    public required ColumnInfo Column { get; init; }
    
    /// <summary>Data patterns detected in this column for AI insights.</summary>
    public ColumnDataPatterns DataPatterns { get; init; } = new();
}

/// <summary>
/// Data patterns detected in table for AI insights.
/// </summary>
public sealed record TableDataPatterns
{
    /// <summary>Detected timestamp columns for delta tracking.</summary>
    public IReadOnlyList<string> TimestampColumns { get; init; } = Array.Empty<string>();
    
    /// <summary>Columns that might contain PII.</summary>
    public IReadOnlyList<string> PotentialPiiColumns { get; init; } = Array.Empty<string>();
    
    /// <summary>Columns with JSON data.</summary>
    public IReadOnlyList<string> JsonColumns { get; init; } = Array.Empty<string>();
    
    /// <summary>Columns suitable for partitioning.</summary>
    public IReadOnlyList<string> PartitionableColumns { get; init; } = Array.Empty<string>();
    
    /// <summary>Detected ID/foreign key columns.</summary>
    public IReadOnlyList<string> IdColumns { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Data patterns detected in individual column.
/// </summary>
public sealed record ColumnDataPatterns
{
    /// <summary>Sample unique values (for enum-like columns).</summary>
    public IReadOnlyList<string> SampleValues { get; init; } = Array.Empty<string>();
    
    /// <summary>Estimated cardinality.</summary>
    public long EstimatedCardinality { get; init; }
    
    /// <summary>Percentage of NULL values.</summary>
    public double NullPercentage { get; init; }
    
    /// <summary>Data format pattern (if detected).</summary>
    public string? FormatPattern { get; init; }
    
    /// <summary>Whether column appears to be an identifier.</summary>
    public bool IsIdentifier { get; init; }
    
    /// <summary>Whether column might contain PII.</summary>
    public bool MightBePii { get; init; }
}

/// <summary>
/// Relationship information between tables.
/// </summary>
public sealed record RelationshipInfo
{
    /// <summary>Parent table name.</summary>
    public required string ParentTable { get; init; }
    
    /// <summary>Child table name.</summary>
    public required string ChildTable { get; init; }
    
    /// <summary>Foreign key column(s) in child table.</summary>
    public IReadOnlyList<string> ForeignKeyColumns { get; init; } = Array.Empty<string>();
    
    /// <summary>Referenced column(s) in parent table.</summary>
    public IReadOnlyList<string> ReferencedColumns { get; init; } = Array.Empty<string>();
    
    /// <summary>Relationship strength (based on data analysis).</summary>
    public double Strength { get; init; }
    
    /// <summary>Relationship type description.</summary>
    public string RelationshipType { get; init; } = "foreign_key";
}

/// <summary>
/// Request for database export operations.
/// </summary>
public sealed record McpExportRequest
{
    /// <summary>Path to SQLite database file.</summary>
    public required string DatabasePath { get; init; }
    
    /// <summary>Output directory for exported files.</summary>
    public required string OutputDirectory { get; init; }
    
    /// <summary>Export format (jsonl, parquet, excel, bundle).</summary>
    public string Format { get; init; } = "bundle";
    
    /// <summary>Tables to include (null = all tables).</summary>
    public IReadOnlyList<string>? IncludeTables { get; init; }
    
    /// <summary>Whether to include sample files.</summary>
    public bool IncludeSamples { get; init; } = true;
    
    /// <summary>Maximum rows for sample files.</summary>
    public int SampleRowLimit { get; init; } = 1000;
    
    /// <summary>Whether to generate manifest files for AI consumption.</summary>
    public bool GenerateManifest { get; init; } = true;
    
    /// <summary>PII redaction configuration.</summary>
    public PiiRedactionConfig? PiiConfig { get; init; }
}

/// <summary>
/// Result of database export operation.
/// </summary>
public sealed record McpExportResult
{
    /// <summary>Whether the export was successful.</summary>
    public bool IsSuccess { get; init; }
    
    /// <summary>Exported files with metadata.</summary>
    public IReadOnlyList<ExportedFileInfo> ExportedFiles { get; init; } = Array.Empty<ExportedFileInfo>();
    
    /// <summary>Export statistics for AI insight.</summary>
    public ExportStatistics Statistics { get; init; } = new();
    
    /// <summary>Manifest file path (if generated).</summary>
    public string? ManifestPath { get; init; }
    
    /// <summary>Any errors encountered.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    
    /// <summary>Processing duration.</summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Information about exported file for AI understanding.
/// </summary>
public sealed record ExportedFileInfo
{
    /// <summary>File path relative to output directory.</summary>
    public required string RelativePath { get; init; }
    
    /// <summary>Absolute file path.</summary>
    public required string FullPath { get; init; }
    
    /// <summary>Table name this file represents.</summary>
    public string TableName { get; init; } = string.Empty;
    
    /// <summary>File format.</summary>
    public string Format { get; init; } = string.Empty;
    
    /// <summary>Number of rows in file.</summary>
    public long RowCount { get; init; }
    
    /// <summary>File size in bytes.</summary>
    public long FileSizeBytes { get; init; }
    
    /// <summary>SHA256 hash for verification.</summary>
    public string Sha256Hash { get; init; } = string.Empty;
    
    /// <summary>Whether this is a sample file.</summary>
    public bool IsSample { get; init; }
    
    /// <summary>File creation timestamp.</summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Export statistics for AI analysis.
/// </summary>
public sealed record ExportStatistics
{
    /// <summary>Number of tables exported.</summary>
    public int TablesExported { get; init; }
    
    /// <summary>Total rows exported.</summary>
    public long TotalRowsExported { get; init; }
    
    /// <summary>Total files created.</summary>
    public int FilesCreated { get; init; }
    
    /// <summary>Total export size in bytes.</summary>
    public long TotalSizeBytes { get; init; }
    
    /// <summary>Number of PII columns redacted.</summary>
    public int PiiColumnsRedacted { get; init; }
    
    /// <summary>Average rows per table.</summary>
    public double AverageRowsPerTable { get; init; }
    
    /// <summary>Largest table by row count.</summary>
    public string? LargestTableName { get; init; }
    
    /// <summary>Row count of largest table.</summary>
    public long LargestTableRows { get; init; }
}

/// <summary>
/// Request for delta export operations.
/// </summary>
public sealed record McpDeltaRequest
{
    /// <summary>Path to SQLite database file.</summary>
    public required string DatabasePath { get; init; }
    
    /// <summary>Output directory for delta files.</summary>
    public required string OutputDirectory { get; init; }
    
    /// <summary>Delta strategy (watermark, changelog).</summary>
    public string Strategy { get; init; } = "watermark";
    
    /// <summary>Watermark column for tracking changes.</summary>
    public string? WatermarkColumn { get; init; }
    
    /// <summary>Tables to include (null = all tables).</summary>
    public IReadOnlyList<string>? IncludeTables { get; init; }
    
    /// <summary>Previous checkpoint file path.</summary>
    public string? CheckpointFile { get; init; }
    
    /// <summary>Whether to install change tracking infrastructure.</summary>
    public bool InstallChangeTracking { get; init; }
}

/// <summary>
/// Result of delta export operation.
/// </summary>
public sealed record McpDeltaResult
{
    /// <summary>Whether the delta export was successful.</summary>
    public bool IsSuccess { get; init; }
    
    /// <summary>Delta files created.</summary>
    public IReadOnlyList<DeltaFileInfo> DeltaFiles { get; init; } = Array.Empty<DeltaFileInfo>();
    
    /// <summary>Updated checkpoint information.</summary>
    public DeltaCheckpointInfo? CheckpointInfo { get; init; }
    
    /// <summary>Tables with changes detected.</summary>
    public IReadOnlyList<string> TablesWithChanges { get; init; } = Array.Empty<string>();
    
    /// <summary>Total rows exported in delta.</summary>
    public long TotalDeltaRows { get; init; }
    
    /// <summary>Any errors encountered.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    
    /// <summary>Processing duration.</summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Information about delta file for AI understanding.
/// </summary>
public sealed record DeltaFileInfo
{
    /// <summary>File path relative to output directory.</summary>
    public required string RelativePath { get; init; }
    
    /// <summary>Table name this delta represents.</summary>
    public required string TableName { get; init; }
    
    /// <summary>Number of changed rows.</summary>
    public long ChangedRows { get; init; }
    
    /// <summary>Types of changes (INSERT, UPDATE, DELETE).</summary>
    public IReadOnlyList<string> ChangeTypes { get; init; } = Array.Empty<string>();
    
    /// <summary>Time range of changes.</summary>
    public DateTimeRange? TimeRange { get; init; }
    
    /// <summary>File size in bytes.</summary>
    public long FileSizeBytes { get; init; }
}

/// <summary>
/// Checkpoint information for delta tracking.
/// </summary>
public sealed record DeltaCheckpointInfo
{
    /// <summary>Checkpoint file path.</summary>
    public required string CheckpointPath { get; init; }
    
    /// <summary>Last processed watermark values by table.</summary>
    public IReadOnlyDictionary<string, object> LastWatermarks { get; init; } = 
        new Dictionary<string, object>();
    
    /// <summary>Total rows processed across all tables.</summary>
    public long TotalRowsProcessed { get; init; }
    
    /// <summary>Checkpoint creation time.</summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Request for schema information.
/// </summary>
public sealed record McpSchemaRequest
{
    /// <summary>Path to SQLite database file.</summary>
    public required string DatabasePath { get; init; }
    
    /// <summary>Whether to include detailed column information.</summary>
    public bool IncludeColumnDetails { get; init; } = true;
    
    /// <summary>Whether to include index information.</summary>
    public bool IncludeIndexes { get; init; } = true;
    
    /// <summary>Whether to include foreign key relationships.</summary>
    public bool IncludeForeignKeys { get; init; } = true;
    
    /// <summary>Whether to include CREATE SQL statements.</summary>
    public bool IncludeCreateSql { get; init; } = false;
}

/// <summary>
/// Result of schema query operation.
/// </summary>
public sealed record McpSchemaResult
{
    /// <summary>Whether the schema query was successful.</summary>
    public bool IsSuccess { get; init; }
    
    /// <summary>Database schema information.</summary>
    public DatabaseSchema Schema { get; init; } = new();
    
    /// <summary>Any errors encountered.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Complete database schema information.
/// </summary>
public sealed record DatabaseSchema
{
    /// <summary>Database file path.</summary>
    public string DatabasePath { get; init; } = string.Empty;
    
    /// <summary>Tables in the database.</summary>
    public IReadOnlyList<TableSchema> Tables { get; init; } = Array.Empty<TableSchema>();
    
    /// <summary>Views in the database.</summary>
    public IReadOnlyList<ViewSchema> Views { get; init; } = Array.Empty<ViewSchema>();
    
    /// <summary>Indexes in the database.</summary>
    public IReadOnlyList<IndexSchema> Indexes { get; init; } = Array.Empty<IndexSchema>();
    
    /// <summary>Foreign key relationships.</summary>
    public IReadOnlyList<ForeignKeySchema> ForeignKeys { get; init; } = Array.Empty<ForeignKeySchema>();
}

/// <summary>
/// Table schema information.
/// </summary>
public sealed record TableSchema
{
    /// <summary>Table name.</summary>
    public required string Name { get; init; }
    
    /// <summary>Columns in the table.</summary>
    public IReadOnlyList<ColumnSchema> Columns { get; init; } = Array.Empty<ColumnSchema>();
    
    /// <summary>Table creation SQL.</summary>
    public string? CreateSql { get; init; }
    
    /// <summary>Whether table has WITHOUT ROWID.</summary>
    public bool WithoutRowId { get; init; }
}

/// <summary>
/// Column schema information.
/// </summary>
public sealed record ColumnSchema
{
    /// <summary>Column name.</summary>
    public required string Name { get; init; }
    
    /// <summary>Column data type.</summary>
    public string Type { get; init; } = string.Empty;
    
    /// <summary>Whether column allows NULL.</summary>
    public bool IsNullable { get; init; } = true;
    
    /// <summary>Whether column is primary key.</summary>
    public bool IsPrimaryKey { get; init; }
    
    /// <summary>Default value expression.</summary>
    public string? DefaultValue { get; init; }
    
    /// <summary>Column position in table.</summary>
    public int Position { get; init; }
}

/// <summary>
/// View schema information.
/// </summary>
public sealed record ViewSchema
{
    /// <summary>View name.</summary>
    public required string Name { get; init; }
    
    /// <summary>View creation SQL.</summary>
    public string CreateSql { get; init; } = string.Empty;
}

/// <summary>
/// Index schema information.
/// </summary>
public sealed record IndexSchema
{
    /// <summary>Index name.</summary>
    public required string Name { get; init; }
    
    /// <summary>Table this index belongs to.</summary>
    public required string TableName { get; init; }
    
    /// <summary>Columns included in index.</summary>
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
    
    /// <summary>Whether index is unique.</summary>
    public bool IsUnique { get; init; }
    
    /// <summary>Index creation SQL.</summary>
    public string? CreateSql { get; init; }
}

/// <summary>
/// Foreign key schema information.
/// </summary>
public sealed record ForeignKeySchema
{
    /// <summary>Foreign key constraint name.</summary>
    public string? Name { get; init; }
    
    /// <summary>Child table name.</summary>
    public required string ChildTable { get; init; }
    
    /// <summary>Parent table name.</summary>
    public required string ParentTable { get; init; }
    
    /// <summary>Child table columns.</summary>
    public IReadOnlyList<string> ChildColumns { get; init; } = Array.Empty<string>();
    
    /// <summary>Parent table columns.</summary>
    public IReadOnlyList<string> ParentColumns { get; init; } = Array.Empty<string>();
    
    /// <summary>ON UPDATE action.</summary>
    public string OnUpdate { get; init; } = "NO ACTION";
    
    /// <summary>ON DELETE action.</summary>
    public string OnDelete { get; init; } = "NO ACTION";
}

/// <summary>
/// Request for SQL query execution.
/// </summary>
public sealed record McpQueryRequest
{
    /// <summary>Path to SQLite database file.</summary>
    public required string DatabasePath { get; init; }
    
    /// <summary>SQL query to execute.</summary>
    public required string SqlQuery { get; init; }
    
    /// <summary>Maximum rows to return.</summary>
    public int MaxRows { get; init; } = 1000;
    
    /// <summary>Query timeout in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 30;
    
    /// <summary>Whether to allow write operations (INSERT, UPDATE, DELETE).</summary>
    public bool AllowWrites { get; init; } = false;
}

/// <summary>
/// Result of SQL query execution.
/// </summary>
public sealed record McpQueryResult
{
    /// <summary>Whether the query executed successfully.</summary>
    public bool IsSuccess { get; init; }
    
    /// <summary>Query result rows.</summary>
    public IReadOnlyList<Dictionary<string, object?>> Rows { get; init; } = Array.Empty<Dictionary<string, object?>>();
    
    /// <summary>Column information for result set.</summary>
    public IReadOnlyList<QueryColumnInfo> Columns { get; init; } = Array.Empty<QueryColumnInfo>();
    
    /// <summary>Number of rows affected (for write operations).</summary>
    public int RowsAffected { get; init; }
    
    /// <summary>Whether result set was truncated due to MaxRows limit.</summary>
    public bool IsTruncated { get; init; }
    
    /// <summary>Query execution duration.</summary>
    public TimeSpan Duration { get; init; }
    
    /// <summary>Any errors encountered.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Column information for query results.
/// </summary>
public sealed record QueryColumnInfo
{
    /// <summary>Column name.</summary>
    public required string Name { get; init; }
    
    /// <summary>Column data type.</summary>
    public string Type { get; init; } = string.Empty;
    
    /// <summary>Column position in result set.</summary>
    public int Position { get; init; }
}

// PII redaction configuration is defined in PiiModels.cs

