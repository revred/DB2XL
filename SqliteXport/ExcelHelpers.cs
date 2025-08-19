using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace DB2XL;

internal static class ExcelHelpers
{
    private static readonly char[] InvalidSheetNameChars = { ':', '\\', '/', '?', '*', '[', ']' };
    private static readonly Regex InvalidCharsRegex = new(@"[:\\/\?\*\[\]]", RegexOptions.Compiled);

    internal static string SanitizeSheetName(string name, HashSet<string>? usedNames = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sheet name cannot be null or whitespace.", nameof(name));

        var sanitized = InvalidCharsRegex.Replace(name.Trim(), "_");
        
        if (sanitized.Length > 31)
            sanitized = sanitized[..31];

        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "Sheet";

        usedNames ??= new HashSet<string>();
        
        if (!usedNames.Contains(sanitized))
        {
            usedNames.Add(sanitized);
            return sanitized;
        }

        for (int i = 1; i <= 9999; i++)
        {
            var suffix = $"~{i}";
            var candidate = sanitized.Length + suffix.Length > 31 
                ? sanitized[..(31 - suffix.Length)] + suffix 
                : sanitized + suffix;

            if (!usedNames.Contains(candidate))
            {
                usedNames.Add(candidate);
                return candidate;
            }
        }

        throw new InvalidOperationException($"Unable to generate unique sheet name for '{name}' after 9999 attempts.");
    }

    internal static string CreateSheetName(string baseName, int partNumber, HashSet<string>? usedNames = null)
    {
        if (partNumber == 1)
            return SanitizeSheetName(baseName, usedNames);

        var nameWithPart = $"{baseName}_p{partNumber}";
        return SanitizeSheetName(nameWithPart, usedNames);
    }

    internal static (IXLWorksheet Worksheet, ChecksumBuilder ChecksumBuilder) NewSheet(
        XLWorkbook workbook, 
        string baseSheetName, 
        int partNumber, 
        IReadOnlyList<Col> columns, 
        SqliteToExcelOptions options,
        HashSet<string>? usedNames = null)
    {
        var sheetName = CreateSheetName(baseSheetName, partNumber, usedNames);
        var worksheet = workbook.Worksheets.Add(sheetName);
        var checksumBuilder = new ChecksumBuilder();

        for (int i = 0; i < columns.Count; i++)
        {
            var headerCell = worksheet.Cell(1, i + 1);
            headerCell.Value = columns[i].Name;
            headerCell.Style.Font.Bold = true;
            headerCell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        return (worksheet, checksumBuilder);
    }

    internal static (IXLWorksheet Worksheet, ChecksumBuilder ChecksumBuilder) NewSheetWithSuffix(
        XLWorkbook workbook, 
        string baseSheetName, 
        int partNumber, 
        IReadOnlyList<Col> columns, 
        SqliteToExcelOptions options,
        HashSet<string>? usedNames = null,
        string suffix = "")
    {
        var nameWithSuffix = $"{baseSheetName}{suffix}";
        var sheetName = CreateSheetName(nameWithSuffix, partNumber, usedNames);
        var worksheet = workbook.Worksheets.Add(sheetName);
        var checksumBuilder = new ChecksumBuilder();

        for (int i = 0; i < columns.Count; i++)
        {
            var headerCell = worksheet.Cell(1, i + 1);
            headerCell.Value = columns[i].Name;
            headerCell.Style.Font.Bold = true;
            headerCell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        return (worksheet, checksumBuilder);
    }
}