using Microsoft.Data.Sqlite;
using System.Globalization;

namespace DB2XL;

internal static class DataConverter
{
    internal static (string Value, bool AsText) ReadValueAsText(SqliteDataReader reader, int columnIndex, SqliteToExcelOptions options)
    {
        if (reader.IsDBNull(columnIndex))
        {
            return (string.Empty, true);
        }

        var fieldType = reader.GetFieldType(columnIndex);
        var value = reader.GetValue(columnIndex);

        return fieldType switch
        {
            Type t when t == typeof(string) => (value.ToString()!, true),
            Type t when t == typeof(long) => FormatNumeric(value.ToString()!, options),
            Type t when t == typeof(double) => FormatNumeric(value.ToString()!, options),
            Type t when t == typeof(decimal) => FormatNumeric(value.ToString()!, options),
            Type t when t == typeof(byte[]) => FormatBlob((byte[])value, options),
            _ => (value.ToString()!, true)
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