using System.Text.RegularExpressions;

namespace DB2XL.Query;

/// <summary>
/// Security configuration for filtering access to tables and columns
/// </summary>
public class SecurityFilterConfig
{
    /// <summary>
    /// Tables that are explicitly allowed. If empty, all tables are allowed (subject to deny list).
    /// Supports glob patterns like "user_*" or exact matches.
    /// </summary>
    public HashSet<string> AllowedTables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// Tables that are explicitly denied. Takes precedence over allow list.
    /// Supports glob patterns like "admin_*" or exact matches.
    /// </summary>
    public HashSet<string> DeniedTables { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// Columns that are explicitly allowed per table. If empty for a table, all columns are allowed.
    /// Key is table name (case-insensitive), value is set of allowed column names.
    /// </summary>
    public Dictionary<string, HashSet<string>> AllowedColumns { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// Columns that are explicitly denied per table. Takes precedence over allow list.
    /// Key is table name (case-insensitive), value is set of denied column names.
    /// </summary>
    public Dictionary<string, HashSet<string>> DeniedColumns { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// Global column patterns that are denied across all tables (e.g., "*password*", "*secret*")
    /// </summary>
    public HashSet<string> GlobalDeniedColumnPatterns { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// Whether to enforce strict mode - if true, only explicitly allowed tables/columns are accessible
    /// </summary>
    public bool StrictMode { get; init; } = false;
    
    /// <summary>
    /// SQL injection protection configuration
    /// </summary>
    public SqlInjectionProtectionConfig? SqlInjectionProtection { get; init; } = null;
}

/// <summary>
/// Result of security filtering validation
/// </summary>
public class SecurityFilterResult
{
    public bool IsAllowed { get; init; }
    public string? DenialReason { get; init; }
    public string? SuggestedFix { get; init; }
    
    public static SecurityFilterResult Allow() => new() { IsAllowed = true };
    
    public static SecurityFilterResult Deny(string reason, string? suggestedFix = null) => new() 
    { 
        IsAllowed = false, 
        DenialReason = reason,
        SuggestedFix = suggestedFix
    };
}

/// <summary>
/// Service for validating table and column access based on security filter configuration
/// </summary>
public class SecurityFilter
{
    private readonly SecurityFilterConfig _config;
    private readonly Dictionary<string, Regex> _compiledPatterns = new();
    private readonly SqlInjectionValidator? _injectionValidator;

    public SecurityFilter(SecurityFilterConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        CompilePatterns();
        
        if (_config.SqlInjectionProtection != null)
        {
            _injectionValidator = new SqlInjectionValidator(_config.SqlInjectionProtection);
        }
    }

    /// <summary>
    /// Validates if a table is allowed to be accessed
    /// </summary>
    public SecurityFilterResult ValidateTable(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return SecurityFilterResult.Deny("Table name cannot be null or empty");
        }

        // Check denied patterns first (takes precedence)
        if (IsMatchingPattern(tableName, _config.DeniedTables))
        {
            return SecurityFilterResult.Deny(
                $"Table '{tableName}' is explicitly denied",
                "Remove the table from the denied tables list or use a different table");
        }

        // In strict mode, check if table is explicitly allowed
        if (_config.StrictMode)
        {
            if (_config.AllowedTables.Count > 0 && !IsMatchingPattern(tableName, _config.AllowedTables))
            {
                return SecurityFilterResult.Deny(
                    $"Table '{tableName}' is not in the allowed tables list (strict mode)",
                    $"Add '{tableName}' to the allowed tables list or disable strict mode");
            }
        }
        else
        {
            // In permissive mode, check if there's an allow list and the table isn't in it
            if (_config.AllowedTables.Count > 0 && !IsMatchingPattern(tableName, _config.AllowedTables))
            {
                return SecurityFilterResult.Deny(
                    $"Table '{tableName}' is not in the allowed tables list",
                    $"Add '{tableName}' to the allowed tables list or clear the allowed tables list");
            }
        }

        return SecurityFilterResult.Allow();
    }

    /// <summary>
    /// Validates if a column in a table is allowed to be accessed
    /// </summary>
    public SecurityFilterResult ValidateColumn(string tableName, string columnName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return SecurityFilterResult.Deny("Table name cannot be null or empty");
        }

        if (string.IsNullOrWhiteSpace(columnName))
        {
            return SecurityFilterResult.Deny("Column name cannot be null or empty");
        }

        // First validate the table itself
        var tableResult = ValidateTable(tableName);
        if (!tableResult.IsAllowed)
        {
            return tableResult;
        }

        // Check global denied column patterns
        foreach (var pattern in _config.GlobalDeniedColumnPatterns)
        {
            if (IsPatternMatch(columnName, pattern))
            {
                return SecurityFilterResult.Deny(
                    $"Column '{columnName}' matches global denied pattern '{pattern}'",
                    $"Remove the pattern '{pattern}' from global denied column patterns or rename the column");
            }
        }

        // Check table-specific denied columns
        if (_config.DeniedColumns.TryGetValue(tableName, out var deniedColumns))
        {
            if (IsMatchingPattern(columnName, deniedColumns))
            {
                return SecurityFilterResult.Deny(
                    $"Column '{columnName}' in table '{tableName}' is explicitly denied",
                    $"Remove '{columnName}' from the denied columns list for table '{tableName}'");
            }
        }

        // Check table-specific allowed columns
        if (_config.AllowedColumns.TryGetValue(tableName, out var allowedColumns))
        {
            if (allowedColumns.Count > 0 && !IsMatchingPattern(columnName, allowedColumns))
            {
                return SecurityFilterResult.Deny(
                    $"Column '{columnName}' in table '{tableName}' is not in the allowed columns list",
                    $"Add '{columnName}' to the allowed columns list for table '{tableName}' or clear the allowed columns list");
            }
        }

        // In strict mode, if no explicit column configuration exists, deny access
        if (_config.StrictMode && !_config.AllowedColumns.ContainsKey(tableName))
        {
            return SecurityFilterResult.Deny(
                $"No column configuration found for table '{tableName}' in strict mode",
                $"Add allowed columns configuration for table '{tableName}' or disable strict mode");
        }

        return SecurityFilterResult.Allow();
    }

    /// <summary>
    /// Validates a SelectionGrammar for security compliance
    /// </summary>
    public SecurityFilterResult ValidateSelectionGrammar(SelectionGrammar grammar)
    {
        if (grammar == null)
        {
            return SecurityFilterResult.Deny("SelectionGrammar cannot be null");
        }

        // First check for SQL injection threats
        if (_injectionValidator != null)
        {
            var injectionResult = _injectionValidator.ValidateSelectionGrammar(grammar);
            if (!injectionResult.IsSafe)
            {
                return SecurityFilterResult.Deny(
                    $"SQL injection threat detected ({injectionResult.ThreatLevel}): {injectionResult.Threat}",
                    injectionResult.SuggestedFix);
            }
        }

        // Validate the main table
        var tableResult = ValidateTable(grammar.Table);
        if (!tableResult.IsAllowed)
        {
            return tableResult;
        }

        // Validate SELECT columns
        if (grammar.Select?.Count > 0)
        {
            foreach (var column in grammar.Select)
            {
                if (column == "*")
                {
                    // For wildcard, we'll need to validate at runtime when columns are resolved
                    continue;
                }

                var columnResult = ValidateColumn(grammar.Table, column);
                if (!columnResult.IsAllowed)
                {
                    return columnResult;
                }
            }
        }

        // Validate WHERE clause columns
        if (grammar.Where != null)
        {
            var whereResult = ValidateWhereExpression(grammar.Table, grammar.Where);
            if (!whereResult.IsAllowed)
            {
                return whereResult;
            }
        }

        // Validate ORDER BY columns
        if (grammar.OrderBy?.Count > 0)
        {
            foreach (var orderBy in grammar.OrderBy)
            {
                var columnResult = ValidateColumn(grammar.Table, orderBy.Column);
                if (!columnResult.IsAllowed)
                {
                    return columnResult;
                }
            }
        }

        return SecurityFilterResult.Allow();
    }

    /// <summary>
    /// Filters a list of column names based on security configuration
    /// </summary>
    public List<string> FilterAllowedColumns(string tableName, IEnumerable<string> columnNames)
    {
        var result = new List<string>();
        
        foreach (var columnName in columnNames)
        {
            var validation = ValidateColumn(tableName, columnName);
            if (validation.IsAllowed)
            {
                result.Add(columnName);
            }
        }

        return result;
    }

    private SecurityFilterResult ValidateWhereExpression(string tableName, IWhereExpression whereExpression)
    {
        return whereExpression switch
        {
            ComparisonExpression comp => ValidateColumn(tableName, comp.Column),
            AndExpression and => ValidateLogicalExpressions(tableName, and.Expressions),
            OrExpression or => ValidateLogicalExpressions(tableName, or.Expressions),
            NotExpression not => ValidateWhereExpression(tableName, not.Expression),
            _ => SecurityFilterResult.Deny($"Unsupported WHERE expression type: {whereExpression.GetType().Name}")
        };
    }

    private SecurityFilterResult ValidateLogicalExpressions(string tableName, IReadOnlyList<IWhereExpression> expressions)
    {
        foreach (var expression in expressions)
        {
            var result = ValidateWhereExpression(tableName, expression);
            if (!result.IsAllowed)
            {
                return result;
            }
        }
        
        return SecurityFilterResult.Allow();
    }

    private void CompilePatterns()
    {
        var allPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        allPatterns.UnionWith(_config.AllowedTables);
        allPatterns.UnionWith(_config.DeniedTables);
        allPatterns.UnionWith(_config.GlobalDeniedColumnPatterns);
        
        foreach (var columnSet in _config.AllowedColumns.Values)
        {
            allPatterns.UnionWith(columnSet);
        }
        
        foreach (var columnSet in _config.DeniedColumns.Values)
        {
            allPatterns.UnionWith(columnSet);
        }

        foreach (var pattern in allPatterns)
        {
            if (pattern.Contains('*') || pattern.Contains('?'))
            {
                var regexPattern = "^" + Regex.Escape(pattern)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".") + "$";
                
                _compiledPatterns[pattern] = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
        }
    }

    private bool IsMatchingPattern(string value, IEnumerable<string> patterns)
    {
        return patterns.Any(pattern => IsPatternMatch(value, pattern));
    }

    private bool IsPatternMatch(string value, string pattern)
    {
        if (_compiledPatterns.TryGetValue(pattern, out var regex))
        {
            return regex.IsMatch(value);
        }
        
        // Exact match (case-insensitive)
        return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);
    }
}