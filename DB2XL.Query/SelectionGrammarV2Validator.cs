using System.Text.Json;
using System.Text.RegularExpressions;
using DB2XL.Core.Models;
using ValidationResult = DB2XL.Core.Models.ValidationResult;

namespace DB2XL.Query;

/// <summary>
/// Validates SelectionGrammar v2 JSON structure and security constraints
/// </summary>
public sealed class SelectionGrammarV2Validator
{
    private readonly SecurityFilter _securityFilter;
    
    public SelectionGrammarV2Validator(SecurityFilter? securityFilter = null)
    {
        _securityFilter = securityFilter ?? new SecurityFilter(new SecurityFilterConfig());
    }
    
    /// <summary>
    /// Validates a SelectionGrammar v2 JSON document
    /// </summary>
    public ValidationResult ValidateJson(string json)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            
            // Validate required table property
            if (!root.TryGetProperty("table", out var tableProperty) || 
                string.IsNullOrWhiteSpace(tableProperty.GetString()))
            {
                errors.Add("Missing or empty required property: 'table'");
                return new ValidationResult
                {
                    IsValid = false,
                    Errors = errors,
                    Warnings = warnings
                };
            }
            
            var tableName = tableProperty.GetString()!;
            ValidateIdentifier(tableName, "table", errors);
            ValidateTableAccess(tableName, errors);
            
            // Validate optional properties
            ValidateAttachProperty(root, errors, warnings);
            ValidateJoinsProperty(root, errors, warnings);
            ValidateSelectProperty(root, tableName, errors, warnings);
            ValidateWhereProperty(root, tableName, errors, warnings);
            ValidateOrderByProperty(root, tableName, errors, warnings);
            ValidatePaginationProperties(root, errors, warnings);
            
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid JSON structure: {ex.Message}");
        }
        
        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }
    
    /// <summary>
    /// Validates the attach property for multi-database support
    /// </summary>
    private void ValidateAttachProperty(JsonElement root, List<string> errors, List<string> warnings)
    {
        if (!root.TryGetProperty("attach", out var attachProperty))
            return;
            
        if (attachProperty.ValueKind != JsonValueKind.Array)
        {
            errors.Add("Property 'attach' must be an array");
            return;
        }
        
        foreach (var attachElement in attachProperty.EnumerateArray())
        {
            ValidateAttachElement(attachElement, errors, warnings);
        }
    }
    
    private void ValidateAttachElement(JsonElement attachElement, List<string> errors, List<string> warnings)
    {
        if (attachElement.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Attach element must be an object");
            return;
        }
        
        // Validate required properties
        var requiredProps = new[] { "alias", "type", "path" };
        foreach (var prop in requiredProps)
        {
            if (!attachElement.TryGetProperty(prop, out var propElement) ||
                string.IsNullOrWhiteSpace(propElement.GetString()))
            {
                errors.Add($"Attach element missing required property: '{prop}'");
            }
        }
        
        // Validate alias is valid SQLite identifier
        if (attachElement.TryGetProperty("alias", out var aliasElement))
        {
            var alias = aliasElement.GetString();
            if (!string.IsNullOrEmpty(alias))
            {
                ValidateIdentifier(alias, "attach.alias", errors);
            }
        }
        
        // Validate supported types
        if (attachElement.TryGetProperty("type", out var typeElement))
        {
            var type = typeElement.GetString();
            if (!string.IsNullOrEmpty(type) && !IsValidAttachType(type))
            {
                errors.Add($"Unsupported attach type: '{type}'. Supported types: sqlite, csv");
            }
        }
    }
    
    /// <summary>
    /// Validates the joins property
    /// </summary>
    private void ValidateJoinsProperty(JsonElement root, List<string> errors, List<string> warnings)
    {
        if (!root.TryGetProperty("joins", out var joinsProperty))
            return;
            
        if (joinsProperty.ValueKind != JsonValueKind.Array)
        {
            errors.Add("Property 'joins' must be an array");
            return;
        }
        
        foreach (var joinElement in joinsProperty.EnumerateArray())
        {
            ValidateJoinElement(joinElement, errors, warnings);
        }
    }
    
    private void ValidateJoinElement(JsonElement joinElement, List<string> errors, List<string> warnings)
    {
        if (joinElement.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Join element must be an object");
            return;
        }
        
        // Validate join type
        if (joinElement.TryGetProperty("type", out var typeElement))
        {
            var joinType = typeElement.GetString();
            if (!IsValidJoinType(joinType))
            {
                errors.Add($"Invalid join type: '{joinType}'. Supported: inner, left, right, full");
            }
        }
        else
        {
            errors.Add("Join element missing required property: 'type'");
        }
        
        // Validate left and right table references
        ValidateTableReference(joinElement, "left", errors);
        ValidateTableReference(joinElement, "right", errors);
    }
    
    private void ValidateTableReference(JsonElement joinElement, string side, List<string> errors)
    {
        if (!joinElement.TryGetProperty(side, out var refElement) ||
            refElement.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"Join element missing required property: '{side}'");
            return;
        }
        
        if (!refElement.TryGetProperty("table", out var tableElement) ||
            string.IsNullOrWhiteSpace(tableElement.GetString()))
        {
            errors.Add($"Join {side} reference missing required property: 'table'");
        }
        
        if (!refElement.TryGetProperty("col", out var colElement) ||
            string.IsNullOrWhiteSpace(colElement.GetString()))
        {
            errors.Add($"Join {side} reference missing required property: 'col'");
        }
        
        // Validate identifiers
        if (refElement.TryGetProperty("table", out tableElement))
        {
            var table = tableElement.GetString();
            if (!string.IsNullOrEmpty(table))
            {
                ValidateIdentifier(table, $"join.{side}.table", errors);
            }
        }
        
        if (refElement.TryGetProperty("col", out colElement))
        {
            var column = colElement.GetString();
            if (!string.IsNullOrEmpty(column))
            {
                ValidateIdentifier(column, $"join.{side}.col", errors);
            }
        }
    }
    
    /// <summary>
    /// Validates the select property
    /// </summary>
    private void ValidateSelectProperty(JsonElement root, string tableName, List<string> errors, List<string> warnings)
    {
        if (!root.TryGetProperty("select", out var selectProperty))
            return;
            
        if (selectProperty.ValueKind != JsonValueKind.Array)
        {
            errors.Add("Property 'select' must be an array");
            return;
        }
        
        foreach (var selectElement in selectProperty.EnumerateArray())
        {
            if (selectElement.ValueKind != JsonValueKind.String)
            {
                errors.Add("Select elements must be strings");
                continue;
            }
            
            var column = selectElement.GetString();
            if (string.IsNullOrWhiteSpace(column))
            {
                errors.Add("Select column cannot be empty");
                continue;
            }
            
            if (column != "*")
            {
                ValidateColumnAccess(tableName, column, errors);
            }
        }
    }
    
    /// <summary>
    /// Validates the where property with enhanced expression support
    /// </summary>
    private void ValidateWhereProperty(JsonElement root, string tableName, List<string> errors, List<string> warnings)
    {
        if (!root.TryGetProperty("where", out var whereProperty))
            return;
            
        ValidateWhereExpression(whereProperty, tableName, errors, warnings);
    }
    
    private void ValidateWhereExpression(JsonElement whereElement, string tableName, List<string> errors, List<string> warnings)
    {
        if (whereElement.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Where expression must be an object");
            return;
        }
        
        // Check for logical operators
        if (whereElement.TryGetProperty("and", out var andProperty))
        {
            ValidateLogicalExpression(andProperty, "and", tableName, errors, warnings);
        }
        else if (whereElement.TryGetProperty("or", out var orProperty))
        {
            ValidateLogicalExpression(orProperty, "or", tableName, errors, warnings);
        }
        else
        {
            // Must be a comparison expression
            ValidateComparisonExpression(whereElement, tableName, errors, warnings);
        }
    }
    
    private void ValidateLogicalExpression(JsonElement logicalElement, string logicalOp, string tableName, List<string> errors, List<string> warnings)
    {
        if (logicalElement.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"Logical operator '{logicalOp}' must contain an array of expressions");
            return;
        }
        
        if (logicalElement.GetArrayLength() < 2)
        {
            errors.Add($"Logical operator '{logicalOp}' must contain at least 2 expressions");
            return;
        }
        
        foreach (var exprElement in logicalElement.EnumerateArray())
        {
            ValidateWhereExpression(exprElement, tableName, errors, warnings);
        }
    }
    
    private void ValidateComparisonExpression(JsonElement compElement, string tableName, List<string> errors, List<string> warnings)
    {
        // Validate required properties
        if (!compElement.TryGetProperty("col", out var colElement) ||
            string.IsNullOrWhiteSpace(colElement.GetString()))
        {
            errors.Add("Comparison expression missing required property: 'col'");
            return;
        }
        
        if (!compElement.TryGetProperty("op", out var opElement) ||
            string.IsNullOrWhiteSpace(opElement.GetString()))
        {
            errors.Add("Comparison expression missing required property: 'op'");
            return;
        }
        
        var column = colElement.GetString()!;
        var op = opElement.GetString()!;
        
        ValidateColumnAccess(tableName, column, errors);
        
        if (!IsValidComparisonOperator(op))
        {
            errors.Add($"Invalid comparison operator: '{op}'");
        }
        
        // Validate value property based on operator
        if (compElement.TryGetProperty("val", out var valElement))
        {
            ValidateComparisonValue(op, valElement, errors, warnings);
        }
        else if (op != "isNull" && op != "isNotNull")
        {
            errors.Add($"Comparison expression with operator '{op}' requires 'val' property");
        }
    }
    
    /// <summary>
    /// Validates orderBy property
    /// </summary>
    private void ValidateOrderByProperty(JsonElement root, string tableName, List<string> errors, List<string> warnings)
    {
        if (!root.TryGetProperty("orderBy", out var orderByProperty))
            return;
            
        if (orderByProperty.ValueKind != JsonValueKind.Array)
        {
            errors.Add("Property 'orderBy' must be an array");
            return;
        }
        
        foreach (var orderElement in orderByProperty.EnumerateArray())
        {
            ValidateOrderByElement(orderElement, tableName, errors, warnings);
        }
    }
    
    private void ValidateOrderByElement(JsonElement orderElement, string tableName, List<string> errors, List<string> warnings)
    {
        if (orderElement.ValueKind != JsonValueKind.Object)
        {
            errors.Add("OrderBy element must be an object");
            return;
        }
        
        if (!orderElement.TryGetProperty("col", out var colElement) ||
            string.IsNullOrWhiteSpace(colElement.GetString()))
        {
            errors.Add("OrderBy element missing required property: 'col'");
            return;
        }
        
        var column = colElement.GetString()!;
        ValidateColumnAccess(tableName, column, errors);
        
        if (orderElement.TryGetProperty("dir", out var dirElement))
        {
            var direction = dirElement.GetString();
            if (!string.IsNullOrEmpty(direction) && !IsValidSortDirection(direction))
            {
                errors.Add($"Invalid sort direction: '{direction}'. Use 'asc' or 'desc'");
            }
        }
    }
    
    /// <summary>
    /// Validates pagination properties (limit/offset)
    /// </summary>
    private void ValidatePaginationProperties(JsonElement root, List<string> errors, List<string> warnings)
    {
        if (root.TryGetProperty("limit", out var limitElement))
        {
            if (limitElement.ValueKind != JsonValueKind.Number ||
                !limitElement.TryGetInt32(out var limit) ||
                limit <= 0)
            {
                errors.Add("Property 'limit' must be a positive integer");
            }
            else if (limit > 1_000_000)
            {
                warnings.Add($"Large limit value ({limit}) may impact performance");
            }
        }
        
        if (root.TryGetProperty("offset", out var offsetElement))
        {
            if (offsetElement.ValueKind != JsonValueKind.Number ||
                !offsetElement.TryGetInt32(out var offset) ||
                offset < 0)
            {
                errors.Add("Property 'offset' must be a non-negative integer");
            }
            
            // Offset requires limit
            if (!root.TryGetProperty("limit", out _))
            {
                errors.Add("Property 'offset' requires 'limit' to be specified");
            }
        }
    }
    
    #region Validation Helpers
    
    private void ValidateIdentifier(string identifier, string propertyName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            errors.Add($"Identifier '{propertyName}' cannot be empty");
            return;
        }
        
        if (identifier.Length > 64)
        {
            errors.Add($"Identifier '{propertyName}' exceeds maximum length of 64 characters");
        }
        
        if (!IsValidSqliteIdentifier(identifier))
        {
            errors.Add($"Invalid SQLite identifier '{identifier}' in property '{propertyName}'");
        }
    }
    
    private void ValidateTableAccess(string tableName, List<string> errors)
    {
        var result = _securityFilter.ValidateTable(tableName);
        if (!result.IsAllowed)
        {
            errors.Add($"Access denied to table: '{tableName}'" + 
                      (result.DenialReason != null ? $" - {result.DenialReason}" : ""));
        }
    }
    
    private void ValidateColumnAccess(string tableName, string columnName, List<string> errors)
    {
        var result = _securityFilter.ValidateColumn(tableName, columnName);
        if (!result.IsAllowed)
        {
            errors.Add($"Access denied to column: '{tableName}.{columnName}'" + 
                      (result.DenialReason != null ? $" - {result.DenialReason}" : ""));
        }
    }
    
    private static void ValidateComparisonValue(string op, JsonElement valElement, List<string> errors, List<string> warnings)
    {
        switch (op.ToLowerInvariant())
        {
            case "in":
            case "notin":
                if (valElement.ValueKind != JsonValueKind.Array)
                {
                    errors.Add($"Operator '{op}' requires an array value");
                }
                else if (valElement.GetArrayLength() == 0)
                {
                    warnings.Add($"Empty array for '{op}' operator will match no rows");
                }
                break;
                
            case "between":
                if (valElement.ValueKind != JsonValueKind.Array ||
                    valElement.GetArrayLength() != 2)
                {
                    errors.Add("Operator 'between' requires an array with exactly 2 values");
                }
                break;
                
            case "isnull":
            case "isnotnull":
                if (valElement.ValueKind != JsonValueKind.Null)
                {
                    warnings.Add($"Operator '{op}' ignores the provided value");
                }
                break;
        }
    }
    
    private static bool IsValidSqliteIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return false;
            
        // SQLite identifiers can start with letter or underscore
        if (!char.IsLetter(identifier[0]) && identifier[0] != '_')
            return false;
            
        // Rest can be letters, digits, or underscores
        return identifier.Skip(1).All(c => char.IsLetterOrDigit(c) || c == '_');
    }
    
    private static bool IsValidAttachType(string type) =>
        type.ToLowerInvariant() is "sqlite" or "csv";
    
    private static bool IsValidJoinType(string? joinType) =>
        joinType?.ToLowerInvariant() is "inner" or "left" or "right" or "full";
    
    private static bool IsValidComparisonOperator(string op) =>
        op.ToLowerInvariant() is "=" or "==" or "!=" or "<>" or "<" or "<=" or ">" or ">=" or 
        "like" or "notlike" or "in" or "notin" or "between" or "isnull" or "isnotnull";
    
    private static bool IsValidSortDirection(string direction) =>
        direction.ToLowerInvariant() is "asc" or "ascending" or "desc" or "descending";
    
    #endregion
}

// ValidationResult removed - using DB2XL.Core.Models.ValidationResult instead