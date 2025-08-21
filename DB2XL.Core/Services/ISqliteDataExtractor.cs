using DB2XL.Core.Models;

namespace DB2XL.Core.Services;

/// <summary>
/// Service interface for extracting data from SQLite databases with streaming capabilities.
/// Provides schema analysis, deterministic ordering, and memory-efficient data access.
/// </summary>
public interface ISqliteDataExtractor
{
    /// <summary>
    /// Extracts all rows from a table as an async enumerable stream.
    /// Uses deterministic ordering based on primary keys or rowid for reproducible exports.
    /// </summary>
    /// <param name="connectionString">SQLite connection string</param>
    /// <param name="tableName">Name of the table to extract</param>
    /// <param name="options">Extraction configuration options</param>
    /// <param name="cancellationToken">Cancellation token for long-running operations</param>
    /// <returns>Async enumerable of table rows as key-value dictionaries</returns>
    IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ExtractTableDataAsync(
        string connectionString,
        string tableName,
        ExtractionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts table data in batches for memory-efficient processing.
    /// Each batch contains a configurable number of rows.
    /// </summary>
    /// <param name="connectionString">SQLite connection string</param>
    /// <param name="tableName">Name of the table to extract</param>
    /// <param name="options">Extraction configuration options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of row batches</returns>
    IAsyncEnumerable<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExtractTableBatchesAsync(
        string connectionString,
        string tableName,
        ExtractionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes table structure, constraints, and statistics.
    /// Provides metadata needed for partitioning and export decisions.
    /// </summary>
    /// <param name="connectionString">SQLite connection string</param>
    /// <param name="tableName">Name of the table to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Complete table metadata including schema and statistics</returns>
    Task<TableMetadata> AnalyzeTableAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets list of available tables and views in the database.
    /// Applies filtering based on options and inclusion criteria.
    /// </summary>
    /// <param name="connectionString">SQLite connection string</param>
    /// <param name="includeViews">Whether to include database views</param>
    /// <param name="tableFilter">Optional table name filter (LIKE pattern)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of table/view names sorted alphabetically</returns>
    Task<IReadOnlyList<string>> GetTablesAsync(
        string connectionString,
        bool includeViews = false,
        string? tableFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimates the number of rows in a table efficiently.
    /// Uses PRAGMA table_info and ANALYZE statistics when available.
    /// </summary>
    /// <param name="connectionString">SQLite connection string</param>
    /// <param name="tableName">Name of the table</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Estimated row count (may be approximate for large tables)</returns>
    Task<long> EstimateRowCountAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests database connectivity and validates access permissions.
    /// </summary>
    /// <param name="connectionString">SQLite connection string</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if connection is successful and database is readable</returns>
    Task<bool> ValidateConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration options for data extraction operations.
/// Controls query generation, ordering, filtering, and performance settings.
/// </summary>
public sealed record ExtractionOptions
{
    /// <summary>Maximum number of rows per batch for batch extraction.</summary>
    public int BatchSize { get; init; } = 25_000;

    /// <summary>Custom WHERE clause for row filtering (optional).</summary>
    public string? WhereClause { get; init; }

    /// <summary>Specific columns to include (null = all columns).</summary>
    public IReadOnlyList<string>? IncludeColumns { get; init; }

    /// <summary>Columns to exclude from extraction.</summary>
    public IReadOnlyList<string>? ExcludeColumns { get; init; } = Array.Empty<string>();

    /// <summary>Whether to use deterministic ordering (recommended for reproducible exports).</summary>
    public bool DeterministicOrdering { get; init; } = true;

    /// <summary>Custom ORDER BY clause (overrides deterministic ordering).</summary>
    public string? CustomOrderBy { get; init; }

    /// <summary>Maximum number of rows to extract (0 = no limit).</summary>
    public long MaxRows { get; init; } = 0;

    /// <summary>Command timeout in seconds for long-running queries.</summary>
    public int CommandTimeoutSeconds { get; init; } = 300;

    /// <summary>Whether to include computed columns and expressions.</summary>
    public bool IncludeComputedColumns { get; init; } = false;

    /// <summary>How to handle BLOB columns during extraction.</summary>
    public BlobHandlingMode BlobMode { get; init; } = BlobHandlingMode.Include;

    /// <summary>Culture info for data formatting (default: InvariantCulture).</summary>
    public string CultureName { get; init; } = "InvariantCulture";
}

/// <summary>
/// Comprehensive metadata about a database table.
/// Includes schema information, statistics, and extraction guidance.
/// </summary>
public sealed record TableMetadata
{
    /// <summary>Table name.</summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>Table type (table, view, etc.).</summary>
    public string TableType { get; init; } = string.Empty;

    /// <summary>Column definitions with types and constraints.</summary>
    public IReadOnlyList<ColumnMetadata> Columns { get; init; } = Array.Empty<ColumnMetadata>();

    /// <summary>Primary key column names in order.</summary>
    public IReadOnlyList<string> PrimaryKeyColumns { get; init; } = Array.Empty<string>();

    /// <summary>Estimated number of rows (may be approximate).</summary>
    public long EstimatedRowCount { get; init; }

    /// <summary>Whether table has rowid column available.</summary>
    public bool HasRowId { get; init; } = true;

    /// <summary>Whether table uses WITHOUT ROWID storage.</summary>
    public bool IsWithoutRowId { get; init; }

    /// <summary>Foreign key relationships.</summary>
    public IReadOnlyList<ForeignKeyInfo> ForeignKeys { get; init; } = Array.Empty<ForeignKeyInfo>();

    /// <summary>Table indexes for query optimization.</summary>
    public IReadOnlyList<IndexInfo> Indexes { get; init; } = Array.Empty<IndexInfo>();

    /// <summary>CREATE TABLE SQL statement.</summary>
    public string CreateSql { get; init; } = string.Empty;

    /// <summary>Recommended ORDER BY clause for deterministic extraction.</summary>
    public string RecommendedOrderBy { get; init; } = string.Empty;

    /// <summary>Data quality indicators and warnings.</summary>
    public IReadOnlyList<string> DataQualityWarnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Metadata for a single table column.
/// </summary>
public sealed record ColumnMetadata
{
    /// <summary>Column name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>SQLite declared type.</summary>
    public string DeclaredType { get; init; } = string.Empty;

    /// <summary>SQLite type affinity (INTEGER, REAL, TEXT, BLOB, NUMERIC).</summary>
    public string TypeAffinity { get; init; } = string.Empty;

    /// <summary>Whether column allows NULL values.</summary>
    public bool IsNullable { get; init; } = true;

    /// <summary>Whether column is part of primary key.</summary>
    public bool IsPrimaryKey { get; init; }

    /// <summary>Primary key position (0 if not part of PK).</summary>
    public int PrimaryKeyPosition { get; init; }

    /// <summary>Default value expression (if any).</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Whether column is auto-increment.</summary>
    public bool IsAutoIncrement { get; init; }

    /// <summary>Column ordinal position in table.</summary>
    public int OrdinalPosition { get; init; }

    /// <summary>Whether column contains BLOB data.</summary>
    public bool IsBlobColumn { get; init; }

    /// <summary>Estimated percentage of NULL values (0-100).</summary>
    public double EstimatedNullPercentage { get; init; }
}

/// <summary>
/// Foreign key relationship information.
/// </summary>
public sealed record ForeignKeyInfo
{
    /// <summary>Local column name.</summary>
    public string ColumnName { get; init; } = string.Empty;

    /// <summary>Referenced table name.</summary>
    public string ReferencedTable { get; init; } = string.Empty;

    /// <summary>Referenced column name.</summary>
    public string ReferencedColumn { get; init; } = string.Empty;

    /// <summary>Foreign key constraint name.</summary>
    public string ConstraintName { get; init; } = string.Empty;
}


/// <summary>
/// How to handle BLOB columns during data extraction.
/// </summary>
public enum BlobHandlingMode
{
    /// <summary>Include BLOB data in extraction (default).</summary>
    Include,

    /// <summary>Skip BLOB columns entirely.</summary>
    Skip,

    /// <summary>Include BLOB columns but extract as NULL.</summary>
    AsNull,

    /// <summary>Include BLOB size information instead of data.</summary>
    SizeOnly
}