using System.Text.Json.Serialization;

namespace DB2XL.Core.Models;

/// <summary>
/// Base class for where clause expressions supporting nested AND/OR operations
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ComparisonExpression), "comparison")]
[JsonDerivedType(typeof(LogicalExpression), "logical")]
public abstract record WhereExpression
{
    /// <summary>
    /// Validates the expression structure
    /// </summary>
    public abstract bool IsValid { get; }
    
    /// <summary>
    /// Gets all column references in the expression tree
    /// </summary>
    public abstract IEnumerable<string> GetColumnReferences();
}

/// <summary>
/// Comparison expression for column operations (=, <, >, IN, LIKE, etc.)
/// </summary>
public sealed record ComparisonExpression(
    string Column,
    ComparisonOperator Operator,
    object? Value) : WhereExpression
{
    /// <summary>
    /// Validates the comparison expression
    /// </summary>
    public override bool IsValid =>
        !string.IsNullOrWhiteSpace(Column) &&
        IsValidOperatorValue(Operator, Value);
    
    /// <summary>
    /// Gets the column reference
    /// </summary>
    public override IEnumerable<string> GetColumnReferences()
    {
        yield return Column;
    }
    
    /// <summary>
    /// Gets the parameter name for this comparison
    /// </summary>
    public string ParameterName => $"p_{GetHashCode():X8}";
    
    /// <summary>
    /// Validates operator-value combinations
    /// </summary>
    private static bool IsValidOperatorValue(ComparisonOperator op, object? value) =>
        op switch
        {
            ComparisonOperator.IsNull or ComparisonOperator.IsNotNull => value is null,
            ComparisonOperator.In or ComparisonOperator.NotIn => value is System.Collections.IEnumerable,
            ComparisonOperator.Between => value is object[] array && array.Length == 2,
            _ => value is not null
        };
}

/// <summary>
/// Logical expression combining multiple expressions with AND/OR
/// </summary>
public sealed record LogicalExpression(
    LogicalOperator Operator,
    IReadOnlyList<WhereExpression> Expressions) : WhereExpression
{
    /// <summary>
    /// Validates the logical expression
    /// </summary>
    public override bool IsValid =>
        Expressions.Count >= 2 &&
        Expressions.All(e => e.IsValid);
    
    /// <summary>
    /// Gets all column references from nested expressions
    /// </summary>
    public override IEnumerable<string> GetColumnReferences() =>
        Expressions.SelectMany(e => e.GetColumnReferences());
}

/// <summary>
/// Comparison operators supported in where clauses
/// </summary>
public enum ComparisonOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    Like,
    NotLike,
    In,
    NotIn,
    Between,
    IsNull,
    IsNotNull
}

/// <summary>
/// Logical operators for combining expressions
/// </summary>
public enum LogicalOperator
{
    And,
    Or
}