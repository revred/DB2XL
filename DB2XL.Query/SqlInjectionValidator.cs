using System.Text.RegularExpressions;

namespace DB2XL.Query;

/// <summary>
/// Configuration for SQL injection protection
/// </summary>
public class SqlInjectionProtectionConfig
{
    /// <summary>
    /// Whether to enable SQL injection protection validation
    /// </summary>
    public bool EnableProtection { get; init; } = true;
    
    /// <summary>
    /// Maximum allowed length for string values to prevent buffer overflow attacks
    /// </summary>
    public int MaxStringLength { get; init; } = 8192;
    
    /// <summary>
    /// Maximum allowed length for table and column names
    /// </summary>
    public int MaxIdentifierLength { get; init; } = 128;
    
    /// <summary>
    /// Whether to allow SQL keywords in values (more restrictive when false)
    /// </summary>
    public bool AllowSqlKeywordsInValues { get; init; } = false;
    
    /// <summary>
    /// Custom patterns to deny in values (regex patterns)
    /// </summary>
    public HashSet<string> DeniedPatterns { get; init; } = new();
    
    /// <summary>
    /// Whether to allow comments in values (-- or /**/)
    /// </summary>
    public bool AllowComments { get; init; } = false;
}

/// <summary>
/// Result of SQL injection validation
/// </summary>
public class SqlInjectionValidationResult
{
    public bool IsSafe { get; init; }
    public string? Threat { get; init; }
    public string? SuggestedFix { get; init; }
    public SqlInjectionThreatLevel ThreatLevel { get; init; }
    
    public static SqlInjectionValidationResult Safe() => new() { IsSafe = true, ThreatLevel = SqlInjectionThreatLevel.None };
    
    public static SqlInjectionValidationResult Unsafe(string threat, SqlInjectionThreatLevel level, string? suggestedFix = null) => new()
    {
        IsSafe = false,
        Threat = threat,
        ThreatLevel = level,
        SuggestedFix = suggestedFix
    };
}

/// <summary>
/// Threat level classification for SQL injection attempts
/// </summary>
public enum SqlInjectionThreatLevel
{
    None,
    Low,      // Potentially suspicious but might be legitimate
    Medium,   // Likely injection attempt
    High,     // Definite injection attempt
    Critical  // Severe injection attempt (e.g., UNION, DROP, etc.)
}

/// <summary>
/// Service for detecting and preventing SQL injection attacks in SelectionGrammar
/// </summary>
public class SqlInjectionValidator
{
    private readonly SqlInjectionProtectionConfig _config;
    private readonly HashSet<string> _sqlKeywords;
    private readonly HashSet<string> _dangerousKeywords;
    private readonly List<Regex> _injectionPatterns;
    private readonly List<Regex> _customPatterns;

    public SqlInjectionValidator(SqlInjectionProtectionConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        
        _sqlKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "DROP", "CREATE", "ALTER", "TRUNCATE",
            "UNION", "JOIN", "WHERE", "FROM", "ORDER", "GROUP", "HAVING", "INTO",
            "VALUES", "SET", "AND", "OR", "NOT", "NULL", "TRUE", "FALSE", "EXISTS",
            "LIKE", "BETWEEN", "IN", "AS", "DISTINCT", "COUNT", "SUM", "AVG", "MIN", "MAX"
        };
        
        _dangerousKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "DROP", "DELETE", "TRUNCATE", "ALTER", "CREATE", "INSERT", "UPDATE",
            "UNION", "EXEC", "EXECUTE", "xp_", "sp_", "SCRIPT", "JAVASCRIPT", "VBSCRIPT"
        };
        
        _injectionPatterns = new()
        {
            // Classic SQL injection patterns
            new Regex(@"(\b(union|select|insert|delete|update|drop|create|alter)\s+)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@";\s*(drop|delete|insert|update|create|alter)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"'\s*(or|and)\s+('.*'|[\d]+)\s*(=|<|>)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"'\s*(or|and)\s+[\d]+\s*(=|<|>)\s*[\d]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"'\s*;\s*(drop|delete|insert|update)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            
            // Comment-based injection
            new Regex(@"--[^\r\n]*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"/\*.*?\*/", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline),
            
            // Union-based injection
            new Regex(@"union\s+(all\s+)?select", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            
            // Boolean-based blind injection
            new Regex(@"'\s*(and|or)\s+\d+\s*=\s*\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"'\s*(and|or)\s+'[^']*'\s*=\s*'[^']*'", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"'='", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            
            // Time-based injection
            new Regex(@"(waitfor|delay|sleep|pg_sleep)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            
            // System function calls
            new Regex(@"\b(xp_|sp_)\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            
            // Hex and char-based encoding evasion
            new Regex(@"0x[0-9a-f]+", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"char\s*\(\s*\d+\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            
            // Script injection
            new Regex(@"<script[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline),
            new Regex(@"javascript\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new Regex(@"vbscript\s*:", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        };
        
        _customPatterns = _config.DeniedPatterns
            .Select(pattern => new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToList();
    }

    /// <summary>
    /// Validates a SelectionGrammar for SQL injection threats
    /// </summary>
    public SqlInjectionValidationResult ValidateSelectionGrammar(SelectionGrammar grammar)
    {
        if (!_config.EnableProtection)
        {
            return SqlInjectionValidationResult.Safe();
        }
        
        if (grammar == null)
        {
            return SqlInjectionValidationResult.Unsafe("SelectionGrammar cannot be null", SqlInjectionThreatLevel.Medium);
        }

        // Validate table name
        var tableResult = ValidateIdentifier(grammar.Table, "table name");
        if (!tableResult.IsSafe)
        {
            return tableResult;
        }

        // Validate column names in SELECT
        if (grammar.Select?.Count > 0)
        {
            foreach (var column in grammar.Select)
            {
                if (column != "*") // Wildcard is allowed
                {
                    var columnResult = ValidateIdentifier(column, "column name");
                    if (!columnResult.IsSafe)
                    {
                        return columnResult;
                    }
                }
            }
        }

        // Validate WHERE clause
        if (grammar.Where != null)
        {
            var whereResult = ValidateWhereExpression(grammar.Where);
            if (!whereResult.IsSafe)
            {
                return whereResult;
            }
        }

        // Validate ORDER BY columns
        if (grammar.OrderBy?.Count > 0)
        {
            foreach (var orderBy in grammar.OrderBy)
            {
                var columnResult = ValidateIdentifier(orderBy.Column, "order by column");
                if (!columnResult.IsSafe)
                {
                    return columnResult;
                }
            }
        }

        return SqlInjectionValidationResult.Safe();
    }

    /// <summary>
    /// Validates an identifier (table name, column name) for injection threats
    /// </summary>
    public SqlInjectionValidationResult ValidateIdentifier(string identifier, string context = "identifier")
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return SqlInjectionValidationResult.Unsafe($"Empty {context}", SqlInjectionThreatLevel.Medium);
        }

        // Check length
        if (identifier.Length > _config.MaxIdentifierLength)
        {
            return SqlInjectionValidationResult.Unsafe(
                $"{context} '{identifier}' exceeds maximum length of {_config.MaxIdentifierLength}",
                SqlInjectionThreatLevel.Medium,
                $"Shorten the {context} to under {_config.MaxIdentifierLength} characters");
        }

        // Check for dangerous patterns
        var patternResult = CheckForDangerousPatterns(identifier);
        if (!patternResult.IsSafe)
        {
            return SqlInjectionValidationResult.Unsafe(
                $"{context} '{identifier}' contains suspicious pattern: {patternResult.Threat}",
                patternResult.ThreatLevel,
                $"Remove suspicious content from {context}");
        }

        // Check for SQL keywords (if not allowed)
        if (!_config.AllowSqlKeywordsInValues && _sqlKeywords.Contains(identifier))
        {
            return SqlInjectionValidationResult.Unsafe(
                $"{context} '{identifier}' is a SQL keyword",
                SqlInjectionThreatLevel.Medium,
                $"Use a different {context} that is not a SQL keyword");
        }

        return SqlInjectionValidationResult.Safe();
    }

    /// <summary>
    /// Validates a value (from WHERE conditions) for injection threats
    /// </summary>
    public SqlInjectionValidationResult ValidateValue(object? value)
    {
        if (value == null)
        {
            return SqlInjectionValidationResult.Safe(); // NULL values are safe
        }

        var stringValue = value.ToString() ?? "";
        
        // Check length for strings
        if (stringValue.Length > _config.MaxStringLength)
        {
            return SqlInjectionValidationResult.Unsafe(
                $"Value exceeds maximum length of {_config.MaxStringLength}",
                SqlInjectionThreatLevel.Medium,
                $"Shorten the value to under {_config.MaxStringLength} characters");
        }

        // Check for dangerous patterns
        var patternResult = CheckForDangerousPatterns(stringValue);
        if (!patternResult.IsSafe)
        {
            return SqlInjectionValidationResult.Unsafe(
                $"Value contains suspicious pattern: {patternResult.Threat}",
                patternResult.ThreatLevel,
                "Remove suspicious content from the value");
        }

        return SqlInjectionValidationResult.Safe();
    }

    private SqlInjectionValidationResult ValidateWhereExpression(IWhereExpression whereExpression)
    {
        return whereExpression switch
        {
            ComparisonExpression comp => ValidateComparisonExpression(comp),
            AndExpression and => ValidateLogicalExpressions(and.Expressions),
            OrExpression or => ValidateLogicalExpressions(or.Expressions),
            NotExpression not => ValidateWhereExpression(not.Expression),
            _ => SqlInjectionValidationResult.Unsafe(
                $"Unsupported WHERE expression type: {whereExpression.GetType().Name}",
                SqlInjectionThreatLevel.Medium)
        };
    }

    private SqlInjectionValidationResult ValidateComparisonExpression(ComparisonExpression comp)
    {
        // Validate column name
        var columnResult = ValidateIdentifier(comp.Column, "comparison column");
        if (!columnResult.IsSafe)
        {
            return columnResult;
        }

        // Validate value
        var valueResult = ValidateValue(comp.Value);
        if (!valueResult.IsSafe)
        {
            return valueResult;
        }

        return SqlInjectionValidationResult.Safe();
    }

    private SqlInjectionValidationResult ValidateLogicalExpressions(IReadOnlyList<IWhereExpression> expressions)
    {
        foreach (var expression in expressions)
        {
            var result = ValidateWhereExpression(expression);
            if (!result.IsSafe)
            {
                return result;
            }
        }

        return SqlInjectionValidationResult.Safe();
    }

    private SqlInjectionValidationResult CheckForDangerousPatterns(string input)
    {
        // Check for dangerous keywords first
        foreach (var keyword in _dangerousKeywords)
        {
            if (input.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return SqlInjectionValidationResult.Unsafe(
                    $"Dangerous keyword '{keyword}' detected",
                    SqlInjectionThreatLevel.Critical);
            }
        }

        // Check for comment patterns (if not allowed)
        if (!_config.AllowComments)
        {
            if (input.Contains("--") || input.Contains("/*") || input.Contains("*/"))
            {
                return SqlInjectionValidationResult.Unsafe(
                    "SQL comments detected",
                    SqlInjectionThreatLevel.High);
            }
        }

        // Check against injection patterns
        foreach (var pattern in _injectionPatterns)
        {
            var match = pattern.Match(input);
            if (match.Success)
            {
                var threatLevel = DetermineThreatLevel(match.Value);
                return SqlInjectionValidationResult.Unsafe(
                    $"Suspicious pattern detected: {match.Value.Trim()}",
                    threatLevel);
            }
        }

        // Check against custom patterns
        foreach (var pattern in _customPatterns)
        {
            var match = pattern.Match(input);
            if (match.Success)
            {
                return SqlInjectionValidationResult.Unsafe(
                    $"Custom denied pattern detected: {match.Value.Trim()}",
                    SqlInjectionThreatLevel.Medium);
            }
        }

        return SqlInjectionValidationResult.Safe();
    }

    private SqlInjectionThreatLevel DetermineThreatLevel(string suspiciousContent)
    {
        var content = suspiciousContent.ToLowerInvariant();
        
        // Critical threats
        if (content.Contains("drop") || content.Contains("delete") || content.Contains("truncate") ||
            content.Contains("union select") || content.Contains("xp_") || content.Contains("sp_"))
        {
            return SqlInjectionThreatLevel.Critical;
        }
        
        // High threats
        if (content.Contains("union") || content.Contains("--") || content.Contains("/*") ||
            content.Contains("waitfor") || content.Contains("sleep"))
        {
            return SqlInjectionThreatLevel.High;
        }
        
        // Medium threats
        if (content.Contains("or 1=1") || content.Contains("and 1=1") || 
            content.Contains("'='") || content.Contains("0x") ||
            content.Contains("'or'") || content.Contains("select"))
        {
            return SqlInjectionThreatLevel.Medium;
        }

        return SqlInjectionThreatLevel.Low;
    }
}