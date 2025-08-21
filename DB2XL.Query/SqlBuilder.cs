using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using DB2XL.Core.Models;
using SortDirection = DB2XL.Core.Models.SortDirection;
using ComparisonExpression = DB2XL.Core.Models.ComparisonExpression;
using LogicalExpression = DB2XL.Core.Models.LogicalExpression;
using WhereExpression = DB2XL.Core.Models.WhereExpression;
using LogicalOperator = DB2XL.Core.Models.LogicalOperator;
using ComparisonOperator = DB2XL.Core.Models.ComparisonOperator;

namespace DB2XL.Query;

/// <summary>
/// Builds safe, parameterized SQL queries from SelectionGrammar with v2 enhancements
/// </summary>
public sealed class SqlBuilder : ISqlBuilder
{
    private readonly JoinBuilder _joinBuilder;
    
    public SqlBuilder(SecurityFilter? securityFilter = null)
    {
        _joinBuilder = new JoinBuilder(securityFilter);
    }
    
    /// <summary>
    /// Builds a parameterized SELECT query from selection grammar
    /// </summary>
    public ParameterizedSql BuildQuery(ISelectionGrammar selection)
    {
        var parameters = new Dictionary<string, object?>();
        var sql = new StringBuilder();
        var attachStatements = new List<string>();
        
        // Check if this is an enhanced SelectionGrammar with v2 features
        if (selection is SelectionGrammar enhancedSelection)
        {
            return BuildEnhancedQuery(enhancedSelection, parameters, sql, attachStatements);
        }
        
        // Fallback to legacy query building
        return BuildLegacyQuery(selection, parameters, sql);
    }
    
    /// <summary>
    /// Builds an enhanced query with v2 features (joins, attach, etc.)
    /// </summary>
    private ParameterizedSql BuildEnhancedQuery(SelectionGrammar selection, Dictionary<string, object?> parameters, StringBuilder sql, List<string> attachStatements)
    {
        // Handle ATTACH DATABASE statements first
        if (selection.Attach.Any())
        {
            var attachResult = _joinBuilder.BuildAttachStatements(selection.Attach);
            if (!attachResult.IsValid)
            {
                throw new InvalidOperationException($"Failed to build ATTACH statements: {string.Join("; ", attachResult.Errors)}");
            }
            
            attachStatements.AddRange(attachResult.Statements);
            foreach (var param in attachResult.Parameters)
            {
                parameters[param.Key] = param.Value;
            }
        }
        
        // SELECT clause
        sql.Append("SELECT ");
        AppendSelectClause(sql, selection.Select);
        
        // FROM clause
        sql.Append(" FROM ");
        AppendQuotedIdentifier(sql, selection.Table);
        
        // JOIN clauses
        if (selection.Joins.Any())
        {
            var joinResult = _joinBuilder.BuildJoins(selection.Joins, parameters);
            if (!joinResult.IsValid)
            {
                throw new InvalidOperationException($"Failed to build JOIN clauses: {string.Join("; ", joinResult.Errors)}");
            }
            
            if (!string.IsNullOrEmpty(joinResult.Sql))
            {
                sql.Append(" ");
                sql.Append(joinResult.Sql);
            }
        }
        
        // WHERE clause (use v2 if available, fall back to legacy)
        if (selection.WhereV2 != null)
        {
            sql.Append(" WHERE ");
            sql.Append(BuildWhereExpressionV2(selection.WhereV2, parameters));
        }
        else if (selection.Where != null)
        {
            sql.Append(" WHERE ");
            sql.Append(selection.Where.ToSql(parameters));
        }
        
        // ORDER BY clause (use v2 if available, fall back to legacy)
        IReadOnlyList<IOrderByClause> orderByList = selection.OrderByV2.Any() ? 
            selection.OrderByV2.Select(o => new LegacyOrderByClause { Column = o.Column, Direction = o.Direction }).ToList() :
            selection.OrderBy;
            
        if (orderByList.Any())
        {
            sql.Append(" ORDER BY ");
            AppendOrderByClause(sql, orderByList);
        }
        
        // LIMIT and OFFSET (use pagination if available, fall back to individual properties)
        var limit = selection.Pagination?.Limit ?? selection.Limit;
        var offset = selection.Pagination?.Offset ?? selection.Offset;
        
        if (limit.HasValue)
        {
            sql.Append(" LIMIT ");
            sql.Append(limit.Value);
        }
        
        if (offset.HasValue)
        {
            sql.Append(" OFFSET ");
            sql.Append(offset.Value);
        }
        
        return new ParameterizedSql(sql.ToString(), parameters, attachStatements);
    }
    
    /// <summary>
    /// Builds a legacy query without v2 features
    /// </summary>
    private static ParameterizedSql BuildLegacyQuery(ISelectionGrammar selection, Dictionary<string, object?> parameters, StringBuilder sql)
    {
        // SELECT clause
        sql.Append("SELECT ");
        AppendSelectClause(sql, selection.Select);
        
        // FROM clause
        sql.Append(" FROM ");
        AppendQuotedIdentifier(sql, selection.Table);
        
        // WHERE clause
        if (selection.Where != null)
        {
            sql.Append(" WHERE ");
            sql.Append(selection.Where.ToSql(parameters));
        }
        
        // ORDER BY clause
        if (selection.OrderBy.Any())
        {
            sql.Append(" ORDER BY ");
            AppendOrderByClause(sql, selection.OrderBy);
        }
        
        // LIMIT clause
        if (selection.Limit.HasValue)
        {
            sql.Append(" LIMIT ");
            sql.Append(selection.Limit.Value);
        }
        
        // OFFSET clause
        if (selection.Offset.HasValue)
        {
            sql.Append(" OFFSET ");
            sql.Append(selection.Offset.Value);
        }
        
        return new ParameterizedSql(sql.ToString(), parameters);
    }
    
    /// <summary>
    /// Builds SQL for WhereExpression v2
    /// </summary>
    private static string BuildWhereExpressionV2(WhereExpression expression, Dictionary<string, object?> parameters)
    {
        return expression switch
        {
            DB2XL.Core.Models.ComparisonExpression comp => BuildComparisonExpression(comp, parameters),
            DB2XL.Core.Models.LogicalExpression logical => BuildLogicalExpression(logical, parameters),
            _ => throw new NotSupportedException($"Unsupported where expression type: {expression.GetType()}")
        };
    }
    
    /// <summary>
    /// Builds SQL for a comparison expression
    /// </summary>
    private static string BuildComparisonExpression(DB2XL.Core.Models.ComparisonExpression comp, Dictionary<string, object?> parameters)
    {
        var column = $"\"{comp.Column.Replace("\"", "\"\"")}\"";
        var op = comp.Operator switch
        {
            ComparisonOperator.Equal => "=",
            ComparisonOperator.NotEqual => "!=",
            ComparisonOperator.LessThan => "<",
            ComparisonOperator.LessThanOrEqual => "<=",
            ComparisonOperator.GreaterThan => ">",
            ComparisonOperator.GreaterThanOrEqual => ">=",
            ComparisonOperator.Like => "LIKE",
            ComparisonOperator.In => "IN",
            ComparisonOperator.NotIn => "NOT IN",
            ComparisonOperator.Between => "BETWEEN",
            ComparisonOperator.IsNull => "IS NULL",
            ComparisonOperator.IsNotNull => "IS NOT NULL",
            _ => throw new NotSupportedException($"Unsupported operator: {comp.Operator}")
        };
        
        if (comp.Operator is ComparisonOperator.IsNull or ComparisonOperator.IsNotNull)
        {
            return $"{column} {op}";
        }
        
        var paramName = comp.ParameterName;
        parameters[paramName] = comp.Value;
        
        return comp.Operator switch
        {
            ComparisonOperator.In or ComparisonOperator.NotIn when comp.Value is System.Collections.IEnumerable enumerable => 
                $"{column} {op} ({string.Join(",", enumerable.Cast<object>().Select((_, i) => $"@{paramName}_{i}"))})",
            ComparisonOperator.Between when comp.Value is object[] arr && arr.Length == 2 =>
                $"{column} {op} @{paramName}_0 AND @{paramName}_1",
            _ => $"{column} {op} @{paramName}"
        };
    }
    
    /// <summary>
    /// Builds SQL for a logical expression
    /// </summary>
    private static string BuildLogicalExpression(DB2XL.Core.Models.LogicalExpression logical, Dictionary<string, object?> parameters)
    {
        if (!logical.Expressions.Any())
        {
            return "1=1";
        }
        
        var op = logical.Operator == LogicalOperator.And ? "AND" : "OR";
        var expressions = logical.Expressions.Select(expr => BuildWhereExpressionV2(expr, parameters));
        
        return $"({string.Join($" {op} ", expressions)})";
    }
    
    /// <summary>
    /// Builds a parameterized COUNT query from selection grammar
    /// </summary>
    public ParameterizedSql BuildCountQuery(ISelectionGrammar selection)
    {
        var parameters = new Dictionary<string, object?>();
        var sql = new StringBuilder();
        
        // SELECT COUNT(*)
        sql.Append("SELECT COUNT(*) FROM ");
        AppendQuotedIdentifier(sql, selection.Table);
        
        // WHERE clause (same as regular query)
        if (selection.Where != null)
        {
            sql.Append(" WHERE ");
            sql.Append(selection.Where.ToSql(parameters));
        }
        
        // Note: No ORDER BY, LIMIT, or OFFSET for count queries
        // TODO: Add v2 features support when SelectionGrammar is extended
        
        return new ParameterizedSql(sql.ToString(), parameters);
    }
    
    // SortDirection conversion methods removed - now using Core.Models.SortDirection directly

    /// <summary>
    /// Appends SELECT clause with column projections
    /// </summary>
    private static void AppendSelectClause(StringBuilder sql, IReadOnlyList<string> select)
    {
        if (select.Count == 0 || (select.Count == 1 && select[0] == "*"))
        {
            sql.Append("*");
            return;
        }
        
        for (int i = 0; i < select.Count; i++)
        {
            if (i > 0)
            {
                sql.Append(", ");
            }
            
            var column = select[i];
            
            // Handle "column AS alias" expressions
            if (TryParseColumnAlias(column, out var columnPart, out var aliasPart))
            {
                AppendColumnExpression(sql, columnPart);
                sql.Append(" AS ");
                AppendQuotedIdentifier(sql, aliasPart);
            }
            else
            {
                AppendColumnExpression(sql, column);
            }
        }
    }
    
    /// <summary>
    /// Appends ORDER BY clause
    /// </summary>
    private static void AppendOrderByClause(StringBuilder sql, IReadOnlyList<IOrderByClause> orderBy)
    {
        for (int i = 0; i < orderBy.Count; i++)
        {
            if (i > 0)
            {
                sql.Append(", ");
            }
            
            var clause = orderBy[i];
            AppendQuotedIdentifier(sql, clause.Column);
            
            sql.Append(clause.Direction == SortDirection.Ascending ? " ASC" : " DESC");
        }
    }
    
    /// <summary>
    /// Appends a column expression (may be a simple column or complex expression)
    /// </summary>
    private static void AppendColumnExpression(StringBuilder sql, string expression)
    {
        // For now, treat complex expressions as-is (e.g., json_extract calls)
        // In a production system, this would need more sophisticated parsing
        // to ensure only safe expressions are allowed
        
        if (IsSimpleColumnName(expression))
        {
            AppendQuotedIdentifier(sql, expression);
        }
        else if (IsSafeExpression(expression))
        {
            sql.Append(expression);
        }
        else
        {
            throw new ArgumentException($"Unsafe column expression: {expression}");
        }
    }
    
    /// <summary>
    /// Appends a quoted SQL identifier
    /// </summary>
    private static void AppendQuotedIdentifier(StringBuilder sql, string identifier)
    {
        sql.Append('"');
        sql.Append(identifier.Replace("\"", "\"\""));
        sql.Append('"');
    }
    
    /// <summary>
    /// Tries to parse "column AS alias" expressions
    /// </summary>
    private static bool TryParseColumnAlias(string expression, out string column, out string alias)
    {
        // Simple regex-free parsing for "column AS alias"
        var asIndex = expression.IndexOf(" AS ", StringComparison.OrdinalIgnoreCase);
        if (asIndex > 0)
        {
            column = expression.Substring(0, asIndex).Trim();
            alias = expression.Substring(asIndex + 4).Trim();
            return !string.IsNullOrEmpty(column) && !string.IsNullOrEmpty(alias);
        }
        
        // Try "column as alias" (lowercase)
        asIndex = expression.IndexOf(" as ", StringComparison.Ordinal);
        if (asIndex > 0)
        {
            column = expression.Substring(0, asIndex).Trim();
            alias = expression.Substring(asIndex + 4).Trim();
            return !string.IsNullOrEmpty(column) && !string.IsNullOrEmpty(alias);
        }
        
        column = string.Empty;
        alias = string.Empty;
        return false;
    }
    
    /// <summary>
    /// Checks if expression is a simple column name (alphanumeric + underscore)
    /// </summary>
    private static bool IsSimpleColumnName(string expression)
    {
        if (string.IsNullOrEmpty(expression))
            return false;
            
        return expression.All(c => char.IsLetterOrDigit(c) || c == '_');
    }
    
    /// <summary>
    /// Checks if expression is safe to include in SQL (whitelist approach)
    /// </summary>
    private static bool IsSafeExpression(string expression)
    {
        // Very conservative whitelist for SQLite expressions
        // In production, this would be much more sophisticated
        
        var allowedFunctions = new[]
        {
            "json_extract(", "json_valid(", "json_type(",
            "substr(", "length(", "upper(", "lower(", "trim(",
            "datetime(", "date(", "time(", "strftime(",
            "coalesce(", "ifnull(", "nullif(",
            "abs(", "round(", "ceil(", "floor("
        };
        
        var lowerExpression = expression.ToLowerInvariant();
        
        // Must start with an allowed function
        if (!allowedFunctions.Any(func => lowerExpression.StartsWith(func)))
        {
            return false;
        }
        
        // Must not contain dangerous keywords
        var dangerousKeywords = new[]
        {
            "drop", "delete", "insert", "update", "create", "alter",
            "grant", "revoke", "truncate", "exec", "execute",
            "union", "join", "into", "values", "set"
        };
        
        return !dangerousKeywords.Any(keyword => 
            lowerExpression.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Legacy adapter for OrderByInfo to IOrderByClause
/// </summary>
internal sealed class LegacyOrderByClause : IOrderByClause
{
    public string Column { get; init; } = string.Empty;
    public SortDirection Direction { get; init; } = SortDirection.Ascending;
}

/// <summary>
/// Default query executor implementation
/// </summary>
public sealed class QueryExecutor : IQueryExecutor
{
    /// <summary>
    /// Executes a selection query and returns results
    /// </summary>
    public IEnumerable<Dictionary<string, object?>> ExecuteQuery(SqliteConnection connection, ParameterizedSql query)
    {
        using var command = connection.CreateCommand();
        command.CommandText = query.Sql;
        
        using var reader = command.ExecuteReaderSafe(query.Parameters);
        
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>();
            
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                row[name] = value;
            }
            
            yield return row;
        }
    }
    
    /// <summary>
    /// Executes a count query and returns the row count
    /// </summary>
    public long ExecuteCount(SqliteConnection connection, ParameterizedSql query)
    {
        using var command = connection.CreateCommand();
        command.CommandText = query.Sql;
        
        var result = command.ExecuteScalarSafe(query.Parameters);
        return Convert.ToInt64(result);
    }
}

/// <summary>
/// Factory for creating selection grammar instances
/// </summary>
public sealed class SelectionGrammarFactory : ISelectionGrammarFactory
{
    /// <summary>
    /// Parses JSON selection grammar into typed objects
    /// </summary>
    public ISelectionGrammar ParseJson(string json)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
                // Converters now auto-discovered via [JsonConverter] attributes
            };
            
            var result = JsonSerializer.Deserialize<SelectionGrammar>(json, options);
            
            if (result == null)
            {
                throw new ArgumentException("JSON deserialization returned null");
            }
            
            ValidateSelectionGrammar(result);
            return result;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid JSON selection grammar: {ex.Message}", ex);
        }
    }
    
    /// <summary>
    /// Creates a simple selection for basic table export
    /// </summary>
    public ISelectionGrammar CreateSimple(string tableName, string[]? columns = null)
    {
        return new SelectionGrammar
        {
            Table = tableName,
            Select = columns ?? new[] { "*" }
        };
    }
    
    /// <summary>
    /// Validates selection grammar for safety and correctness
    /// </summary>
    private static void ValidateSelectionGrammar(SelectionGrammar grammar)
    {
        if (string.IsNullOrWhiteSpace(grammar.Table))
        {
            throw new ArgumentException("Table name cannot be empty");
        }
        
        if (grammar.Select.Count == 0)
        {
            throw new ArgumentException("Select clause cannot be empty");
        }
        
        if (grammar.Limit.HasValue && grammar.Limit.Value <= 0)
        {
            throw new ArgumentException("Limit must be positive");
        }
        
        if (grammar.Offset.HasValue && grammar.Offset.Value < 0)
        {
            throw new ArgumentException("Offset cannot be negative");
        }
        
        // Validate table name is safe
        if (!IsValidTableName(grammar.Table))
        {
            throw new ArgumentException($"Invalid table name: {grammar.Table}");
        }
    }
    
    /// <summary>
    /// Validates that table name is safe for SQL
    /// </summary>
    private static bool IsValidTableName(string tableName)
    {
        // Simple validation - alphanumeric, underscore, not starting with digit
        if (string.IsNullOrEmpty(tableName) || char.IsDigit(tableName[0]))
        {
            return false;
        }
        
        return tableName.All(c => char.IsLetterOrDigit(c) || c == '_');
    }
}