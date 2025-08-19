using System.Text.Json;
using System.Text.Json.Serialization;

namespace DB2XL.Query;

/// <summary>
/// Comparison expression: col op value
/// </summary>
public sealed record ComparisonExpression : IWhereExpression
{
    [JsonPropertyName("col")]
    public string Column { get; init; } = string.Empty;
    
    [JsonPropertyName("op")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ComparisonOperator Operator { get; init; }
    
    [JsonPropertyName("val")]
    public object? Value { get; init; }
    
    public string ToSql(Dictionary<string, object?> parameters)
    {
        var paramName = $"param_{parameters.Count}";
        var quotedColumn = $"\"{Column.Replace("\"", "\"\"")}\"";
        
        return Operator switch
        {
            ComparisonOperator.Equal => HandleEquals(quotedColumn, paramName, parameters),
            ComparisonOperator.NotEqual => HandleNotEquals(quotedColumn, paramName, parameters),
            ComparisonOperator.LessThan => HandleSimple(quotedColumn, "<", paramName, parameters),
            ComparisonOperator.LessThanOrEqual => HandleSimple(quotedColumn, "<=", paramName, parameters),
            ComparisonOperator.GreaterThan => HandleSimple(quotedColumn, ">", paramName, parameters),
            ComparisonOperator.GreaterThanOrEqual => HandleSimple(quotedColumn, ">=", paramName, parameters),
            ComparisonOperator.Like => HandleSimple(quotedColumn, "LIKE", paramName, parameters),
            ComparisonOperator.Glob => HandleSimple(quotedColumn, "GLOB", paramName, parameters),
            ComparisonOperator.In => HandleIn(quotedColumn, paramName, parameters, false),
            ComparisonOperator.NotIn => HandleIn(quotedColumn, paramName, parameters, true),
            ComparisonOperator.Between => HandleBetween(quotedColumn, paramName, parameters),
            ComparisonOperator.IsNull => $"{quotedColumn} IS NULL",
            ComparisonOperator.IsNotNull => $"{quotedColumn} IS NOT NULL",
            _ => throw new ArgumentException($"Unsupported operator: {Operator}")
        };
    }
    
    private string HandleEquals(string column, string paramName, Dictionary<string, object?> parameters)
    {
        if (Value is null)
        {
            return $"{column} IS NULL";
        }
        parameters[paramName] = Value;
        return $"{column} = @{paramName}";
    }
    
    private string HandleNotEquals(string column, string paramName, Dictionary<string, object?> parameters)
    {
        if (Value is null)
        {
            return $"{column} IS NOT NULL";
        }
        parameters[paramName] = Value;
        return $"{column} != @{paramName}";
    }
    
    private string HandleSimple(string column, string op, string paramName, Dictionary<string, object?> parameters)
    {
        parameters[paramName] = Value;
        return $"{column} {op} @{paramName}";
    }
    
    private string HandleIn(string column, string paramName, Dictionary<string, object?> parameters, bool negate)
    {
        if (Value is not System.Collections.IEnumerable enumerable)
        {
            throw new ArgumentException("IN operator requires enumerable value");
        }
        
        var values = enumerable.Cast<object?>().ToList();
        if (values.Count == 0)
        {
            return negate ? "1=1" : "1=0";
        }
        
        var paramNames = new List<string>();
        for (int i = 0; i < values.Count; i++)
        {
            var pName = $"{paramName}_{i}";
            parameters[pName] = values[i];
            paramNames.Add($"@{pName}");
        }
        
        var inClause = string.Join(", ", paramNames);
        var op = negate ? "NOT IN" : "IN";
        return $"{column} {op} ({inClause})";
    }
    
    private string HandleBetween(string column, string paramName, Dictionary<string, object?> parameters)
    {
        if (Value is not System.Collections.IEnumerable enumerable)
        {
            throw new ArgumentException("BETWEEN operator requires array with two values");
        }
        
        var values = enumerable.Cast<object?>().ToArray();
        if (values.Length != 2)
        {
            throw new ArgumentException("BETWEEN operator requires exactly two values");
        }
        
        var param1 = $"{paramName}_start";
        var param2 = $"{paramName}_end";
        parameters[param1] = values[0];
        parameters[param2] = values[1];
        
        return $"{column} BETWEEN @{param1} AND @{param2}";
    }
}

/// <summary>
/// AND expression: all sub-expressions must be true
/// </summary>
public sealed record AndExpression : IWhereExpression
{
    [JsonPropertyName("and")]
    public IReadOnlyList<IWhereExpression> Expressions { get; init; } = Array.Empty<IWhereExpression>();
    
    public string ToSql(Dictionary<string, object?> parameters)
    {
        if (Expressions.Count == 0)
        {
            return "1=1";
        }
        
        var clauses = Expressions.Select(expr => $"({expr.ToSql(parameters)})");
        return string.Join(" AND ", clauses);
    }
}

/// <summary>
/// OR expression: any sub-expression must be true
/// </summary>
public sealed record OrExpression : IWhereExpression
{
    [JsonPropertyName("or")]
    public IReadOnlyList<IWhereExpression> Expressions { get; init; } = Array.Empty<IWhereExpression>();
    
    public string ToSql(Dictionary<string, object?> parameters)
    {
        if (Expressions.Count == 0)
        {
            return "1=0";
        }
        
        var clauses = Expressions.Select(expr => $"({expr.ToSql(parameters)})");
        return string.Join(" OR ", clauses);
    }
}

/// <summary>
/// NOT expression: negates the sub-expression
/// </summary>
public sealed record NotExpression : IWhereExpression
{
    [JsonPropertyName("not")]
    public IWhereExpression Expression { get; init; } = null!;
    
    public string ToSql(Dictionary<string, object?> parameters)
    {
        return $"NOT ({Expression.ToSql(parameters)})";
    }
}

/// <summary>
/// Helper class for building WHERE expressions fluently
/// </summary>
public static class Where
{
    public static ComparisonExpression Column(string column) => new() { Column = column };
    
    public static ComparisonExpression Equal(string column, object? value) => new()
    {
        Column = column,
        Operator = ComparisonOperator.Equal,
        Value = value
    };
    
    public static ComparisonExpression NotEqual(string column, object? value) => new()
    {
        Column = column,
        Operator = ComparisonOperator.NotEqual,
        Value = value
    };
    
    public static ComparisonExpression GreaterThan(string column, object value) => new()
    {
        Column = column,
        Operator = ComparisonOperator.GreaterThan,
        Value = value
    };
    
    public static ComparisonExpression GreaterThanOrEqual(string column, object value) => new()
    {
        Column = column,
        Operator = ComparisonOperator.GreaterThanOrEqual,
        Value = value
    };
    
    public static ComparisonExpression LessThan(string column, object value) => new()
    {
        Column = column,
        Operator = ComparisonOperator.LessThan,
        Value = value
    };
    
    public static ComparisonExpression LessThanOrEqual(string column, object value) => new()
    {
        Column = column,
        Operator = ComparisonOperator.LessThanOrEqual,
        Value = value
    };
    
    public static ComparisonExpression Like(string column, string pattern) => new()
    {
        Column = column,
        Operator = ComparisonOperator.Like,
        Value = pattern
    };
    
    public static ComparisonExpression In(string column, params object[] values) => new()
    {
        Column = column,
        Operator = ComparisonOperator.In,
        Value = values
    };
    
    public static ComparisonExpression NotIn(string column, params object[] values) => new()
    {
        Column = column,
        Operator = ComparisonOperator.NotIn,
        Value = values
    };
    
    public static ComparisonExpression Between(string column, object start, object end) => new()
    {
        Column = column,
        Operator = ComparisonOperator.Between,
        Value = new[] { start, end }
    };
    
    public static ComparisonExpression IsNull(string column) => new()
    {
        Column = column,
        Operator = ComparisonOperator.IsNull
    };
    
    public static ComparisonExpression IsNotNull(string column) => new()
    {
        Column = column,
        Operator = ComparisonOperator.IsNotNull
    };
    
    public static AndExpression And(params IWhereExpression[] expressions) => new()
    {
        Expressions = expressions
    };
    
    public static OrExpression Or(params IWhereExpression[] expressions) => new()
    {
        Expressions = expressions
    };
    
    public static NotExpression Not(IWhereExpression expression) => new()
    {
        Expression = expression
    };
}