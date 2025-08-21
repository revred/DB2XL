using System.Text.Json;
using System.Text.Json.Serialization;
using DB2XL.Core.Models;
using DB2XL.Core.Enums;
using SortDirection = DB2XL.Core.Models.SortDirection;

namespace DB2XL.Query;

/// <summary>
/// Concrete implementation of selection grammar
/// </summary>
public sealed record SelectionGrammar : ISelectionGrammar
{
    [JsonPropertyName("table")]
    public string Table { get; init; } = string.Empty;
    
    [JsonPropertyName("select")]
    public IReadOnlyList<string> Select { get; init; } = Array.Empty<string>();
    
    [JsonPropertyName("where")]
    [JsonConverter(typeof(WhereExpressionJsonConverter))]
    public IWhereExpression? Where { get; init; }
    
    [JsonPropertyName("orderBy")]
    public IReadOnlyList<IOrderByClause> OrderBy { get; init; } = Array.Empty<IOrderByClause>();
    
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }
    
    [JsonPropertyName("offset")]
    public int? Offset { get; init; }
    
    // V2 Enhanced Properties
    
    /// <summary>
    /// ATTACH DATABASE statements for multi-database operations
    /// </summary>
    [JsonPropertyName("attach")]
    public IReadOnlyList<AttachInfo> Attach { get; init; } = Array.Empty<AttachInfo>();
    
    /// <summary>
    /// JOIN operations for multi-table queries
    /// </summary>
    [JsonPropertyName("joins")]
    public IReadOnlyList<JoinInfo> Joins { get; init; } = Array.Empty<JoinInfo>();
    
    /// <summary>
    /// Enhanced WHERE expressions with nested AND/OR support
    /// </summary>
    [JsonPropertyName("whereV2")]
    public WhereExpression? WhereV2 { get; init; }
    
    /// <summary>
    /// Enhanced ORDER BY clauses
    /// </summary>
    [JsonPropertyName("orderByV2")]
    public IReadOnlyList<OrderByInfo> OrderByV2 { get; init; } = Array.Empty<OrderByInfo>();
    
    /// <summary>
    /// Consolidated pagination settings
    /// </summary>
    [JsonPropertyName("pagination")]
    public PaginationInfo? Pagination { get; init; }
    
    /// <summary>
    /// Creates a simple selection for a table with all columns
    /// </summary>
    public static SelectionGrammar All(string table) => new()
    {
        Table = table,
        Select = new[] { "*" }
    };
    
    /// <summary>
    /// Creates a selection with specific columns
    /// </summary>
    public static SelectionGrammar Columns(string table, params string[] columns) => new()
    {
        Table = table,
        Select = columns
    };
}

/// <summary>
/// Concrete implementation of ORDER BY clause
/// </summary>
public sealed record OrderByClause : IOrderByClause
{
    [JsonPropertyName("col")]
    public string Column { get; init; } = string.Empty;
    
    [JsonPropertyName("dir")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SortDirection Direction { get; init; } = SortDirection.Ascending;
    
    public static OrderByClause Asc(string column) => new() { Column = column, Direction = SortDirection.Ascending };
    public static OrderByClause Desc(string column) => new() { Column = column, Direction = SortDirection.Descending };
}

/// <summary>
/// Helper class to build selection grammar fluently
/// </summary>
public static class SelectionBuilder
{
    public static SelectionGrammarBuilder From(string table) => new(table);
}

/// <summary>
/// Fluent builder for selection grammar
/// </summary>
public sealed class SelectionGrammarBuilder
{
    private readonly string _table;
    private readonly List<string> _select = new();
    private IWhereExpression? _where;
    private readonly List<IOrderByClause> _orderBy = new();
    private int? _limit;
    private int? _offset;
    
    internal SelectionGrammarBuilder(string table)
    {
        _table = table;
    }
    
    public SelectionGrammarBuilder Select(params string[] columns)
    {
        _select.AddRange(columns);
        return this;
    }
    
    public SelectionGrammarBuilder SelectAll()
    {
        _select.Clear();
        _select.Add("*");
        return this;
    }
    
    public SelectionGrammarBuilder Where(IWhereExpression where)
    {
        _where = where;
        return this;
    }
    
    public SelectionGrammarBuilder OrderBy(string column, SortDirection direction = SortDirection.Ascending)
    {
        _orderBy.Add(new OrderByClause { Column = column, Direction = direction });
        return this;
    }
    
    public SelectionGrammarBuilder OrderByAsc(string column) => OrderBy(column, SortDirection.Ascending);
    public SelectionGrammarBuilder OrderByDesc(string column) => OrderBy(column, SortDirection.Descending);
    
    public SelectionGrammarBuilder Limit(int limit)
    {
        _limit = limit;
        return this;
    }
    
    public SelectionGrammarBuilder Offset(int offset)
    {
        _offset = offset;
        return this;
    }
    
    public SelectionGrammar Build() => new()
    {
        Table = _table,
        Select = _select.Count > 0 ? _select.ToArray() : new[] { "*" },
        Where = _where,
        OrderBy = _orderBy.ToArray(),
        Limit = _limit,
        Offset = _offset
    };
}

/// <summary>
/// JSON converter for WhereExpression objects
/// </summary>
internal sealed class WhereExpressionJsonConverter : JsonConverter<IWhereExpression>
{
    public override IWhereExpression? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected JSON object for WhereExpression");
        }
        
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        
        // Check if it's an AND expression
        if (root.TryGetProperty("and", out var andProperty))
        {
            var expressions = new List<IWhereExpression>();
            foreach (var item in andProperty.EnumerateArray())
            {
                var expr = ParseComparisonExpression(item);
                if (expr != null)
                {
                    expressions.Add(expr);
                }
            }
            return new AndExpression { Expressions = expressions };
        }
        
        // Check if it's an OR expression
        if (root.TryGetProperty("or", out var orProperty))
        {
            var expressions = new List<IWhereExpression>();
            foreach (var item in orProperty.EnumerateArray())
            {
                var expr = ParseComparisonExpression(item);
                if (expr != null)
                {
                    expressions.Add(expr);
                }
            }
            return new OrExpression { Expressions = expressions };
        }
        
        // Otherwise treat as a comparison expression
        return ParseComparisonExpression(root);
    }
    
    private static ComparisonExpression? ParseComparisonExpression(JsonElement element)
    {
        if (!element.TryGetProperty("col", out var colProperty) ||
            !element.TryGetProperty("op", out var opProperty) ||
            !element.TryGetProperty("val", out var valProperty))
        {
            return null;
        }
        
        var column = colProperty.GetString();
        var operatorStr = opProperty.GetString();
        
        if (string.IsNullOrEmpty(column) || string.IsNullOrEmpty(operatorStr))
        {
            return null;
        }
        
        if (!Enum.TryParse<ComparisonOperator>(operatorStr, true, out var op))
        {
            return null;
        }
        
        object? value = null;
        switch (valProperty.ValueKind)
        {
            case JsonValueKind.String:
                value = valProperty.GetString();
                break;
            case JsonValueKind.Number:
                if (valProperty.TryGetInt32(out var intVal))
                    value = intVal;
                else if (valProperty.TryGetInt64(out var longVal))
                    value = longVal;
                else
                    value = valProperty.GetDouble();
                break;
            case JsonValueKind.True:
                value = true;
                break;
            case JsonValueKind.False:
                value = false;
                break;
            case JsonValueKind.Null:
                value = null;
                break;
        }
        
        return new ComparisonExpression
        {
            Column = column,
            Operator = op,
            Value = value
        };
    }

    public override void Write(Utf8JsonWriter writer, IWhereExpression value, JsonSerializerOptions options)
    {
        // Simple placeholder implementation for serialization
        writer.WriteNullValue();
    }
}

/// <summary>
/// JSON converter for IOrderByClause interface
/// </summary>
internal sealed class OrderByClauseJsonConverter : JsonConverter<IOrderByClause>
{
    public override IOrderByClause? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected JSON object for OrderByClause");
        }
        
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        
        if (!root.TryGetProperty("col", out var colProperty))
        {
            throw new JsonException("OrderByClause requires 'col' property");
        }
        
        var column = colProperty.GetString();
        if (string.IsNullOrEmpty(column))
        {
            throw new JsonException("OrderByClause 'col' property cannot be empty");
        }
        
        var direction = SortDirection.Ascending; // default
        if (root.TryGetProperty("dir", out var dirProperty))
        {
            var dirStr = dirProperty.GetString();
            if (!string.IsNullOrEmpty(dirStr) && 
                Enum.TryParse<SortDirection>(dirStr, true, out var parsedDir))
            {
                direction = parsedDir;
            }
        }
        
        return new OrderByClause { Column = column, Direction = direction };
    }
    
    public override void Write(Utf8JsonWriter writer, IOrderByClause value, JsonSerializerOptions options)
    {
        // Simple placeholder implementation for serialization
        writer.WriteNullValue();
    }
}