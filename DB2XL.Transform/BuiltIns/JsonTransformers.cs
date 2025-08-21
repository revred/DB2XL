using DB2XL.Transform.Interfaces;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DB2XL.Transform.BuiltIns;

/// <summary>
/// Compacts JSON by removing unnecessary whitespace, supports both text and binary JSON formats
/// </summary>
public class JsonCompactTransformer : CellTransformerBase
{
    public JsonCompactTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        // Support both TEXT (JSON strings) and BLOB (binary JSON) columns
        var columnName = ctx.Column.ToLowerInvariant();
        var isJsonColumn = columnName.Contains("json") || 
                          columnName.Contains("data") ||
                          columnName.Contains("config") ||
                          columnName.Contains("bson") ||
                          columnName.Contains("msgpack");
        
        return (ctx.Affinity == SqliteAffinity.Text || ctx.Affinity == SqliteAffinity.Blob) && 
               (isJsonColumn || GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        try
        {
            var jsonString = ExtractJsonString(raw, ctx);
            if (jsonString == null) return raw;
            
            // Try to parse and reformat as compact JSON
            using var doc = JsonDocument.Parse(jsonString);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions 
            { 
                WriteIndented = false 
            });
        }
        catch (JsonException)
        {
            // Not valid JSON, return original
            return raw;
        }
        catch (Exception ex)
        {
            throw new TransformerException("json-compact", ctx, $"Failed to compact JSON '{raw}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extracts JSON string from various binary formats (base64, hex, compressed)
    /// </summary>
    private string? ExtractJsonString(string raw, CellContext ctx)
    {
        // If TEXT column, assume it's already JSON
        if (ctx.Affinity == SqliteAffinity.Text)
            return raw;

        var encoding = GetConfig("encoding", "auto").ToLowerInvariant();
        
        try
        {
            return encoding switch
            {
                "base64" => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(raw)),
                "hex" => System.Text.Encoding.UTF8.GetString(Convert.FromHexString(raw)),
                "auto" => AutoDetectAndDecode(raw),
                _ => raw // Unknown encoding, treat as text
            };
        }
        catch
        {
            // Failed to decode, return original
            return raw;
        }
    }

    /// <summary>
    /// Auto-detects binary JSON format and decodes it
    /// </summary>
    private string? AutoDetectAndDecode(string raw)
    {
        // Try base64 first (most common for BLOB storage)
        if (IsBase64(raw))
        {
            try
            {
                var bytes = Convert.FromBase64String(raw);
                var text = System.Text.Encoding.UTF8.GetString(bytes);
                // Check if decoded text looks like JSON
                if (text.TrimStart().StartsWith("{") || text.TrimStart().StartsWith("["))
                    return text;
            }
            catch { }
        }

        // Try hex encoding
        if (IsHex(raw))
        {
            try
            {
                var bytes = Convert.FromHexString(raw);
                var text = System.Text.Encoding.UTF8.GetString(bytes);
                if (text.TrimStart().StartsWith("{") || text.TrimStart().StartsWith("["))
                    return text;
            }
            catch { }
        }

        // If all else fails, treat as regular text
        return raw;
    }

    private bool IsBase64(string str)
    {
        return !string.IsNullOrEmpty(str) && 
               str.Length % 4 == 0 && 
               System.Text.RegularExpressions.Regex.IsMatch(str, @"^[A-Za-z0-9+/]*={0,2}$");
    }

    private bool IsHex(string str)
    {
        return !string.IsNullOrEmpty(str) && 
               str.Length % 2 == 0 && 
               System.Text.RegularExpressions.Regex.IsMatch(str, @"^[0-9A-Fa-f]+$");
    }
}

/// <summary>
/// Pretty-prints JSON with proper indentation
/// </summary>
public class JsonPrettyTransformer : CellTransformerBase
{
    public JsonPrettyTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Affinity == SqliteAffinity.Text && 
               (ctx.Column.ToLowerInvariant().Contains("json") || 
                ctx.Column.ToLowerInvariant().Contains("data") ||
                ctx.Column.ToLowerInvariant().Contains("config") ||
                GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var indent = GetConfig("indent", "  ");
        var maxDepth = GetConfigInt("maxDepth", 10);

        try
        {
            // Try to parse and reformat as pretty JSON
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                MaxDepth = maxDepth
            });
        }
        catch (JsonException)
        {
            // Not valid JSON, return original
            return raw;
        }
        catch (Exception ex)
        {
            throw new TransformerException("json-pretty", ctx, $"Failed to format JSON '{raw}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Extracts a specific value from JSON using JSONPath-like syntax
/// </summary>
public class JsonExtractTransformer : CellTransformerBase
{
    public JsonExtractTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Affinity == SqliteAffinity.Text && 
               (ctx.Column.ToLowerInvariant().Contains("json") || 
                ctx.Column.ToLowerInvariant().Contains("data") ||
                GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var path = GetConfig("path", "");
        var defaultValue = GetConfig("default", "");
        
        if (string.IsNullOrEmpty(path))
            return raw; // No path specified, return original

        try
        {
            var jsonNode = JsonNode.Parse(raw);
            if (jsonNode == null)
                return defaultValue;

            var value = ExtractJsonValue(jsonNode, path);
            return value ?? defaultValue;
        }
        catch (JsonException)
        {
            // Not valid JSON, return original
            return raw;
        }
        catch (Exception ex)
        {
            throw new TransformerException("json-extract", ctx, $"Failed to extract JSON path '{path}' from '{raw}': {ex.Message}", ex);
        }
    }

    private string? ExtractJsonValue(JsonNode? node, string path)
    {
        if (node == null) return null;
        
        // Simple path parsing - supports dot notation like "user.name" or "items[0].title"
        var parts = path.Split('.');
        JsonNode? current = node;

        foreach (var part in parts)
        {
            if (current == null) return null;

            // Handle array indexing like "items[0]"
            if (part.Contains('[') && part.EndsWith(']'))
            {
                var bracketIndex = part.IndexOf('[');
                var propertyName = part.Substring(0, bracketIndex);
                var indexStr = part.Substring(bracketIndex + 1, part.Length - bracketIndex - 2);
                
                if (int.TryParse(indexStr, out var index))
                {
                    current = current[propertyName]?[index];
                }
                else
                {
                    return null; // Invalid array index
                }
            }
            else
            {
                // Simple property access
                current = current[part];
            }
        }

        return current?.ToJsonString() ?? current?.ToString();
    }
}

/// <summary>
/// Flattens JSON object into key-value pairs with configurable separator
/// </summary>
public class JsonFlattenTransformer : CellTransformerBase
{
    public JsonFlattenTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Affinity == SqliteAffinity.Text && 
               (ctx.Column.ToLowerInvariant().Contains("json") || 
                ctx.Column.ToLowerInvariant().Contains("data") ||
                GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var separator = GetConfig("separator", ".");
        var delimiter = GetConfig("delimiter", "; ");
        var maxDepth = GetConfigInt("maxDepth", 5);

        try
        {
            var jsonNode = JsonNode.Parse(raw);
            if (jsonNode == null)
                return raw;

            var flattened = new Dictionary<string, string>();
            FlattenJsonNode(jsonNode, "", flattened, separator, 0, maxDepth);

            if (flattened.Count == 0)
                return raw;

            // Format as key=value pairs
            return string.Join(delimiter, flattened.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }
        catch (JsonException)
        {
            // Not valid JSON, return original
            return raw;
        }
        catch (Exception ex)
        {
            throw new TransformerException("json-flatten", ctx, $"Failed to flatten JSON '{raw}': {ex.Message}", ex);
        }
    }

    private void FlattenJsonNode(JsonNode? node, string prefix, Dictionary<string, string> result, string separator, int depth, int maxDepth)
    {
        if (node == null || depth >= maxDepth) return;

        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj)
                {
                    var key = string.IsNullOrEmpty(prefix) ? property.Key : $"{prefix}{separator}{property.Key}";
                    FlattenJsonNode(property.Value, key, result, separator, depth + 1, maxDepth);
                }
                break;

            case JsonArray array:
                for (int i = 0; i < array.Count; i++)
                {
                    var key = string.IsNullOrEmpty(prefix) ? $"[{i}]" : $"{prefix}[{i}]";
                    FlattenJsonNode(array[i], key, result, separator, depth + 1, maxDepth);
                }
                break;

            default:
                // Leaf value
                var value = node.ToString();
                if (!string.IsNullOrEmpty(value))
                {
                    result[prefix] = value;
                }
                break;
        }
    }
}

/// <summary>
/// Validates JSON and returns validation status or error message
/// </summary>
public class JsonValidateTransformer : CellTransformerBase
{
    public JsonValidateTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Affinity == SqliteAffinity.Text && 
               (ctx.Column.ToLowerInvariant().Contains("json") || 
                ctx.Column.ToLowerInvariant().Contains("data") ||
                GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return GetConfig("emptyResult", "EMPTY");

        var validResult = GetConfig("validResult", "VALID");
        var invalidResult = GetConfig("invalidResult", "INVALID");
        var showError = GetConfigBool("showError", false);

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return validResult;
        }
        catch (JsonException ex)
        {
            if (showError)
            {
                return $"{invalidResult}: {ex.Message}";
            }
            return invalidResult;
        }
        catch (Exception)
        {
            return invalidResult;
        }
    }
}

/// <summary>
/// Counts elements in JSON (properties in object, items in array)
/// </summary>
public class JsonCountTransformer : CellTransformerBase
{
    public JsonCountTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Affinity == SqliteAffinity.Text && 
               (ctx.Column.ToLowerInvariant().Contains("json") || 
                ctx.Column.ToLowerInvariant().Contains("data") ||
                GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "0";

        var countType = GetConfig("type", "auto"); // auto, properties, items, all

        try
        {
            var jsonNode = JsonNode.Parse(raw);
            if (jsonNode == null)
                return "0";

            return countType.ToLowerInvariant() switch
            {
                "properties" => CountProperties(jsonNode).ToString(),
                "items" => CountArrayItems(jsonNode).ToString(),
                "all" => CountAll(jsonNode).ToString(),
                _ => CountAuto(jsonNode).ToString()
            };
        }
        catch (JsonException)
        {
            // Not valid JSON, return 0
            return "0";
        }
        catch (Exception ex)
        {
            throw new TransformerException("json-count", ctx, $"Failed to count JSON elements in '{raw}': {ex.Message}", ex);
        }
    }

    private int CountProperties(JsonNode? node)
    {
        return node is JsonObject obj ? obj.Count : 0;
    }

    private int CountArrayItems(JsonNode? node)
    {
        return node is JsonArray array ? array.Count : 0;
    }

    private int CountAuto(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => obj.Count,
            JsonArray array => array.Count,
            _ => 0
        };
    }

    private int CountAll(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => CountAllRecursive(obj),
            JsonArray array => CountAllRecursive(array),
            _ => 1
        };
    }

    private int CountAllRecursive(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => obj.Sum(kvp => 1 + CountAllRecursive(kvp.Value)),
            JsonArray array => array.Sum(item => CountAllRecursive(item)),
            _ => 1
        };
    }
}