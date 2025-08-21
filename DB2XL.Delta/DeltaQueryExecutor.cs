using Microsoft.Data.Sqlite;
using DB2XL.Query;

namespace DB2XL.DeltaExport;

/// <summary>
/// Query executor specialized for delta export operations
/// Provides additional functionality for tracking row counts and pagination
/// </summary>
public sealed class DeltaQueryExecutor : IDeltaQueryExecutor
{
    private readonly IQueryExecutor _baseExecutor;
    
    public DeltaQueryExecutor(IQueryExecutor? baseExecutor = null)
    {
        _baseExecutor = baseExecutor ?? new QueryExecutor();
    }
    
    public async Task<(IEnumerable<Dictionary<string, object?>> rows, long totalCount, bool hasMore)> ExecuteDeltaQueryAsync(
        SqliteConnection connection, 
        ParameterizedSql query, 
        int? maxRows = null)
    {
        var allRows = _baseExecutor.ExecuteQuery(connection, query).ToList();
        var totalCount = allRows.Count;
        
        if (maxRows.HasValue && totalCount > maxRows.Value)
        {
            var limitedRows = allRows.Take(maxRows.Value);
            return (limitedRows, totalCount, hasMore: true);
        }
        
        return (allRows, totalCount, hasMore: false);
    }
    
    public async Task<Dictionary<string, object?>> GetCurrentWatermarkValuesAsync(
        SqliteConnection connection, 
        string tableName, 
        IReadOnlyList<string> watermarkColumns)
    {
        var result = new Dictionary<string, object?>();
        
        foreach (var column in watermarkColumns)
        {
            var quotedTable = $"\"{tableName.Replace("\"", "\"\"")}\"";
            var quotedColumn = $"\"{column.Replace("\"", "\"\"")}\"";
            var sql = $"SELECT MAX({quotedColumn}) FROM {quotedTable}";
            
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            
            var value = await cmd.ExecuteScalarAsync();
            result[column] = value == DBNull.Value ? null : value;
        }
        
        return result;
    }
}

/// <summary>
/// Query builder specialized for delta export queries
/// Generates SQL queries with delta filtering conditions
/// </summary>
public static class DeltaQueryBuilder
{
    /// <summary>
    /// Builds a watermark-based delta query
    /// </summary>
    /// <param name="tableName">Table to query</param>
    /// <param name="watermarkColumns">Watermark columns in order</param>
    /// <param name="watermarkValues">Last processed watermark values</param>
    /// <param name="additionalFilter">Optional additional filtering</param>
    /// <param name="ordering">Custom ordering (if null, uses watermark columns)</param>
    /// <param name="maxRows">Maximum rows to return</param>
    /// <returns>Parameterized SQL for delta query</returns>
    public static ParameterizedSql BuildWatermarkQuery(
        string tableName,
        IReadOnlyList<string> watermarkColumns,
        Dictionary<string, object?> watermarkValues,
        ISelectionGrammar? additionalFilter = null,
        IReadOnlyList<IOrderByClause>? ordering = null,
        int? maxRows = null)
    {
        if (watermarkColumns.Count == 0)
        {
            throw new ArgumentException("At least one watermark column is required", nameof(watermarkColumns));
        }
        
        var parameters = new Dictionary<string, object?>();
        var whereClauses = new List<string>();
        
        // Build watermark WHERE clause
        if (watermarkValues.Count > 0)
        {
            var watermarkConditions = BuildWatermarkConditions(watermarkColumns, watermarkValues, parameters);
            whereClauses.Add(watermarkConditions);
        }
        
        // Add additional filter if provided
        if (additionalFilter?.Where != null)
        {
            var additionalSql = additionalFilter.Where.ToSql(parameters);
            whereClauses.Add($"({additionalSql})");
        }
        
        // Build query
        var quotedTable = $"\"{tableName.Replace("\"", "\"\"")}\"";
        var selectClause = additionalFilter?.Select.Count > 0 
            ? string.Join(", ", additionalFilter.Select.Select(QuoteIdentifier))
            : "*";
        
        var sql = $"SELECT {selectClause} FROM {quotedTable}";
        
        if (whereClauses.Count > 0)
        {
            sql += $" WHERE {string.Join(" AND ", whereClauses)}";
        }
        
        // Add ordering
        var orderBy = BuildOrderByClause(ordering, watermarkColumns);
        if (!string.IsNullOrEmpty(orderBy))
        {
            sql += $" ORDER BY {orderBy}";
        }
        
        // Add limit
        if (maxRows.HasValue)
        {
            sql += $" LIMIT {maxRows.Value}";
        }
        
        return new ParameterizedSql(sql, parameters);
    }
    
    /// <summary>
    /// Builds a change log-based delta query
    /// </summary>
    /// <param name="tableName">Table to query</param>
    /// <param name="changeLogTableName">Change log table name</param>
    /// <param name="lastChangeLogId">Last processed change log ID</param>
    /// <param name="includeDeletes">Whether to include deleted rows</param>
    /// <param name="additionalFilter">Optional additional filtering</param>
    /// <param name="maxRows">Maximum rows to return</param>
    /// <returns>Parameterized SQL for change log delta query</returns>
    public static ParameterizedSql BuildChangeLogQuery(
        string tableName,
        string changeLogTableName,
        long? lastChangeLogId,
        bool includeDeletes,
        ISelectionGrammar? additionalFilter = null,
        int? maxRows = null)
    {
        var parameters = new Dictionary<string, object?>();
        var whereClauses = new List<string>();
        
        var quotedTable = QuoteIdentifier(tableName);
        var quotedChangeLogTable = QuoteIdentifier(changeLogTableName);
        
        // Filter by change log ID
        if (lastChangeLogId.HasValue)
        {
            whereClauses.Add($"cl.change_id > @lastChangeLogId");
            parameters["lastChangeLogId"] = lastChangeLogId.Value;
        }
        
        // Filter by operation type
        if (!includeDeletes)
        {
            whereClauses.Add("cl.operation != 'DELETE'");
        }
        
        // Build the query - join change log with main table
        var selectClause = additionalFilter?.Select.Count > 0 
            ? string.Join(", ", additionalFilter.Select.Select(col => $"t.{QuoteIdentifier(col)}"))
            : "t.*";
        
        var sql = $@"
            SELECT {selectClause}, cl.change_id, cl.operation, cl.changed_at
            FROM {quotedChangeLogTable} cl
            LEFT JOIN {quotedTable} t ON cl.table_name = @tableName";
        
        parameters["tableName"] = tableName;
        
        // Add change log specific WHERE clauses
        whereClauses.Add("cl.table_name = @tableName");
        
        // Add additional filter if provided (applied to main table)
        if (additionalFilter?.Where != null)
        {
            var additionalSql = additionalFilter.Where.ToSql(parameters);
            whereClauses.Add($"({additionalSql})");
        }
        
        if (whereClauses.Count > 0)
        {
            sql += $" WHERE {string.Join(" AND ", whereClauses)}";
        }
        
        // Order by change log ID for deterministic results
        sql += " ORDER BY cl.change_id ASC";
        
        if (maxRows.HasValue)
        {
            sql += $" LIMIT {maxRows.Value}";
        }
        
        return new ParameterizedSql(sql, parameters);
    }
    
    private static string BuildWatermarkConditions(
        IReadOnlyList<string> watermarkColumns, 
        Dictionary<string, object?> watermarkValues,
        Dictionary<string, object?> parameters)
    {
        if (watermarkColumns.Count == 1)
        {
            // Simple case: single watermark column
            var column = watermarkColumns[0];
            var quotedColumn = QuoteIdentifier(column);
            var paramName = $"watermark_{column}";
            
            parameters[paramName] = watermarkValues.GetValueOrDefault(column);
            return $"{quotedColumn} > @{paramName}";
        }
        
        // Complex case: composite watermark (lexicographic ordering)
        var conditions = new List<string>();
        
        for (int i = 0; i < watermarkColumns.Count; i++)
        {
            var currentConditions = new List<string>();
            
            // Add equality conditions for all previous columns
            for (int j = 0; j < i; j++)
            {
                var column = watermarkColumns[j];
                var quotedColumn = QuoteIdentifier(column);
                var paramName = $"watermark_{column}";
                
                parameters[paramName] = watermarkValues.GetValueOrDefault(column);
                currentConditions.Add($"{quotedColumn} = @{paramName}");
            }
            
            // Add greater-than condition for current column
            var currentColumn = watermarkColumns[i];
            var quotedCurrentColumn = QuoteIdentifier(currentColumn);
            var currentParamName = $"watermark_{currentColumn}";
            
            parameters[currentParamName] = watermarkValues.GetValueOrDefault(currentColumn);
            currentConditions.Add($"{quotedCurrentColumn} > @{currentParamName}");
            
            conditions.Add($"({string.Join(" AND ", currentConditions)})");
        }
        
        return string.Join(" OR ", conditions);
    }
    
    private static string BuildOrderByClause(
        IReadOnlyList<IOrderByClause>? customOrdering,
        IReadOnlyList<string> defaultColumns)
    {
        if (customOrdering != null && customOrdering.Count > 0)
        {
            return string.Join(", ", customOrdering.Select(clause => 
                $"{QuoteIdentifier(clause.Column)} {(clause.Direction == SortDirection.Ascending ? "ASC" : "DESC")}"));
        }
        
        // Default to ordering by watermark columns ascending
        return string.Join(", ", defaultColumns.Select(col => $"{QuoteIdentifier(col)} ASC"));
    }
    
    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}