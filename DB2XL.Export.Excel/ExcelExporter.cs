using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using DB2XL.Core.Interfaces;
using DB2XL.Core.Models;
using DB2XL.Core.Enums;
using DB2XL.Core.Exceptions;
using DB2XL.Data.Schema;
using DB2XL.Data.Query;
using DB2XL.Data.Checksum;
using DB2XL.Query;
using System.Diagnostics;

namespace DB2XL.Export.Excel;

/// <summary>
/// Excel implementation of the data exporter
/// </summary>
public class ExcelExporter : IExporter
{
    /// <summary>
    /// Exports data from a SQLite database to Excel format
    /// </summary>
    public async Task<ExportResult> ExportAsync(string sourcePath, string outputPath, IExportOptions options)
    {
        var excelOptions = options as ExcelExportOptions ?? throw new ArgumentException("Options must be ExcelExportOptions", nameof(options));
        
        var stopwatch = Stopwatch.StartNew();
        var validationResult = ValidateExport(sourcePath, options);
        
        if (!validationResult.IsValid)
        {
            return new ExportResult
            {
                Success = false,
                OutputPath = outputPath,
                ErrorMessage = string.Join("; ", validationResult.Errors),
                Duration = stopwatch.Elapsed
            };
        }

        try
        {
            await Task.Run(() => ExportSync(sourcePath, outputPath, excelOptions));
            stopwatch.Stop();
            
            var fileInfo = new FileInfo(outputPath);
            
            return new ExportResult
            {
                Success = true,
                OutputPath = outputPath,
                Duration = stopwatch.Elapsed,
                OutputSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
                Warnings = validationResult.Warnings
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ExportResult
            {
                Success = false,
                OutputPath = outputPath,
                ErrorMessage = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// Validates that the export can be performed with the given options
    /// </summary>
    public ValidationResult ValidateExport(string sourcePath, IExportOptions options)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var tablesFound = new List<string>();

        // Validate source file
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            errors.Add("Source path cannot be null or empty");
        }
        else if (!File.Exists(sourcePath))
        {
            errors.Add($"Source file not found: {sourcePath}");
        }

        // Try to connect and get tables
        if (errors.Count == 0)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly;");
                connection.Open();
                
                var tables = SqliteSchemaReader.GetDatabaseObjects(connection, options.TableNameFilter, options.IncludeViews);
                tablesFound.AddRange(tables.Select(t => t.Name));
                
                if (tables.Count == 0)
                {
                    warnings.Add("No tables found matching the specified criteria");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Cannot open SQLite database: {ex.Message}");
            }
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings,
            TablesFound = tablesFound
        };
    }

    private void ExportSync(string sourcePath, string outputPath, ExcelExportOptions options)
    {
        // Create output directory if it doesn't exist
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        using var connection = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly;Cache=Shared;Pooling=True;");
        connection.Open();

        // Disable foreign keys for read-only access
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = OFF;";
            command.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        
        try
        {
            var tables = SqliteSchemaReader.GetDatabaseObjects(connection, options.TableNameFilter, options.IncludeViews);
            
            switch (options.DualExportStrategy)
            {
                case DualExportStrategy.TransformedOnly:
                case DualExportStrategy.RawOnly:
                    ExportSingleWorkbook(connection, outputPath, tables, options);
                    break;
                    
                case DualExportStrategy.DualSheets:
                    ExportDualSheets(connection, outputPath, tables, options);
                    break;
                    
                case DualExportStrategy.DualWorkbooks:
                    ExportDualWorkbooks(connection, outputPath, tables, options);
                    break;
                    
                default:
                    throw new ExportException($"Unsupported dual export strategy: {options.DualExportStrategy}");
            }
            
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private void ExportSingleWorkbook(SqliteConnection connection, string outputPath, List<TableInfo> tables, ExcelExportOptions options)
    {
        using var workbook = new XLWorkbook();
        var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var metadataRows = options.IncludeMetadataSheet ? new List<TableExportResult>() : null;

        foreach (var table in tables)
        {
            ExportTable(connection, workbook, table, options, usedSheetNames, metadataRows);
        }

        if (metadataRows != null)
        {
            WriteMetadataSheet(workbook, options, metadataRows, connection, usedSheetNames);
        }

        workbook.SaveAs(outputPath);
    }

    private void ExportDualSheets(SqliteConnection connection, string outputPath, List<TableInfo> tables, ExcelExportOptions options)
    {
        using var workbook = new XLWorkbook();
        var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var metadataRows = options.IncludeMetadataSheet ? new List<TableExportResult>() : null;

        foreach (var table in tables)
        {
            // Export raw data
            ExportTableWithSuffix(connection, workbook, table, options, usedSheetNames, metadataRows, options.RawDataSuffix, useTransformations: false);
            
            // Export transformed data
            ExportTableWithSuffix(connection, workbook, table, options, usedSheetNames, metadataRows, options.TransformedDataSuffix, useTransformations: true);
        }

        if (metadataRows != null)
        {
            WriteMetadataSheet(workbook, options, metadataRows, connection, usedSheetNames);
        }

        workbook.SaveAs(outputPath);
    }

    private void ExportDualWorkbooks(SqliteConnection connection, string outputPath, List<TableInfo> tables, ExcelExportOptions options)
    {
        // Export raw data to the specified path
        var rawOptions = options with { DualExportStrategy = DualExportStrategy.RawOnly };
        ExportSingleWorkbook(connection, outputPath, tables, rawOptions);
        
        // Export transformed data to a separate workbook
        var transformedPath = GetTransformedWorkbookPath(outputPath);
        var transformedOptions = options with { DualExportStrategy = DualExportStrategy.TransformedOnly };
        ExportSingleWorkbook(connection, transformedPath, tables, transformedOptions);
    }

    private static string GetTransformedWorkbookPath(string originalPath)
    {
        var directory = Path.GetDirectoryName(originalPath) ?? "";
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalPath);
        var extension = Path.GetExtension(originalPath);
        
        return Path.Combine(directory, $"{fileNameWithoutExtension}_Transformed{extension}");
    }

    private void ExportTable(SqliteConnection connection, XLWorkbook workbook, TableInfo table, ExcelExportOptions options, HashSet<string> usedSheetNames, List<TableExportResult>? metadataRows)
    {
        var allColumns = SqliteSchemaReader.GetTableColumns(connection, table.Name);
        List<ColumnInfo> columnsToExport;
        string sql;
        
        // Check if we have SelectionGrammar for this specific table
        if (options.SelectionGrammar != null && 
            options.SelectionGrammar.Table.Equals(table.Name, StringComparison.OrdinalIgnoreCase))
        {
            // Use SelectionGrammar for advanced filtering
            var sqlBuilder = new SqlBuilder();
            var result = sqlBuilder.BuildQuery(options.SelectionGrammar);
            
            // For now, validate that the query doesn't require parameters
            if (result.Parameters.Any())
            {
                throw new InvalidOperationException(
                    "SelectionGrammar queries with parameters are not yet supported in this context. " +
                    "Please use literal values in WHERE clauses for now.");
            }
            
            sql = result.Sql;
            
            // Determine which columns will be returned by the query
            if (options.SelectionGrammar.Select.Contains("*"))
            {
                columnsToExport = allColumns;
            }
            else
            {
                // Map selected column names to ColumnInfo objects
                columnsToExport = new List<ColumnInfo>();
                foreach (var selectedColumn in options.SelectionGrammar.Select)
                {
                    var columnInfo = allColumns.FirstOrDefault(c => c.Name.Equals(selectedColumn, StringComparison.OrdinalIgnoreCase));
                    if (columnInfo != null)
                    {
                        columnsToExport.Add(columnInfo);
                    }
                    else
                    {
                        throw new InvalidOperationException($"Column '{selectedColumn}' specified in SelectionGrammar does not exist in table '{table.Name}'");
                    }
                }
            }
        }
        else
        {
            // Use traditional approach
            columnsToExport = allColumns;
            var orderInfo = SqliteSchemaReader.DetermineTableOrdering(connection, table.Name, allColumns);
            sql = SqlQueryBuilder.BuildSelectQuery(table.Name, allColumns, orderInfo, options.OrderRowsDeterministically);
        }
        
        if (columnsToExport.Count == 0)
        {
            return; // Skip tables with no columns
        }
        
        if (columnsToExport.Count > 16384)
        {
            throw new ExportException($"Table {table.Name} has {columnsToExport.Count} columns, exceeding Excel's limit of 16,384", table.Name);
        }

        var sheetName = SanitizeSheetName(table.Name, usedSheetNames);
        var worksheet = workbook.Worksheets.Add(sheetName);
        
        // Write headers
        for (int i = 0; i < columnsToExport.Count; i++)
        {
            worksheet.Cell(1, i + 1).Value = columnsToExport[i].Name;
            worksheet.Cell(1, i + 1).Style.Font.Bold = true;
        }

        int rowCount = 0;
        using var checksumCalculator = new DataChecksumCalculator();
        
        using var command = connection.CreateCommand();
        command.CommandTimeout = options.CommandTimeoutSeconds;
        command.CommandText = sql;
        
        using var reader = command.ExecuteReader(System.Data.CommandBehavior.SequentialAccess);
        
        int excelRow = 2; // Start after header
        while (reader.Read())
        {
            for (int i = 0; i < columnsToExport.Count; i++)
            {
                var value = ReadCellValue(reader, i, options);
                worksheet.Cell(excelRow, i + 1).Value = value ?? string.Empty;
                checksumCalculator.AddField(value);
            }
            checksumCalculator.EndRow();
            rowCount++;
            excelRow++;
            
            // Check Excel row limit
            if (excelRow > 1048576)
            {
                if (!options.SplitOversizeSheets)
                {
                    throw new ExportException($"Table {table.Name} exceeds Excel's row limit of 1,048,576 rows", table.Name);
                }
                // TODO: Implement sheet splitting
                break;
            }
        }

        metadataRows?.Add(new TableExportResult
        {
            TableName = table.Name,
            RowCount = rowCount,
            ColumnCount = columnsToExport.Count,
            Checksum = checksumCalculator.GetChecksum(),
            WasSplit = false,
            SplitParts = 1
        });
    }

    private void ExportTableWithSuffix(SqliteConnection connection, XLWorkbook workbook, TableInfo table, ExcelExportOptions options, HashSet<string> usedSheetNames, List<TableExportResult>? metadataRows, string suffix, bool useTransformations)
    {
        // For now, simplified implementation without transformations
        // TODO: Integrate with transformation pipeline
        ExportTable(connection, workbook, table, options, usedSheetNames, metadataRows);
    }

    private static string? ReadCellValue(SqliteDataReader reader, int columnIndex, ExcelExportOptions options)
    {
        if (reader.IsDBNull(columnIndex))
            return null;

        var fieldType = reader.GetFieldType(columnIndex);
        var value = reader.GetValue(columnIndex);

        return fieldType switch
        {
            Type t when t == typeof(string) => value.ToString(),
            Type t when t == typeof(long) => ((long)value).ToString(options.Culture),
            Type t when t == typeof(double) => ((double)value).ToString(options.Culture),
            Type t when t == typeof(decimal) => ((decimal)value).ToString(options.Culture),
            Type t when t == typeof(byte[]) => FormatBlob((byte[])value, options),
            _ => value.ToString()
        };
    }

    private static string FormatBlob(byte[] blob, ExcelExportOptions options)
    {
        return options.BlobMode switch
        {
            BlobRenderMode.Skip => string.Empty,
            BlobRenderMode.Hex => Convert.ToHexString(blob),
            BlobRenderMode.Base64 => Convert.ToBase64String(blob),
            _ => string.Empty
        };
    }

    private static string SanitizeSheetName(string tableName, HashSet<string> usedNames)
    {
        // Excel sheet name constraints: max 31 chars, no special characters
        var sanitized = tableName;
        var invalidChars = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        
        foreach (var ch in invalidChars)
        {
            sanitized = sanitized.Replace(ch, '_');
        }
        
        if (sanitized.Length > 31)
        {
            sanitized = sanitized.Substring(0, 31);
        }
        
        // Ensure uniqueness
        var originalSanitized = sanitized;
        int counter = 1;
        while (usedNames.Contains(sanitized))
        {
            var suffix = $"_{counter}";
            if (originalSanitized.Length + suffix.Length > 31)
            {
                sanitized = originalSanitized.Substring(0, 31 - suffix.Length) + suffix;
            }
            else
            {
                sanitized = originalSanitized + suffix;
            }
            counter++;
        }
        
        usedNames.Add(sanitized);
        return sanitized;
    }

    private static void WriteMetadataSheet(XLWorkbook workbook, ExcelExportOptions options, List<TableExportResult> metadataRows, SqliteConnection connection, HashSet<string> usedSheetNames)
    {
        var sheetName = SanitizeSheetName(options.MetadataSheetName, usedSheetNames);
        var metaSheet = workbook.Worksheets.Add(sheetName);

        int row = 1;
        
        // Title
        metaSheet.Cell(row, 1).Value = "Export Metadata";
        metaSheet.Cell(row, 1).Style.Font.Bold = true;
        metaSheet.Cell(row, 1).Style.Font.FontSize = 14;
        row += 2;

        // Export timestamp
        metaSheet.Cell(row, 1).Value = "Export Timestamp (UTC):";
        metaSheet.Cell(row, 2).Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        row += 2;

        // Table summary headers
        metaSheet.Cell(row, 1).Value = "Table Export Summary";
        metaSheet.Cell(row, 1).Style.Font.Bold = true;
        row++;

        var headerRow = row;
        metaSheet.Cell(row, 1).Value = "Table Name";
        metaSheet.Cell(row, 2).Value = "Row Count";
        metaSheet.Cell(row, 3).Value = "Column Count";
        metaSheet.Cell(row, 4).Value = "SHA256 Checksum";

        for (int col = 1; col <= 4; col++)
        {
            metaSheet.Cell(headerRow, col).Style.Font.Bold = true;
            metaSheet.Cell(headerRow, col).Style.Fill.BackgroundColor = XLColor.LightGray;
        }
        row++;

        // Table data
        foreach (var meta in metadataRows)
        {
            metaSheet.Cell(row, 1).Value = meta.TableName;
            metaSheet.Cell(row, 2).Value = meta.RowCount;
            metaSheet.Cell(row, 3).Value = meta.ColumnCount;
            metaSheet.Cell(row, 4).Value = meta.Checksum;
            row++;
        }

        metaSheet.Columns().AdjustToContents();
    }
}