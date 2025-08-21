using DB2XL.Data.Query;
using DB2XL.Data.Schema;
using System.Globalization;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using DB2XL.Core.Models;
using DB2XL.Core.Enums;
using DB2XL.Data.Checksum;
using DB2XL.Transform.Interfaces;
using DB2XL.Transform.Configuration;
using DB2XL.Query;
using DB2XL.Schema;
using Microsoft.Extensions.Logging;

namespace DB2XL;

public static class SqliteToExcel
{
    public static void Export(string sqlitePath, string xlsxPath, SqliteToExcelOptions? options = null)
    {
        options ??= new SqliteToExcelOptions();

        ValidateInputs(sqlitePath, xlsxPath);

        // Handle different dual export strategies
        switch (options.DualExportStrategy)
        {
            case DualExportStrategy.TransformedOnly:
                ExportSingle(sqlitePath, xlsxPath, options, useTransformations: true);
                break;
                
            case DualExportStrategy.RawOnly:
                ExportSingle(sqlitePath, xlsxPath, options, useTransformations: false);
                break;
                
            case DualExportStrategy.DualSheets:
                ExportDualSheets(sqlitePath, xlsxPath, options);
                break;
                
            case DualExportStrategy.DualWorkbooks:
                ExportDualWorkbooks(sqlitePath, xlsxPath, options);
                break;
                
            default:
                throw new ArgumentOutOfRangeException(nameof(options.DualExportStrategy), 
                    $"Unsupported dual export strategy: {options.DualExportStrategy}");
        }
    }

    private static void ExportSingle(string sqlitePath, string xlsxPath, SqliteToExcelOptions options, bool useTransformations)
    {
        using var connection = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly;Cache=Shared;Pooling=True;");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = OFF;";
        command.ExecuteNonQuery();

        using var transaction = connection.BeginTransaction();

        var tables = GetTablesToExport(connection, options);
        using var workbook = new XLWorkbook();
        var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var metadataRows = options.IncludeMetadataSheet ? new List<MetaRow>() : null;

        // Initialize transformation pipeline if configured and requested
        TransformationPipeline? transformationPipeline = null;
        if (useTransformations && options.TransformationConfig != null)
        {
            var registry = options.TransformerRegistry ?? TransformerRegistryBuilder.CreateDefault();
            transformationPipeline = new TransformationPipeline(options.TransformationConfig, registry);
        }

        foreach (var table in tables)
        {
            ExportTable(connection, workbook, table, options, usedSheetNames, metadataRows, transformationPipeline);
        }

        if (metadataRows != null)
        {
            WriteMetadataSheet(workbook, options, metadataRows, sqlitePath, connection, usedSheetNames, transformationPipeline);
        }

        var outputDir = Path.GetDirectoryName(xlsxPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        workbook.SaveAs(xlsxPath);
        transaction.Commit();
    }

    private static void ExportDualSheets(string sqlitePath, string xlsxPath, SqliteToExcelOptions options)
    {
        using var connection = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly;Cache=Shared;Pooling=True;");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = OFF;";
        command.ExecuteNonQuery();

        using var transaction = connection.BeginTransaction();

        var tables = GetTablesToExport(connection, options);
        using var workbook = new XLWorkbook();
        var usedSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var metadataRows = options.IncludeMetadataSheet ? new List<MetaRow>() : null;

        // Initialize transformation pipeline if configured
        TransformationPipeline? transformationPipeline = null;
        if (options.TransformationConfig != null)
        {
            var registry = options.TransformerRegistry ?? TransformerRegistryBuilder.CreateDefault();
            transformationPipeline = new TransformationPipeline(options.TransformationConfig, registry);
        }

        foreach (var table in tables)
        {
            // Export raw data sheet
            ExportTableWithSuffix(connection, workbook, table, options, usedSheetNames, metadataRows, 
                transformationPipeline: null, sheetSuffix: options.RawDataSuffix);
                
            // Export transformed data sheet (if transformations are configured)
            if (transformationPipeline != null && transformationPipeline.AreTransformationsEnabled)
            {
                ExportTableWithSuffix(connection, workbook, table, options, usedSheetNames, metadataRows, 
                    transformationPipeline, sheetSuffix: options.TransformedDataSuffix);
            }
        }

        if (metadataRows != null)
        {
            WriteMetadataSheet(workbook, options, metadataRows, sqlitePath, connection, usedSheetNames, transformationPipeline);
        }

        var outputDir = Path.GetDirectoryName(xlsxPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        workbook.SaveAs(xlsxPath);
        transaction.Commit();
    }

    private static void ExportDualWorkbooks(string sqlitePath, string xlsxPath, SqliteToExcelOptions options)
    {
        // Export raw data to the specified path
        ExportSingle(sqlitePath, xlsxPath, options, useTransformations: false);
        
        // Export transformed data to a separate workbook with suffix
        if (options.TransformationConfig != null)
        {
            var transformedPath = GetTransformedWorkbookPath(xlsxPath);
            ExportSingle(sqlitePath, transformedPath, options, useTransformations: true);
        }
    }

    private static string GetTransformedWorkbookPath(string originalPath)
    {
        var directory = Path.GetDirectoryName(originalPath) ?? "";
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalPath);
        var extension = Path.GetExtension(originalPath);
        
        return Path.Combine(directory, $"{fileNameWithoutExtension}_Transformed{extension}");
    }

    private static void ExportTable(
        SqliteConnection connection,
        XLWorkbook workbook,
        DB2XL.Core.Models.TableInfo table,
        SqliteToExcelOptions options,
        HashSet<string> usedSheetNames,
        List<MetaRow>? metadataRows,
        TransformationPipeline? transformationPipeline)
    {
        var allColumns = SqliteSchemaReader.GetTableColumns(connection, table.Name);
        
        // Apply security filtering to columns if configured
        if (options.SecurityFilter != null)
        {
            allColumns = ApplyColumnSecurityFiltering(table.Name, allColumns, options.SecurityFilter);
        }
        
        // Determine which columns will actually be returned by the query
        List<DB2XL.Core.Models.ColumnInfo> columnsToExport;
        if (options.SelectionGrammar != null && 
            options.SelectionGrammar.Table.Equals(table.Name, StringComparison.OrdinalIgnoreCase))
        {
            // Use SelectionGrammar to determine columns
            if (options.SelectionGrammar.Select.Contains("*"))
            {
                columnsToExport = allColumns;
            }
            else
            {
                // Map selected column names to ColumnInfo objects
                columnsToExport = new List<DB2XL.Core.Models.ColumnInfo>();
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
            // Use all columns (traditional behavior)
            columnsToExport = allColumns;
        }
        
        if (columnsToExport.Count == 0)
        {
            Console.WriteLine($"Warning: Table '{table.Name}' has no accessible columns after security filtering. Skipping table.");
            return;
        }
        
        if (columnsToExport.Count > 16384)
        {
            throw new InvalidOperationException($"Table {table.Name} has {columnsToExport.Count} columns, exceeding Excel's limit of 16,384.");
        }

        var orderInfo = SqliteSchemaReader.DetermineTableOrdering(connection, table.Name, allColumns);
        var sql = BuildSelectSqlWithGrammar(table.Name, allColumns, orderInfo, options);

        int partNumber = 1;
        int totalRows = 0;
        int rowsInCurrentSheet = 0;
        IXLWorksheet? currentSheet = null;
        DataChecksumCalculator? masterChecksum = new DataChecksumCalculator();

        using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = options.CommandTimeoutSeconds;
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader(System.Data.CommandBehavior.SequentialAccess);
        
        bool hasRows = false;
        while (reader.Read())
        {
            hasRows = true;
            
            // Create sheet on first row or when exceeding row limit
            if (currentSheet == null || rowsInCurrentSheet >= 1048575)
            {
                if (rowsInCurrentSheet >= 1048575 && !options.SplitOversizeSheets)
                {
                    throw new InvalidOperationException(
                        $"Table {table.Name} exceeds Excel's row limit of 1,048,576 rows. " +
                        $"Enable SplitOversizeSheets to split across multiple sheets.");
                }

                var (sheet, checksum) = ExcelHelpers.NewSheet(
                    workbook, table.Name, partNumber, columnsToExport, options, usedSheetNames);
                currentSheet = sheet;
                rowsInCurrentSheet = 0;
                partNumber++;
            }

            rowsInCurrentSheet++;
            totalRows++;
            var excelRow = rowsInCurrentSheet + 1;

            for (int i = 0; i < columnsToExport.Count; i++)
            {
                var columnName = columnsToExport[i].Name;
                var (value, isText) = DataConverter.ReadValueAsText(reader, i, options, table.Name, columnName, totalRows, transformationPipeline);
                var cell = currentSheet.Cell(excelRow, i + 1);

                if (options.WriteAllAsText || isText)
                {
                    cell.Value = value ?? string.Empty;
                }
                else if (!string.IsNullOrEmpty(value))
                {
                    if (options.PreserveNumericTypes && 
                        double.TryParse(value, NumberStyles.Any, options.InvariantCulture, out var numValue))
                    {
                        cell.SetValue(numValue);
                    }
                    else
                    {
                        cell.Value = value;
                    }
                }

                masterChecksum.AddField(value);
            }
            masterChecksum.EndRow();
        }
        
        // Ensure we create at least one sheet even for empty tables/views
        if (!hasRows && currentSheet == null)
        {
            var (sheet, checksum) = ExcelHelpers.NewSheet(
                workbook, table.Name, partNumber, columnsToExport, options, usedSheetNames);
            currentSheet = sheet;
            partNumber++;
        }

        metadataRows?.Add(new MetaRow(
            table.Name,
            table.Type,
            totalRows,
            columnsToExport.Count,
            partNumber - 1,
            orderInfo.Mode,
            masterChecksum.GetChecksum()));
    }

    private static void ExportTableWithSuffix(
        SqliteConnection connection,
        XLWorkbook workbook,
        DB2XL.Core.Models.TableInfo table,
        SqliteToExcelOptions options,
        HashSet<string> usedSheetNames,
        List<MetaRow>? metadataRows,
        TransformationPipeline? transformationPipeline,
        string sheetSuffix)
    {
        var columns = SqliteSchemaReader.GetTableColumns(connection, table.Name);
        
        // Apply security filtering to columns if configured
        if (options.SecurityFilter != null)
        {
            columns = ApplyColumnSecurityFiltering(table.Name, columns, options.SecurityFilter);
        }
        
        if (columns.Count == 0)
        {
            Console.WriteLine($"Warning: Table '{table.Name}' has no accessible columns after security filtering. Skipping table.");
            return;
        }
        
        if (columns.Count > 16384)
        {
            throw new InvalidOperationException($"Table {table.Name} has {columns.Count} columns, exceeding Excel's limit of 16,384.");
        }

        var orderInfo = SqliteSchemaReader.DetermineTableOrdering(connection, table.Name, columns);
        var sql = SqlQueryBuilder.BuildSelectQuery(table.Name, columns, orderInfo, options.OrderRowsDeterministically);

        int partNumber = 1;
        int totalRows = 0;
        int rowsInCurrentSheet = 0;
        IXLWorksheet? currentSheet = null;
        DataChecksumCalculator? masterChecksum = new DataChecksumCalculator();

        using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = options.CommandTimeoutSeconds;
        cmd.CommandText = sql;

        using var reader = cmd.ExecuteReader(System.Data.CommandBehavior.SequentialAccess);
        
        bool hasRows = false;
        while (reader.Read())
        {
            hasRows = true;
            
            // Create sheet on first row or when exceeding row limit
            if (currentSheet == null || rowsInCurrentSheet >= 1048575)
            {
                if (rowsInCurrentSheet >= 1048575 && !options.SplitOversizeSheets)
                {
                    throw new InvalidOperationException(
                        $"Table {table.Name} exceeds Excel's row limit of 1,048,576 rows. " +
                        $"Enable SplitOversizeSheets to split across multiple sheets.");
                }

                var (sheet, checksum) = ExcelHelpers.NewSheetWithSuffix(
                    workbook, table.Name, partNumber, columns, options, usedSheetNames, sheetSuffix);
                currentSheet = sheet;
                rowsInCurrentSheet = 0;
                partNumber++;
            }

            rowsInCurrentSheet++;
            totalRows++;
            var excelRow = rowsInCurrentSheet + 1;

            for (int i = 0; i < columns.Count; i++)
            {
                var columnName = columns[i].Name;
                var (value, isText) = DataConverter.ReadValueAsText(reader, i, options, table.Name, columnName, totalRows, transformationPipeline);
                var cell = currentSheet.Cell(excelRow, i + 1);

                if (options.WriteAllAsText || isText)
                {
                    cell.Value = value ?? string.Empty;
                }
                else if (!string.IsNullOrEmpty(value))
                {
                    if (options.PreserveNumericTypes && 
                        double.TryParse(value, NumberStyles.Any, options.InvariantCulture, out var numValue))
                    {
                        cell.SetValue(numValue);
                    }
                    else
                    {
                        cell.Value = value;
                    }
                }

                masterChecksum.AddField(value);
            }
            masterChecksum.EndRow();
        }
        
        // Ensure we create at least one sheet even for empty tables/views
        if (!hasRows && currentSheet == null)
        {
            var (sheet, checksum) = ExcelHelpers.NewSheetWithSuffix(
                workbook, table.Name, partNumber, columns, options, usedSheetNames, sheetSuffix);
            currentSheet = sheet;
            partNumber++;
        }

        // Add suffix to table name in metadata for identification
        var tableNameWithSuffix = $"{table.Name}{sheetSuffix}";
        metadataRows?.Add(new MetaRow(
            tableNameWithSuffix,
            table.Type,
            totalRows,
            columns.Count,
            partNumber - 1,
            orderInfo.Mode,
            masterChecksum.GetChecksum()));
    }

    private static void WriteMetadataSheet(
        XLWorkbook workbook,
        SqliteToExcelOptions options,
        List<MetaRow> metadataRows,
        string sqlitePath,
        SqliteConnection connection,
        HashSet<string> usedSheetNames,
        TransformationPipeline? transformationPipeline)
    {
        var sheetName = ExcelHelpers.SanitizeSheetName(options.MetadataSheetName, usedSheetNames);
        var metaSheet = workbook.Worksheets.Add(sheetName);

        var dbFileInfo = new FileInfo(sqlitePath);
        string journalMode = "unknown";
        long userVersion = 0;
        long schemaVersion = 0;

        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode;";
            journalMode = cmd.ExecuteScalar()?.ToString() ?? "unknown";

            cmd.CommandText = "PRAGMA user_version;";
            userVersion = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);

            cmd.CommandText = "PRAGMA schema_version;";
            schemaVersion = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        }
        catch { }

        int row = 1;
        
        metaSheet.Cell(row, 1).Value = "Export Metadata";
        metaSheet.Cell(row, 1).Style.Font.Bold = true;
        metaSheet.Cell(row, 1).Style.Font.FontSize = 14;
        row += 2;

        metaSheet.Cell(row, 1).Value = "Database Information";
        metaSheet.Cell(row, 1).Style.Font.Bold = true;
        row++;

        metaSheet.Cell(row, 1).Value = "Database Path:";
        metaSheet.Cell(row, 2).Value = sqlitePath;
        row++;

        metaSheet.Cell(row, 1).Value = "File Size (bytes):";
        metaSheet.Cell(row, 2).Value = dbFileInfo.Exists ? dbFileInfo.Length : 0;
        row++;

        metaSheet.Cell(row, 1).Value = "Last Modified (UTC):";
        metaSheet.Cell(row, 2).Value = dbFileInfo.Exists ? dbFileInfo.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss") : "N/A";
        row++;

        metaSheet.Cell(row, 1).Value = "Journal Mode:";
        metaSheet.Cell(row, 2).Value = journalMode;
        row++;

        metaSheet.Cell(row, 1).Value = "User Version:";
        metaSheet.Cell(row, 2).Value = userVersion;
        row++;

        metaSheet.Cell(row, 1).Value = "Schema Version:";
        metaSheet.Cell(row, 2).Value = schemaVersion;
        row++;

        metaSheet.Cell(row, 1).Value = "Export Timestamp (UTC):";
        metaSheet.Cell(row, 2).Value = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        row++;

        metaSheet.Cell(row, 1).Value = "DB2XL Version:";
        metaSheet.Cell(row, 2).Value = typeof(SqliteToExcel).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        row += 2;

        metaSheet.Cell(row, 1).Value = "Export Options";
        metaSheet.Cell(row, 1).Style.Font.Bold = true;
        row++;

        metaSheet.Cell(row, 1).Value = "Write All As Text:";
        metaSheet.Cell(row, 2).Value = options.WriteAllAsText ? "Yes" : "No";
        row++;

        metaSheet.Cell(row, 1).Value = "Preserve Numeric Types:";
        metaSheet.Cell(row, 2).Value = options.PreserveNumericTypes ? "Yes" : "No";
        row++;

        metaSheet.Cell(row, 1).Value = "Include Views:";
        metaSheet.Cell(row, 2).Value = options.IncludeViews ? "Yes" : "No";
        row++;

        metaSheet.Cell(row, 1).Value = "BLOB Mode:";
        metaSheet.Cell(row, 2).Value = options.BlobMode.ToString();
        row++;

        metaSheet.Cell(row, 1).Value = "Order Rows Deterministically:";
        metaSheet.Cell(row, 2).Value = options.OrderRowsDeterministically ? "Yes" : "No";
        row++;

        metaSheet.Cell(row, 1).Value = "Split Oversize Sheets:";
        metaSheet.Cell(row, 2).Value = options.SplitOversizeSheets ? "Yes" : "No";
        row++;
        
        // Add comprehensive transformation information
        WriteTransformationMetadata(metaSheet, ref row, transformationPipeline);
        
        row++;

        metaSheet.Cell(row, 1).Value = "Table Export Summary";
        metaSheet.Cell(row, 1).Style.Font.Bold = true;
        row++;

        var headerRow = row;
        metaSheet.Cell(row, 1).Value = "Table Name";
        metaSheet.Cell(row, 2).Value = "Type";
        metaSheet.Cell(row, 3).Value = "Row Count";
        metaSheet.Cell(row, 4).Value = "Column Count";
        metaSheet.Cell(row, 5).Value = "Split Sheets";
        metaSheet.Cell(row, 6).Value = "Order Mode";
        metaSheet.Cell(row, 7).Value = "SHA256 Checksum";

        for (int col = 1; col <= 7; col++)
        {
            metaSheet.Cell(headerRow, col).Style.Font.Bold = true;
            metaSheet.Cell(headerRow, col).Style.Fill.BackgroundColor = XLColor.LightGray;
        }
        row++;

        foreach (var meta in metadataRows)
        {
            metaSheet.Cell(row, 1).Value = meta.TableName;
            metaSheet.Cell(row, 2).Value = meta.Type;
            metaSheet.Cell(row, 3).Value = meta.RowCount;
            metaSheet.Cell(row, 4).Value = meta.ColumnCount;
            metaSheet.Cell(row, 5).Value = meta.SplitSheets;
            metaSheet.Cell(row, 6).Value = meta.OrderMode.ToString();
            metaSheet.Cell(row, 7).Value = meta.ChecksumSha256;
            row++;
        }

        metaSheet.Columns().AdjustToContents();
    }

    /// <summary>
    /// Writes comprehensive transformation tracking information to the metadata sheet
    /// </summary>
    private static void WriteTransformationMetadata(
        IXLWorksheet metaSheet, 
        ref int row, 
        TransformationPipeline? transformationPipeline)
    {
        metaSheet.Cell(row, 1).Value = "Transformation Configuration";
        metaSheet.Cell(row, 1).Style.Font.Bold = true;
        row++;

        if (transformationPipeline == null)
        {
            metaSheet.Cell(row, 1).Value = "Transformations Enabled:";
            metaSheet.Cell(row, 2).Value = "No";
            row++;
            metaSheet.Cell(row, 1).Value = "Reason:";
            metaSheet.Cell(row, 2).Value = "No transformation configuration provided";
            row++;
            return;
        }

        var config = transformationPipeline.Configuration;
        
        // Basic transformation status
        metaSheet.Cell(row, 1).Value = "Transformations Enabled:";
        metaSheet.Cell(row, 2).Value = transformationPipeline.AreTransformationsEnabled ? "Yes" : "No";
        row++;

        if (!transformationPipeline.AreTransformationsEnabled)
        {
            metaSheet.Cell(row, 1).Value = "Reason:";
            metaSheet.Cell(row, 2).Value = "Transformations disabled in configuration";
            row++;
            return;
        }

        // Configuration details
        metaSheet.Cell(row, 1).Value = "Configuration Version:";
        metaSheet.Cell(row, 2).Value = config.Version;
        row++;

        metaSheet.Cell(row, 1).Value = "Error Handling Strategy:";
        metaSheet.Cell(row, 2).Value = config.Global.ErrorHandling.ToString();
        row++;

        metaSheet.Cell(row, 1).Value = "Max Errors Allowed:";
        metaSheet.Cell(row, 2).Value = config.Global.MaxErrors;
        row++;

        metaSheet.Cell(row, 1).Value = "Transformation Errors Encountered:";
        metaSheet.Cell(row, 2).Value = transformationPipeline.ErrorCount;
        row++;

        // Performance settings
        metaSheet.Cell(row, 1).Value = "Batch Size:";
        metaSheet.Cell(row, 2).Value = config.Global.Performance.BatchSize;
        row++;

        metaSheet.Cell(row, 1).Value = "Parallel Processing:";
        metaSheet.Cell(row, 2).Value = config.Global.Performance.EnableParallelProcessing ? "Yes" : "No";
        row++;

        if (config.Global.Performance.EnableParallelProcessing)
        {
            metaSheet.Cell(row, 1).Value = "Max Parallelism:";
            metaSheet.Cell(row, 2).Value = config.Global.Performance.MaxDegreeOfParallelism == 0 
                ? "Auto" 
                : config.Global.Performance.MaxDegreeOfParallelism.ToString();
            row++;
        }

        // Global transformers summary
        if (config.GlobalTransformers.Count > 0)
        {
            row++;
            metaSheet.Cell(row, 1).Value = "Global Transformers";
            metaSheet.Cell(row, 1).Style.Font.Bold = true;
            row++;

            metaSheet.Cell(row, 1).Value = "Count:";
            metaSheet.Cell(row, 2).Value = config.GlobalTransformers.Count;
            row++;

            metaSheet.Cell(row, 1).Value = "Transformer Names:";
            metaSheet.Cell(row, 2).Value = string.Join(", ", config.GlobalTransformers
                .Where(t => t.Enabled)
                .Select(t => t.Name));
            row++;
        }

        // Table-specific transformers summary
        if (config.Tables.Count > 0)
        {
            row++;
            metaSheet.Cell(row, 1).Value = "Table-Specific Transformations";
            metaSheet.Cell(row, 1).Style.Font.Bold = true;
            row++;

            metaSheet.Cell(row, 1).Value = "Tables with Transformations:";
            metaSheet.Cell(row, 2).Value = config.Tables.Count(kvp => kvp.Value.EnableTransformations);
            row++;

            var tablesWithFilters = config.Tables.Where(kvp => 
                kvp.Value.Filters?.ExcludeColumns.Count > 0 || 
                kvp.Value.Filters?.IncludeColumns.Count > 0).ToList();

            if (tablesWithFilters.Count > 0)
            {
                metaSheet.Cell(row, 1).Value = "Tables with Column Filters:";
                metaSheet.Cell(row, 2).Value = tablesWithFilters.Count;
                row++;
            }

            var totalColumnTransformers = config.Tables.Values
                .SelectMany(t => t.Columns.Values)
                .SelectMany(cols => cols)
                .Count(t => t.Enabled);

            if (totalColumnTransformers > 0)
            {
                metaSheet.Cell(row, 1).Value = "Total Column Transformers:";
                metaSheet.Cell(row, 2).Value = totalColumnTransformers;
                row++;
            }

            var totalRowTransformers = config.Tables.Values
                .SelectMany(t => t.RowTransformers)
                .Count(t => t.Enabled);

            if (totalRowTransformers > 0)
            {
                metaSheet.Cell(row, 1).Value = "Total Row Transformers:";
                metaSheet.Cell(row, 2).Value = totalRowTransformers;
                row++;
            }
        }

        // Data lineage tracking
        row++;
        metaSheet.Cell(row, 1).Value = "Data Lineage and Provenance";
        metaSheet.Cell(row, 1).Style.Font.Bold = true;
        row++;

        metaSheet.Cell(row, 1).Value = "Source System:";
        metaSheet.Cell(row, 2).Value = "SQLite Database";
        row++;

        metaSheet.Cell(row, 1).Value = "Transformation Pipeline:";
        metaSheet.Cell(row, 2).Value = "DB2XL Configuration-Based Pipeline";
        row++;

        metaSheet.Cell(row, 1).Value = "Data Integrity:";
        metaSheet.Cell(row, 2).Value = transformationPipeline.ErrorCount == 0 ? "Intact" : $"Modified ({transformationPipeline.ErrorCount} errors)";
        row++;

        metaSheet.Cell(row, 1).Value = "Audit Trail:";
        metaSheet.Cell(row, 2).Value = "Available in transformation configuration and error logs";
        row++;

        // Transformation quality metrics
        if (transformationPipeline.ErrorCount > 0)
        {
            row++;
            metaSheet.Cell(row, 1).Value = "Quality Metrics";
            metaSheet.Cell(row, 1).Style.Font.Bold = true;
            row++;

            metaSheet.Cell(row, 1).Value = "Error Rate:";
            metaSheet.Cell(row, 2).Value = $"{transformationPipeline.ErrorCount} errors";
            row++;

            metaSheet.Cell(row, 1).Value = "Data Quality Impact:";
            metaSheet.Cell(row, 2).Value = config.Global.ErrorHandling switch
            {
                ErrorHandling.StopOnError => "High - Processing stops on error",
                ErrorHandling.UseOriginalOnError => "Medium - Original values preserved on error",
                ErrorHandling.SkipErrors => "Low - Errors silently skipped",
                ErrorHandling.LogAndContinue => "Medium - Errors logged and tracked",
                _ => "Unknown"
            };
            row++;
        }
    }

    private static void ValidateInputs(string sqlitePath, string xlsxPath)
    {
        if (string.IsNullOrWhiteSpace(sqlitePath))
        {
            throw new ArgumentException("SQLite database path cannot be null or empty.", nameof(sqlitePath));
        }

        if (string.IsNullOrWhiteSpace(xlsxPath))
        {
            throw new ArgumentException("Excel output path cannot be null or empty.", nameof(xlsxPath));
        }

        if (!File.Exists(sqlitePath))
        {
            throw new FileNotFoundException($"SQLite database file not found: {sqlitePath}");
        }

        var outputDir = Path.GetDirectoryName(xlsxPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            try
            {
                Directory.CreateDirectory(outputDir);
            }
            catch (Exception ex)
            {
                throw new DirectoryNotFoundException($"Cannot create output directory: {outputDir}", ex);
            }
        }

        try
        {
            using var testConnection = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly;");
            testConnection.Open();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Cannot open SQLite database: {sqlitePath}", ex);
        }
    }

    /// <summary>
    /// Generates a comprehensive schema manifest for an Excel export
    /// </summary>
    public static SchemaManifest GenerateManifest(string sqlitePath, string xlsxPath, SqliteToExcelOptions? options = null)
    {
        options ??= new SqliteToExcelOptions();
        ValidateInputs(sqlitePath, xlsxPath);

        using var connection = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly;Cache=Shared;Pooling=True;");
        connection.Open();

        // Initialize transformation pipeline if configured
        TransformationPipeline? transformationPipeline = null;
        if (options.TransformationConfig != null)
        {
            var registry = options.TransformerRegistry ?? TransformerRegistryBuilder.CreateDefault();
            transformationPipeline = new TransformationPipeline(options.TransformationConfig, registry);
        }

        return ManifestGenerator.GenerateManifest(connection, sqlitePath, xlsxPath, "Excel", options, transformationPipeline);
    }

    /// <summary>
    /// Exports SQLite to Excel and generates a comprehensive schema manifest
    /// </summary>
    public static SchemaManifest ExportWithManifest(string sqlitePath, string xlsxPath, SqliteToExcelOptions? options = null)
    {
        // Perform the export
        Export(sqlitePath, xlsxPath, options);
        
        // Generate manifest
        var manifest = GenerateManifest(sqlitePath, xlsxPath, options);
        
        // Save manifest alongside Excel file
        var manifestPath = Path.ChangeExtension(xlsxPath, ".manifest.json");
        ManifestGenerator.SaveManifest(manifest, manifestPath);
        
        return manifest;
    }

    /// <summary>
    /// Validates an Excel export against its manifest
    /// </summary>
    public static ManifestValidationResult ValidateExport(string xlsxPath, string? manifestPath = null)
    {
        manifestPath ??= Path.ChangeExtension(xlsxPath, ".manifest.json");
        
        if (!File.Exists(manifestPath))
        {
            return new ManifestValidationResult
            {
                IsValid = false,
                ValidationTimestamp = DateTime.UtcNow,
                ExportPath = xlsxPath,
                ManifestPath = manifestPath,
                Errors = { $"Manifest file not found: {manifestPath}" }
            };
        }

        var manifest = ManifestGenerator.LoadManifest(manifestPath);
        var result = ManifestGenerator.ValidateExport(xlsxPath, manifest);
        result.ManifestPath = manifestPath;  // Set the manifest path that was used
        return result;
    }

    /// <summary>
    /// Gets tables to export based on options, with SelectionGrammar support and security filtering
    /// </summary>
    private static List<DB2XL.Core.Models.TableInfo> GetTablesToExport(SqliteConnection connection, SqliteToExcelOptions options)
    {
        List<DB2XL.Core.Models.TableInfo> tables;
        
        if (options.SelectionGrammar != null)
        {
            // Use SelectionGrammar for advanced table selection and filtering
            tables = GetTablesFromSelectionGrammar(connection, options.SelectionGrammar, options.SecurityFilter);
        }
        else
        {
            // Fall back to simple table name filtering
            tables = SqliteSchemaReader.GetDatabaseObjects(connection, options.TableNameLikeFilter, options.IncludeViews);
        }
        
        // Apply security filtering if configured
        if (options.SecurityFilter != null)
        {
            tables = ApplySecurityFiltering(tables, options.SecurityFilter);
        }
        
        return tables;
    }

    /// <summary>
    /// Processes SelectionGrammar to get table information with security filtering support
    /// </summary>
    private static List<DB2XL.Core.Models.TableInfo> GetTablesFromSelectionGrammar(SqliteConnection connection, SelectionGrammar grammar, SecurityFilterConfig? securityConfig)
    {
        // Validate SelectionGrammar for security compliance if configured
        if (securityConfig != null)
        {
            var securityFilter = new SecurityFilter(securityConfig);
            var validationResult = securityFilter.ValidateSelectionGrammar(grammar);
            if (!validationResult.IsAllowed)
            {
                throw new UnauthorizedAccessException($"Security validation failed: {validationResult.DenialReason}. {validationResult.SuggestedFix}");
            }
        }
        
        var result = new List<DB2XL.Core.Models.TableInfo>();
        
        // Validate the table exists
        var tableInfo = SqliteSchemaReader.GetDatabaseObjects(connection, null, true)
            .FirstOrDefault(t => t.Name.Equals(grammar.Table, StringComparison.OrdinalIgnoreCase));
            
        if (tableInfo != null)
        {
            // The SelectionGrammar contains WHERE/ORDER BY information that will be used during data retrieval
            result.Add(tableInfo);
        }
        else
        {
            throw new InvalidOperationException($"Table '{grammar.Table}' specified in SelectionGrammar does not exist in the database");
        }
        
        return result;
    }

    /// <summary>
    /// Applies security filtering to a list of tables
    /// </summary>
    private static List<DB2XL.Core.Models.TableInfo> ApplySecurityFiltering(List<DB2XL.Core.Models.TableInfo> tables, SecurityFilterConfig securityConfig)
    {
        var securityFilter = new SecurityFilter(securityConfig);
        var filteredTables = new List<DB2XL.Core.Models.TableInfo>();
        
        foreach (var table in tables)
        {
            var validationResult = securityFilter.ValidateTable(table.Name);
            if (validationResult.IsAllowed)
            {
                filteredTables.Add(table);
            }
            else
            {
                // In permissive mode, we skip denied tables; in strict mode, we would throw
                // For now, we'll log the denial and skip the table
                Console.WriteLine($"Warning: Table '{table.Name}' was filtered out due to security policy: {validationResult.DenialReason}");
            }
        }
        
        return filteredTables;
    }

    /// <summary>
    /// Applies security filtering to columns based on security configuration
    /// </summary>
    private static List<DB2XL.Core.Models.ColumnInfo> ApplyColumnSecurityFiltering(string tableName, List<DB2XL.Core.Models.ColumnInfo> columns, SecurityFilterConfig securityConfig)
    {
        var securityFilter = new SecurityFilter(securityConfig);
        var filteredColumns = new List<DB2XL.Core.Models.ColumnInfo>();
        
        foreach (var column in columns)
        {
            var validationResult = securityFilter.ValidateColumn(tableName, column.Name);
            if (validationResult.IsAllowed)
            {
                filteredColumns.Add(column);
            }
            else
            {
                // In permissive mode, we skip denied columns
                Console.WriteLine($"Warning: Column '{column.Name}' in table '{tableName}' was filtered out due to security policy: {validationResult.DenialReason}");
            }
        }
        
        return filteredColumns;
    }

    /// <summary>
    /// Builds SELECT SQL with SelectionGrammar support for advanced filtering
    /// </summary>
    private static string BuildSelectSqlWithGrammar(
        string tableName, 
        List<DB2XL.Core.Models.ColumnInfo> columns, 
        OrderInfo orderInfo, 
        SqliteToExcelOptions options)
    {
        // Check if we have SelectionGrammar for this specific table
        if (options.SelectionGrammar != null)
        {
            // Check if the SelectionGrammar is for this table
            if (options.SelectionGrammar.Table.Equals(tableName, StringComparison.OrdinalIgnoreCase))
            {
                // Use SqlBuilder from DB2XL.Query to generate SQL with advanced filtering
                var sqlBuilder = new SqlBuilder();
                
                var result = sqlBuilder.BuildQuery(options.SelectionGrammar);
                
                // For now, we can't easily pass parameters to the existing ExportTable infrastructure
                // So we'll validate that the query is safe and doesn't require parameters
                if (result.Parameters.Any())
                {
                    throw new InvalidOperationException(
                        "SelectionGrammar queries with parameters are not yet supported in this context. " +
                        "Please use literal values in WHERE clauses for now.");
                }
                
                return result.Sql;
            }
        }
        
        // Fall back to the original SQL building approach
        return SqlQueryBuilder.BuildSelectQuery(tableName, columns, orderInfo, options.OrderRowsDeterministically);
    }
}