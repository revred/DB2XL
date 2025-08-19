using System.Globalization;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;

namespace DB2XL;

public static class SqliteToExcel
{
    public static void Export(string sqlitePath, string xlsxPath, SqliteToExcelOptions? options = null)
    {
        options ??= new SqliteToExcelOptions();

        ValidateInputs(sqlitePath, xlsxPath);

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

        foreach (var table in tables)
        {
            ExportTable(connection, workbook, table, options, usedSheetNames, metadataRows);
        }

        if (metadataRows != null)
        {
            WriteMetadataSheet(workbook, options, metadataRows, sqlitePath, connection, usedSheetNames);
        }

        var outputDir = Path.GetDirectoryName(xlsxPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        workbook.SaveAs(xlsxPath);
        transaction.Commit();
    }

    private static void ExportTable(
        SqliteConnection connection,
        XLWorkbook workbook,
        TableInfo table,
        SqliteToExcelOptions options,
        HashSet<string> usedSheetNames,
        List<MetaRow>? metadataRows)
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
                var (value, isText) = DataConverter.ReadValueAsText(reader, i, options);
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

    private static void WriteMetadataSheet(
        XLWorkbook workbook,
        SqliteToExcelOptions options,
        List<MetaRow> metadataRows,
        string sqlitePath,
        SqliteConnection connection,
        HashSet<string> usedSheetNames)
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
        row += 2;

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
}