using Microsoft.Data.Sqlite;

namespace DB2XL.Query;

/// <summary>
/// Represents a complete database selection query with filtering, ordering, and projection
/// </summary>
public interface ISelectionGrammar
{
    /// <summary>
    /// Target table name
    /// </summary>
    string Table { get; }
    
    /// <summary>
    /// Column projections (can include expressions like "json_extract(payload, '$.id') as user_id")
    /// </summary>
    IReadOnlyList<string> Select { get; }
    
    /// <summary>
    /// WHERE clause expression tree
    /// </summary>
    IWhereExpression? Where { get; }
    
    /// <summary>
    /// ORDER BY clauses for deterministic sorting
    /// </summary>
    IReadOnlyList<IOrderByClause> OrderBy { get; }
    
    /// <summary>
    /// Maximum number of rows to return
    /// </summary>
    int? Limit { get; }
    
    /// <summary>
    /// Number of rows to skip
    /// </summary>
    int? Offset { get; }
}

/// <summary>
/// Base interface for WHERE clause expressions
/// </summary>
public interface IWhereExpression
{
    /// <summary>
    /// Converts this expression to SQL with parameter placeholders
    /// </summary>
    /// <param name="parameters">Dictionary to populate with parameter values</param>
    /// <returns>SQL string with parameter placeholders</returns>
    string ToSql(Dictionary<string, object?> parameters);
}

/// <summary>
/// Represents an ORDER BY clause
/// </summary>
public interface IOrderByClause
{
    /// <summary>
    /// Column name to order by
    /// </summary>
    string Column { get; }
    
    /// <summary>
    /// Sort direction
    /// </summary>
    SortDirection Direction { get; }
}

/// <summary>
/// Sort direction enumeration
/// </summary>
public enum SortDirection
{
    Ascending,
    Descending
}

/// <summary>
/// Comparison operators for WHERE clauses
/// </summary>
public enum ComparisonOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    In,
    NotIn,
    Like,
    Glob,
    Between,
    IsNull,
    IsNotNull
}

/// <summary>
/// Converts selection grammar to parameterized SQL
/// </summary>
public interface ISqlBuilder
{
    /// <summary>
    /// Builds a parameterized SQL query from selection grammar
    /// </summary>
    /// <param name="selection">Selection grammar specification</param>
    /// <returns>Parameterized SQL query</returns>
    ParameterizedSql BuildQuery(ISelectionGrammar selection);
    
    /// <summary>
    /// Builds a parameterized COUNT query from selection grammar
    /// </summary>
    /// <param name="selection">Selection grammar specification</param>
    /// <returns>Parameterized COUNT SQL query</returns>
    ParameterizedSql BuildCountQuery(ISelectionGrammar selection);
}

/// <summary>
/// Represents a SQL query with its parameters
/// </summary>
public sealed record ParameterizedSql(string Sql, Dictionary<string, object?> Parameters);

/// <summary>
/// Executes parameterized SQL queries safely
/// </summary>
public interface IQueryExecutor
{
    /// <summary>
    /// Executes a selection query and returns results
    /// </summary>
    /// <param name="connection">SQLite connection</param>
    /// <param name="query">Parameterized SQL query</param>
    /// <returns>Query results as enumerable of string dictionaries</returns>
    IEnumerable<Dictionary<string, object?>> ExecuteQuery(SqliteConnection connection, ParameterizedSql query);
    
    /// <summary>
    /// Executes a count query and returns the row count
    /// </summary>
    /// <param name="connection">SQLite connection</param>
    /// <param name="query">Parameterized COUNT SQL query</param>
    /// <returns>Number of matching rows</returns>
    long ExecuteCount(SqliteConnection connection, ParameterizedSql query);
}

/// <summary>
/// Factory for creating selection grammar instances from JSON
/// </summary>
public interface ISelectionGrammarFactory
{
    /// <summary>
    /// Parses JSON selection grammar into typed objects
    /// </summary>
    /// <param name="json">JSON selection grammar string</param>
    /// <returns>Parsed selection grammar</returns>
    ISelectionGrammar ParseJson(string json);
    
    /// <summary>
    /// Creates a simple selection for basic table export
    /// </summary>
    /// <param name="tableName">Table to select from</param>
    /// <param name="columns">Columns to select (null for all)</param>
    /// <returns>Basic selection grammar</returns>
    ISelectionGrammar CreateSimple(string tableName, string[]? columns = null);
}