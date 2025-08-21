using System.Data;
using Microsoft.Data.Sqlite;

namespace DB2XL.Transform.Interfaces;

/// <summary>
/// Utility class for converting SQLite types to transformer-friendly enums
/// </summary>
public static class SqliteTypeHelper
{
    /// <summary>
    /// Converts a SQLite field type to our SqliteAffinity enum
    /// </summary>
    /// <param name="reader">SQLite data reader</param>
    /// <param name="columnIndex">Column index</param>
    /// <returns>Corresponding SqliteAffinity</returns>
    public static SqliteAffinity GetSqliteType(SqliteDataReader reader, int columnIndex)
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
    /// Converts a SQLite column type string to our SqliteAffinity enum
    /// </summary>
    /// <param name="columnType">SQLite column type string from schema</param>
    /// <returns>Corresponding SqliteAffinity</returns>
    public static SqliteAffinity ParseColumnType(string columnType)
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
    /// <param name="type">SqliteAffinity enum value</param>
    /// <returns>String representation</returns>
    public static string ToString(SqliteAffinity type)
    {
        return type switch
        {
            SqliteAffinity.Integer => "INTEGER",
            SqliteAffinity.Real => "REAL",
            SqliteAffinity.Text => "TEXT",
            SqliteAffinity.Blob => "BLOB",
            SqliteAffinity.Null => "NULL",
            _ => "UNKNOWN"
        };
    }
}