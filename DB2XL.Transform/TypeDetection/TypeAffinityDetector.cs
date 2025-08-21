using Microsoft.Data.Sqlite;
using DB2XL.Transform.Interfaces;

namespace DB2XL.Transform.TypeDetection;

/// <summary>
/// Utility class for detecting SQLite type affinity for transformation context
/// </summary>
public static class TypeAffinityDetector
{
    /// <summary>
    /// Converts a SQLite field type to SqliteAffinity enum
    /// </summary>
    /// <param name="reader">SQLite data reader</param>
    /// <param name="columnIndex">Column index</param>
    /// <returns>Corresponding SqliteAffinity</returns>
    public static SqliteAffinity GetSqliteAffinity(SqliteDataReader reader, int columnIndex)
    {
        if (reader.IsDBNull(columnIndex))
            return SqliteAffinity.Null;

        var fieldType = reader.GetFieldType(columnIndex);
        
        return fieldType switch
        {
            Type t when t == typeof(long) => SqliteAffinity.Integer,
            Type t when t == typeof(double) => SqliteAffinity.Real,
            Type t when t == typeof(string) => SqliteAffinity.Text,
            Type t when t == typeof(byte[]) => SqliteAffinity.Blob,
            _ => SqliteAffinity.Text // Default fallback
        };
    }

    /// <summary>
    /// Converts a SQLite column type string to SqliteAffinity enum
    /// </summary>
    /// <param name="columnType">SQLite column type string from schema</param>
    /// <returns>Corresponding SqliteAffinity</returns>
    public static SqliteAffinity ParseColumnType(string? columnType)
    {
        if (columnType == null)
            return SqliteAffinity.Text; // null defaults to TEXT
        if (columnType == "")
            return SqliteAffinity.Blob; // Empty string defaults to BLOB

        var upperType = columnType.ToUpperInvariant();
        
        // SQLite type affinity rules
        if (upperType.Contains("INT"))
            return SqliteAffinity.Integer;
        
        if (upperType.Contains("CHAR") || upperType.Contains("CLOB") || upperType.Contains("TEXT"))
            return SqliteAffinity.Text;
        
        if (upperType.Contains("BLOB") || upperType == "")
            return SqliteAffinity.Blob;
        
        if (upperType.Contains("REAL") || upperType.Contains("FLOA") || upperType.Contains("DOUB") || upperType.Contains("NUMERIC") || upperType.Contains("DECIMAL"))
            return SqliteAffinity.Real;
        
        // Default fallback - SQLite's default affinity for unrecognized types
        return SqliteAffinity.Text;
    }

    /// <summary>
    /// Gets a friendly string representation of SqliteAffinity
    /// </summary>
    /// <param name="affinity">SqliteAffinity enum value</param>
    /// <returns>String representation</returns>
    public static string AffinityToString(SqliteAffinity affinity)
    {
        return affinity switch
        {
            SqliteAffinity.Integer => "INTEGER",
            SqliteAffinity.Real => "REAL",
            SqliteAffinity.Text => "TEXT",
            SqliteAffinity.Blob => "BLOB",
            SqliteAffinity.Null => "NULL",
            _ => "UNKNOWN"
        };
    }

    /// <summary>
    /// Checks if a column name matches common patterns for specific data types
    /// </summary>
    /// <param name="columnName">Name of the column</param>
    /// <param name="pattern">Pattern to match (case-insensitive)</param>
    /// <returns>True if the column name matches the pattern</returns>
    public static bool ColumnNameMatches(string columnName, string pattern)
    {
        return columnName.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Detects if a column is likely to contain timestamp data based on name patterns
    /// </summary>
    /// <param name="columnName">Name of the column</param>
    /// <returns>True if the column name suggests timestamp data</returns>
    public static bool IsLikelyTimestampColumn(string columnName)
    {
        var timestampPatterns = new[]
        {
            "timestamp", "created", "updated", "modified", "time", 
            "date", "_at", "when", "occurred", "logged"
        };

        return timestampPatterns.Any(pattern => ColumnNameMatches(columnName, pattern));
    }

    /// <summary>
    /// Detects if a column is likely to contain JSON data based on name patterns
    /// </summary>
    /// <param name="columnName">Name of the column</param>
    /// <returns>True if the column name suggests JSON data</returns>
    public static bool IsLikelyJsonColumn(string columnName)
    {
        var jsonPatterns = new[]
        {
            "json", "data", "metadata", "payload", "config", 
            "settings", "properties", "attributes"
        };

        return jsonPatterns.Any(pattern => ColumnNameMatches(columnName, pattern));
    }
}