using System.Text;
using DB2XL.Core.Models;
using DB2XL.Core.Enums;

namespace DB2XL.Query;

/// <summary>
/// Builds SQL JOIN clauses from JoinInfo models with proper security and validation
/// </summary>
public sealed class JoinBuilder
{
    private readonly SecurityFilter _securityFilter;
    
    public JoinBuilder(SecurityFilter? securityFilter = null)
    {
        _securityFilter = securityFilter ?? new SecurityFilter(new SecurityFilterConfig());
    }
    
    /// <summary>
    /// Builds SQL JOIN clauses for a collection of joins
    /// </summary>
    public JoinBuildResult BuildJoins(IEnumerable<JoinInfo> joins, Dictionary<string, object?> parameters)
    {
        if (!joins.Any())
        {
            return new JoinBuildResult(string.Empty, Array.Empty<string>());
        }
        
        var errors = new List<string>();
        var joinClauses = new List<string>();
        
        foreach (var join in joins)
        {
            var result = BuildSingleJoin(join, parameters);
            if (result.IsValid)
            {
                joinClauses.Add(result.Sql);
            }
            else
            {
                errors.AddRange(result.Errors);
            }
        }
        
        if (errors.Any())
        {
            return new JoinBuildResult(string.Empty, errors);
        }
        
        return new JoinBuildResult(string.Join(" ", joinClauses), Array.Empty<string>());
    }
    
    /// <summary>
    /// Builds a single JOIN clause from a JoinInfo
    /// </summary>
    public JoinBuildResult BuildSingleJoin(JoinInfo join, Dictionary<string, object?> parameters)
    {
        var errors = new List<string>();
        
        // Validate the join structure
        if (!join.IsValid)
        {
            errors.Add("Invalid join configuration: missing required table or column information");
            return new JoinBuildResult(string.Empty, errors);
        }
        
        // Validate security access for both sides
        ValidateTableAccess(join.Left.Table, "left", errors);
        ValidateTableAccess(join.Right.Table, "right", errors);
        ValidateColumnAccess(join.Left.Table, join.Left.Column, "left", errors);
        ValidateColumnAccess(join.Right.Table, join.Right.Column, "right", errors);
        
        if (errors.Any())
        {
            return new JoinBuildResult(string.Empty, errors);
        }
        
        // Build the JOIN SQL
        var joinType = GetJoinTypeClause(join.Type);
        var leftTable = FormatTableReference(join.Left);
        var rightTable = FormatTableReference(join.Right);
        var leftColumn = FormatColumnReference(join.Left);
        var rightColumn = FormatColumnReference(join.Right);
        
        var sql = $"{joinType} {rightTable} ON {leftColumn} = {rightColumn}";
        
        return new JoinBuildResult(sql, Array.Empty<string>());
    }
    
    /// <summary>
    /// Builds ATTACH DATABASE statements for multi-database joins
    /// </summary>
    public AttachBuildResult BuildAttachStatements(IEnumerable<AttachInfo> attachments)
    {
        if (!attachments.Any())
        {
            return new AttachBuildResult(Array.Empty<string>(), new Dictionary<string, object?>());
        }
        
        var errors = new List<string>();
        var attachStatements = new List<string>();
        var attachParameters = new Dictionary<string, object?>();
        
        foreach (var attach in attachments)
        {
            if (!attach.IsValid)
            {
                errors.Add($"Invalid attach configuration for alias '{attach.Alias}'");
                continue;
            }
            
            // Validate supported database types
            if (!IsSupportedAttachType(attach.Type))
            {
                errors.Add($"Unsupported attach type '{attach.Type}' for alias '{attach.Alias}'");
                continue;
            }
            
            // Generate parameterized ATTACH statement
            var sql = attach.ToAttachSql();
            var paramName = attach.PathParameterName;
            
            attachStatements.Add(sql);
            attachParameters[paramName] = attach.Path;
        }
        
        if (errors.Any())
        {
            return new AttachBuildResult(Array.Empty<string>(), new Dictionary<string, object?>(), errors);
        }
        
        return new AttachBuildResult(attachStatements, attachParameters);
    }
    
    /// <summary>
    /// Gets the SQL clause for a join type
    /// </summary>
    private static string GetJoinTypeClause(JoinType joinType) =>
        joinType switch
        {
            JoinType.Inner => "INNER JOIN",
            JoinType.Left => "LEFT JOIN", 
            JoinType.Right => "RIGHT JOIN",
            JoinType.Full => "FULL OUTER JOIN",
            _ => throw new ArgumentException($"Unsupported join type: {joinType}")
        };
    
    /// <summary>
    /// Formats a table reference with optional alias
    /// </summary>
    private static string FormatTableReference(TableReference tableRef)
    {
        var tableName = EscapeIdentifier(tableRef.Table);
        return tableRef.Alias != null 
            ? $"{tableName} AS {EscapeIdentifier(tableRef.Alias)}" 
            : tableName;
    }
    
    /// <summary>
    /// Formats a column reference with qualified table name
    /// </summary>
    private static string FormatColumnReference(TableReference tableRef)
    {
        var qualifiedTable = tableRef.Alias ?? tableRef.Table;
        return $"{EscapeIdentifier(qualifiedTable)}.{EscapeIdentifier(tableRef.Column)}";
    }
    
    /// <summary>
    /// Escapes SQLite identifiers by doubling quotes
    /// </summary>
    private static string EscapeIdentifier(string identifier) =>
        $"\"{identifier.Replace("\"", "\"\"")}\"";
    
    /// <summary>
    /// Validates table access using security filter
    /// </summary>
    private void ValidateTableAccess(string tableName, string side, List<string> errors)
    {
        var result = _securityFilter.ValidateTable(tableName);
        if (!result.IsAllowed)
        {
            errors.Add($"Access denied to {side} table '{tableName}'" +
                      (result.DenialReason != null ? $" - {result.DenialReason}" : ""));
        }
    }
    
    /// <summary>
    /// Validates column access using security filter
    /// </summary>
    private void ValidateColumnAccess(string tableName, string columnName, string side, List<string> errors)
    {
        var result = _securityFilter.ValidateColumn(tableName, columnName);
        if (!result.IsAllowed)
        {
            errors.Add($"Access denied to {side} column '{tableName}.{columnName}'" +
                      (result.DenialReason != null ? $" - {result.DenialReason}" : ""));
        }
    }
    
    /// <summary>
    /// Checks if the attach type is supported
    /// </summary>
    private static bool IsSupportedAttachType(string type) =>
        type.ToLowerInvariant() is "sqlite" or "csv";
}

/// <summary>
/// Result of building JOIN clauses
/// </summary>
public sealed record JoinBuildResult(
    string Sql,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Result of building ATTACH statements
/// </summary>
public sealed record AttachBuildResult(
    IReadOnlyList<string> Statements,
    IReadOnlyDictionary<string, object?> Parameters,
    IReadOnlyList<string>? Errors = null)
{
    public bool IsValid => (Errors?.Count ?? 0) == 0;
    
    public AttachBuildResult(IReadOnlyList<string> statements, IReadOnlyDictionary<string, object?> parameters)
        : this(statements, parameters, Array.Empty<string>())
    {
    }
}