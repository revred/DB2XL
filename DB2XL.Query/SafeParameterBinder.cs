using Microsoft.Data.Sqlite;

namespace DB2XL.Query;

/// <summary>
/// Provides safe parameter binding for SQLite queries to prevent SQL injection
/// </summary>
public static class SafeParameterBinder
{
    /// <summary>
    /// Validates that all parameters are safe for binding
    /// </summary>
    /// <param name="parameters">Parameters to validate</param>
    /// <param name="sql">SQL query string</param>
    /// <returns>Validation result</returns>
    public static ParameterValidationResult ValidateParameters(Dictionary<string, object?> parameters, string sql)
    {
        var errors = new List<string>();
        
        // Check that all SQL parameters have corresponding dictionary entries
        var sqlParams = ExtractParameterNames(sql);
        var missingParams = sqlParams.Where(p => !parameters.ContainsKey(p)).ToList();
        
        if (missingParams.Any())
        {
            errors.Add($"Missing parameters: {string.Join(", ", missingParams)}");
        }
        
        // Check for potentially dangerous parameter names
        foreach (var param in parameters.Keys)
        {
            if (ContainsSqlKeywords(param))
            {
                errors.Add($"Parameter name contains SQL keywords: {param}");
            }
            
            if (param.Contains("'") || param.Contains("\"") || param.Contains(";"))
            {
                errors.Add($"Parameter name contains dangerous characters: {param}");
            }
        }
        
        // Validate parameter values
        foreach (var kvp in parameters)
        {
            var validationError = ValidateParameterValue(kvp.Key, kvp.Value);
            if (validationError != null)
            {
                errors.Add(validationError);
            }
        }
        
        return new ParameterValidationResult(errors.Count == 0, errors);
    }
    
    /// <summary>
    /// Safely binds parameters to a SQLite command
    /// </summary>
    /// <param name="command">SQLite command</param>
    /// <param name="parameters">Parameters to bind</param>
    public static void BindParameters(SqliteCommand command, Dictionary<string, object?> parameters)
    {
        var validation = ValidateParameters(parameters, command.CommandText);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Parameter validation failed: {string.Join("; ", validation.Errors)}");
        }
        
        command.Parameters.Clear();
        
        foreach (var kvp in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@{kvp.Key}";
            parameter.Value = kvp.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
    
    /// <summary>
    /// Extracts parameter names from SQL query string
    /// </summary>
    private static HashSet<string> ExtractParameterNames(string sql)
    {
        var parameters = new HashSet<string>();
        var i = 0;
        
        while (i < sql.Length)
        {
            if (sql[i] == '@')
            {
                i++; // Skip @
                var start = i;
                
                // Read parameter name (alphanumeric + underscore)
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_'))
                {
                    i++;
                }
                
                if (i > start)
                {
                    parameters.Add(sql.Substring(start, i - start));
                }
            }
            else
            {
                i++;
            }
        }
        
        return parameters;
    }
    
    /// <summary>
    /// Checks if parameter name contains SQL keywords
    /// </summary>
    private static bool ContainsSqlKeywords(string paramName)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "DROP", "CREATE", "ALTER",
            "FROM", "WHERE", "JOIN", "UNION", "ORDER", "GROUP", "HAVING"
        };
        
        return keywords.Any(keyword => paramName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// Validates a single parameter value
    /// </summary>
    private static string? ValidateParameterValue(string name, object? value)
    {
        if (value == null)
        {
            return null; // NULL values are always safe
        }
        
        // Check for extremely large values that could cause DoS
        if (value is string str && str.Length > 100_000)
        {
            return $"Parameter {name} exceeds maximum string length (100,000 characters)";
        }
        
        if (value is byte[] bytes && bytes.Length > 10_000_000) // 10MB
        {
            return $"Parameter {name} exceeds maximum binary length (10MB)";
        }
        
        // Validate supported parameter types
        var allowedTypes = new[]
        {
            typeof(string), typeof(int), typeof(long), typeof(double), typeof(float),
            typeof(bool), typeof(DateTime), typeof(DateTimeOffset), typeof(byte[]),
            typeof(decimal), typeof(Guid)
        };
        
        if (!allowedTypes.Contains(value.GetType()))
        {
            return $"Parameter {name} has unsupported type: {value.GetType().Name}";
        }
        
        return null;
    }
}

/// <summary>
/// Result of parameter validation
/// </summary>
public sealed record ParameterValidationResult(bool IsValid, IReadOnlyList<string> Errors);

/// <summary>
/// Extension methods for safe SQL execution
/// </summary>
public static class SqliteCommandExtensions
{
    /// <summary>
    /// Executes a parameterized query safely
    /// </summary>
    public static SqliteDataReader ExecuteReaderSafe(this SqliteCommand command, Dictionary<string, object?> parameters)
    {
        SafeParameterBinder.BindParameters(command, parameters);
        return command.ExecuteReader();
    }
    
    /// <summary>
    /// Executes a parameterized scalar query safely
    /// </summary>
    public static object? ExecuteScalarSafe(this SqliteCommand command, Dictionary<string, object?> parameters)
    {
        SafeParameterBinder.BindParameters(command, parameters);
        return command.ExecuteScalar();
    }
    
    /// <summary>
    /// Executes a parameterized non-query safely
    /// </summary>
    public static int ExecuteNonQuerySafe(this SqliteCommand command, Dictionary<string, object?> parameters)
    {
        SafeParameterBinder.BindParameters(command, parameters);
        return command.ExecuteNonQuery();
    }
}