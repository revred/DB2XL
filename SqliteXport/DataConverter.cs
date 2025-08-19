using Microsoft.Data.Sqlite;
using System.Globalization;
using DB2XL.Configuration;
using DB2XL.Transformers;

namespace DB2XL;

internal static class DataConverter
{
    internal static (string Value, bool AsText) ReadValueAsText(
        SqliteDataReader reader, 
        int columnIndex, 
        SqliteToExcelOptions options,
        string tableName,
        string columnName,
        int rowIndex,
        TransformationPipeline? transformationPipeline = null)
    {
        // Get the raw value first
        string? rawValue = null;
        bool wasNull = reader.IsDBNull(columnIndex);
        bool asText = true;
        
        if (!wasNull)
        {
            var fieldType = reader.GetFieldType(columnIndex);
            var value = reader.GetValue(columnIndex);

            var (convertedValue, isText) = fieldType switch
            {
                Type t when t == typeof(string) => (value.ToString()!, true),
                Type t when t == typeof(long) => FormatNumeric(((long)value).ToString(options.InvariantCulture), options),
                Type t when t == typeof(double) => FormatNumeric(((double)value).ToString(options.InvariantCulture), options),
                Type t when t == typeof(decimal) => FormatNumeric(((decimal)value).ToString(options.InvariantCulture), options),
                Type t when t == typeof(byte[]) => FormatBlob((byte[])value, options),
                _ => (value.ToString()!, true)
            };
            
            rawValue = convertedValue;
            asText = isText;
        }
        else
        {
            rawValue = null; // Explicitly null for transformers
        }

        // Apply transformations if pipeline is available
        if (transformationPipeline != null && transformationPipeline.AreTransformationsEnabled)
        {
            // Check if column is excluded from processing
            if (!transformationPipeline.IsColumnExcluded(tableName, columnName))
            {
                // Get SQLite affinity for context
                var affinity = GetSqliteAffinity(reader, columnIndex);
                var context = new CellContext(tableName, columnName, rowIndex, affinity);
                
                try
                {
                    var transformedValue = transformationPipeline.TransformCell(tableName, columnName, rawValue, context);
                    if (transformedValue != rawValue) // Only update if transformation occurred
                    {
                        rawValue = transformedValue;
                        asText = true; // Transformed values are typically text
                    }
                }
                catch (Exception ex)
                {
                    // Log transformation error but continue with original value
                    // The pipeline handles error counting internally
                    System.Diagnostics.Debug.WriteLine($"Transformation error for {tableName}.{columnName}: {ex.Message}");
                }
            }
        }

        return (rawValue ?? string.Empty, asText);
    }
    
    private static SqliteAffinity GetSqliteAffinity(SqliteDataReader reader, int columnIndex)
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
            _ => SqliteAffinity.Text
        };
    }

    private static (string Value, bool AsText) FormatNumeric(string stringValue, SqliteToExcelOptions options)
    {
        if (options.WriteAllAsText)
        {
            return (stringValue, true);
        }

        return (stringValue, false);
    }

    private static (string Value, bool AsText) FormatBlob(byte[] blobValue, SqliteToExcelOptions options)
    {
        return options.BlobMode switch
        {
            BlobRenderMode.Skip => (string.Empty, true),
            BlobRenderMode.Hex => (Convert.ToHexString(blobValue), true),
            BlobRenderMode.Base64 => (Convert.ToBase64String(blobValue), true),
            _ => throw new ArgumentOutOfRangeException(nameof(options.BlobMode))
        };
    }

    internal static string ToInvariantString(object? value, CultureInfo culture)
    {
        return value switch
        {
            null => string.Empty,
            string s => s,
            long l => l.ToString(culture),
            double d => d.ToString(culture),
            decimal dec => dec.ToString(culture),
            float f => f.ToString(culture),
            int i => i.ToString(culture),
            bool b => b.ToString(culture),
            _ => value.ToString() ?? string.Empty
        };
    }
}