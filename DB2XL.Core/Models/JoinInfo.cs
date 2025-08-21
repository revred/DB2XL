using DB2XL.Core.Enums;

namespace DB2XL.Core.Models;

/// <summary>
/// Represents table reference in a join operation
/// </summary>
public sealed record TableReference(
    string Table,
    string Column,
    string? Alias = null)
{
    /// <summary>
    /// Gets the qualified table name with alias if present
    /// </summary>
    public string QualifiedTableName => Alias ?? Table;
    
    /// <summary>
    /// Gets the qualified column reference
    /// </summary>
    public string QualifiedColumnName => $"{QualifiedTableName}.{Column}";
}

/// <summary>
/// Represents a join operation between two tables
/// </summary>
public sealed record JoinInfo(
    JoinType Type,
    TableReference Left,
    TableReference Right)
{
    /// <summary>
    /// Validates that the join is properly configured
    /// </summary>
    public bool IsValid => 
        !string.IsNullOrWhiteSpace(Left.Table) &&
        !string.IsNullOrWhiteSpace(Left.Column) &&
        !string.IsNullOrWhiteSpace(Right.Table) &&
        !string.IsNullOrWhiteSpace(Right.Column);
        
    /// <summary>
    /// Gets a string representation of the join for debugging
    /// </summary>
    public override string ToString() => 
        $"{Type} JOIN {Right.QualifiedTableName} ON {Left.QualifiedColumnName} = {Right.QualifiedColumnName}";
}