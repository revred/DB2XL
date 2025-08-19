using System.Globalization;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using DB2XL.Configuration;
using DB2XL.Transformers;
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

        var tables = DatabaseDiscovery.GetObjects(connection, options.TableNameLikeFilter, options.IncludeViews);
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

        var tables = DatabaseDiscovery.GetObjects(connection, options.TableNameLikeFilter, options.IncludeViews);
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
        TableInfo table,
        SqliteToExcelOptions options,
        HashSet<string> usedSheetNames,
        List<MetaRow>? metadataRows,
        TransformationPipeline? transformationPipeline)
    {
        var columns = DatabaseDiscovery.GetColumns(connection, table.Name);
        if (columns.Count > 16384)
        {
            throw new InvalidOperationException($"Table {table.Name} has {columns.Count} columns, exceeding Excel's limit of 16,384.");
        }

        var orderInfo = DatabaseDiscovery.DetermineOrder(connection, table.Name, columns);
        var sql = SqlHelpers.BuildSelectSql(table.Name, columns, orderInfo, options.OrderRowsDeterministically);

        int partNumber = 1;
        int totalRows = 0;
        int rowsInCurrentSheet = 0;
        IXLWorksheet? currentSheet = null;
        ChecksumBuilder? masterChecksum = new ChecksumBuilder();

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
                    workbook, table.Name, partNumber, columns, options, usedSheetNames);
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

                masterChecksum.UpdateField(value);
            }
            masterChecksum.EndRow();
        }
        
        // Ensure we create at least one sheet even for empty tables/views
        if (!hasRows && currentSheet == null)
        {
            var (sheet, checksum) = ExcelHelpers.NewSheet(
                workbook, table.Name, partNumber, columns, options, usedSheetNames);
            currentSheet = sheet;
            partNumber++;
        }

        metadataRows?.Add(new MetaRow(
            table.Name,
            table.Type,
            totalRows,
            columns.Count,
            partNumber - 1,
            orderInfo.Mode,
            masterChecksum.FinalizeHex()));
    }

    private static void ExportTableWithSuffix(
        SqliteConnection connection,
        XLWorkbook workbook,
        TableInfo table,
        SqliteToExcelOptions options,
        HashSet<string> usedSheetNames,
        List<MetaRow>? metadataRows,
        TransformationPipeline? transformationPipeline,
        string sheetSuffix)
    {
        var columns = DatabaseDiscovery.GetColumns(connection, table.Name);
        if (columns.Count > 16384)
        {
            throw new InvalidOperationException($"Table {table.Name} has {columns.Count} columns, exceeding Excel's limit of 16,384.");
        }

        var orderInfo = DatabaseDiscovery.DetermineOrder(connection, table.Name, columns);
        var sql = SqlHelpers.BuildSelectSql(table.Name, columns, orderInfo, options.OrderRowsDeterministically);

        int partNumber = 1;
        int totalRows = 0;
        int rowsInCurrentSheet = 0;
        IXLWorksheet? currentSheet = null;
        ChecksumBuilder? masterChecksum = new ChecksumBuilder();

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

                masterChecksum.UpdateField(value);
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
            masterChecksum.FinalizeHex()));
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
}