using DB2XL.Core.Models;
using DB2XL.Data.Schema;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;

namespace DB2XL.Integration.Tests;

public class ExportValidator
{
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public Dictionary<string, TableValidation> TableResults { get; set; } = new();
        public FileInfo? ExcelFileInfo { get; set; }
        public FileInfo? DatabaseFileInfo { get; set; }
    }

    public class TableValidation
    {
        public string TableName { get; set; } = "";
        public int ExpectedRows { get; set; }
        public int ActualRows { get; set; }
        public int ExpectedColumns { get; set; }
        public int ActualColumns { get; set; }
        public string? ExpectedChecksum { get; set; }
        public string? ActualChecksum { get; set; }
        public bool ChecksumMatch { get; set; }
        public List<string> Issues { get; set; } = new();
    }

    public static ValidationResult ValidateExport(string dbPath, string xlsxPath, bool includeViews = false)
    {
        var result = new ValidationResult { IsValid = true };

        try
        {
            if (!File.Exists(dbPath))
            {
                result.Errors.Add($"Database file not found: {dbPath}");
                result.IsValid = false;
                return result;
            }

            if (!File.Exists(xlsxPath))
            {
                result.Errors.Add($"Excel file not found: {xlsxPath}");
                result.IsValid = false;
                return result;
            }

            result.DatabaseFileInfo = new FileInfo(dbPath);
            result.ExcelFileInfo = new FileInfo(xlsxPath);

            using var workbook = new XLWorkbook(xlsxPath);
            using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
            connection.Open();

            var dbTables = GetDatabaseTables(connection, includeViews);
            var excelSheets = workbook.Worksheets.ToDictionary(ws => ws.Name, ws => ws);

            foreach (var table in dbTables)
            {
                var validation = ValidateTable(connection, workbook, table, excelSheets);
                result.TableResults[table] = validation;

                if (validation.ActualRows != validation.ExpectedRows)
                {
                    result.Errors.Add($"Table {table}: Row count mismatch. Expected {validation.ExpectedRows}, got {validation.ActualRows}");
                    result.IsValid = false;
                }

                if (validation.ActualColumns != validation.ExpectedColumns)
                {
                    result.Errors.Add($"Table {table}: Column count mismatch. Expected {validation.ExpectedColumns}, got {validation.ActualColumns}");
                    result.IsValid = false;
                }

                if (!validation.ChecksumMatch && !string.IsNullOrEmpty(validation.ExpectedChecksum))
                {
                    result.Warnings.Add($"Table {table}: Checksum mismatch");
                }
            }

            if (excelSheets.ContainsKey("_Export_Metadata"))
            {
                ValidateMetadataSheet(workbook.Worksheet("_Export_Metadata"), result);
            }
            else
            {
                result.Warnings.Add("Metadata sheet not found");
            }

            foreach (var sheet in excelSheets.Keys)
            {
                if (sheet == "_Export_Metadata") continue;
                
                var baseName = sheet.Contains("_p") ? sheet.Substring(0, sheet.LastIndexOf("_p")) : sheet;
                if (!dbTables.Contains(baseName) && !sheet.StartsWith("~"))
                {
                    result.Warnings.Add($"Unexpected sheet in Excel: {sheet}");
                }
            }

            if (result.ExcelFileInfo.Length > 50 * 1024 * 1024)
            {
                result.Warnings.Add($"Excel file is large: {result.ExcelFileInfo.Length / (1024 * 1024):F2} MB");
            }

        }
        catch (Exception ex)
        {
            result.Errors.Add($"Validation error: {ex.Message}");
            result.IsValid = false;
        }

        return result;
    }

    private static TableValidation ValidateTable(SqliteConnection connection, XLWorkbook workbook, string tableName, Dictionary<string, IXLWorksheet> sheets)
    {
        var validation = new TableValidation { TableName = tableName };

        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM \"{tableName.Replace("\"", "\"\"")}\"";
            validation.ExpectedRows = Convert.ToInt32(cmd.ExecuteScalar());

            // Check if it's a view or table
            cmd.CommandText = $"SELECT type FROM sqlite_master WHERE name = @name";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@name", tableName);
            var objectType = cmd.ExecuteScalar()?.ToString() ?? "table";
            
            var columns = new List<string>();
            
            if (objectType == "view")
            {
                // For views, we need to get column info differently
                cmd.CommandText = $"SELECT * FROM \"{tableName.Replace("\"", "\"\"")}\" LIMIT 0";
                using var reader = cmd.ExecuteReader();
                validation.ExpectedColumns = reader.FieldCount;
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    columns.Add(reader.GetName(i));
                }
            }
            else
            {
                // For tables, use PRAGMA table_info
                cmd.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    columns.Add(reader.GetString(1));
                }
                validation.ExpectedColumns = columns.Count;
            }

            validation.ExpectedChecksum = CalculateTableChecksum(connection, tableName);

            int actualRows = 0;
            var matchingSheets = sheets.Where(kvp => kvp.Key == tableName || kvp.Key.StartsWith($"{tableName}_p")).ToList();

            if (matchingSheets.Count == 0)
            {
                validation.Issues.Add($"No Excel sheet found for table {tableName}");
                return validation;
            }

            foreach (var sheetPair in matchingSheets.OrderBy(kvp => kvp.Key))
            {
                var sheet = sheetPair.Value;
                var rowCount = sheet.RowsUsed().Count() - 1;
                actualRows += Math.Max(0, rowCount);

                if (validation.ActualColumns == 0 && sheet.RowsUsed().Any())
                {
                    validation.ActualColumns = sheet.Row(1).CellsUsed().Count();
                }

                var headerRow = sheet.Row(1);
                for (int i = 0; i < columns.Count && i < validation.ActualColumns; i++)
                {
                    var expectedCol = columns[i];
                    var actualCol = headerRow.Cell(i + 1).Value.ToString();
                    if (expectedCol != actualCol)
                    {
                        validation.Issues.Add($"Column name mismatch at position {i + 1}: expected '{expectedCol}', got '{actualCol}'");
                    }
                }
            }

            validation.ActualRows = actualRows;
            validation.ChecksumMatch = validation.ExpectedChecksum == validation.ActualChecksum;

        }
        catch (Exception ex)
        {
            validation.Issues.Add($"Error validating table: {ex.Message}");
        }

        return validation;
    }

    private static string CalculateTableChecksum(SqliteConnection connection, string tableName)
    {
        using var sha256 = SHA256.Create();
        using var cmd = connection.CreateCommand();
        
        // Check if it's a view or table
        cmd.CommandText = "SELECT type FROM sqlite_master WHERE name = @name";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@name", tableName);
        var objectType = cmd.ExecuteScalar()?.ToString() ?? "table";
        
        var columns = new List<string>();
        string orderBy;
        
        if (objectType == "view")
        {
            // For views, get column info differently and don't use rowid
            cmd.CommandText = $"SELECT * FROM \"{tableName.Replace("\"", "\"\"")}\" LIMIT 0";
            cmd.Parameters.Clear();
            using var reader = cmd.ExecuteReader();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add($"\"{reader.GetName(i).Replace("\"", "\"\"")}\"");
            }
            // Views can't be ordered deterministically, so no ORDER BY
            orderBy = "";
        }
        else
        {
            // For tables, use PRAGMA table_info
            cmd.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")";
            cmd.Parameters.Clear();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    columns.Add($"\"{reader.GetString(1).Replace("\"", "\"\"")}\"");
                }
            }

            var pkColumns = columns.Where((col, idx) => 
            {
                cmd.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")";
                cmd.Parameters.Clear();
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if ($"\"{r.GetString(1).Replace("\"", "\"\"")}\"" == col)
                        return r.GetInt32(5) > 0;
                }
                return false;
            }).ToList();

            orderBy = pkColumns.Count > 0 ? 
                $"ORDER BY {string.Join(", ", pkColumns)} ASC" : 
                "ORDER BY rowid ASC";
        }

        cmd.CommandText = $"SELECT {string.Join(", ", columns)} FROM \"{tableName.Replace("\"", "\"\"")}\" {orderBy}";
        
        using var dataReader = cmd.ExecuteReader();
        var buffer = new StringBuilder();

        while (dataReader.Read())
        {
            for (int i = 0; i < dataReader.FieldCount; i++)
            {
                if (i > 0) buffer.Append('\x1F');
                
                if (dataReader.IsDBNull(i))
                {
                    buffer.Append('\x00');
                }
                else
                {
                    buffer.Append(dataReader.GetValue(i).ToString());
                }
            }
            buffer.Append('\x1E');
        }

        var bytes = Encoding.UTF8.GetBytes(buffer.ToString());
        var hash = sha256.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
    }

    private static HashSet<string> GetDatabaseTables(SqliteConnection connection, bool includeViews)
    {
        var tables = new HashSet<string>();
        using var cmd = connection.CreateCommand();
        
        var types = includeViews ? "('table', 'view')" : "('table')";
        cmd.CommandText = $@"
            SELECT name FROM sqlite_master 
            WHERE type IN {types} 
            AND name NOT LIKE 'sqlite_%' 
            ORDER BY name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static void ValidateMetadataSheet(IXLWorksheet metaSheet, ValidationResult result)
    {
        try
        {
            var usedCells = metaSheet.CellsUsed().Count();
            if (usedCells < 10)
            {
                result.Warnings.Add("Metadata sheet appears incomplete");
            }

            var exportTimestampFound = false;
            var checksumColumnFound = false;

            foreach (var row in metaSheet.RowsUsed())
            {
                var firstCell = row.Cell(1).Value.ToString();
                if (firstCell.Contains("Export Timestamp"))
                    exportTimestampFound = true;
                if (firstCell.Contains("SHA256") || row.CellsUsed().Any(c => c.Value.ToString().Contains("SHA256")))
                    checksumColumnFound = true;
            }

            if (!exportTimestampFound)
                result.Warnings.Add("Export timestamp not found in metadata");
            if (!checksumColumnFound)
                result.Warnings.Add("Checksum column not found in metadata");
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Error validating metadata sheet: {ex.Message}");
        }
    }

    public static void PrintValidationReport(ValidationResult result)
    {
        Console.WriteLine("\n" + new string('=', 80));
        Console.WriteLine("EXPORT VALIDATION REPORT");
        Console.WriteLine(new string('=', 80));

        Console.WriteLine($"\nValidation Status: {(result.IsValid ? "✅ PASSED" : "❌ FAILED")}");

        if (result.DatabaseFileInfo != null)
        {
            Console.WriteLine($"\nDatabase: {result.DatabaseFileInfo.Name}");
            Console.WriteLine($"  Size: {result.DatabaseFileInfo.Length:N0} bytes");
        }

        if (result.ExcelFileInfo != null)
        {
            Console.WriteLine($"\nExcel File: {result.ExcelFileInfo.Name}");
            Console.WriteLine($"  Size: {result.ExcelFileInfo.Length:N0} bytes");
        }

        if (result.Errors.Count > 0)
        {
            Console.WriteLine($"\n❌ Errors ({result.Errors.Count}):");
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"  • {error}");
            }
        }

        if (result.Warnings.Count > 0)
        {
            Console.WriteLine($"\n⚠️  Warnings ({result.Warnings.Count}):");
            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"  • {warning}");
            }
        }

        if (result.TableResults.Count > 0)
        {
            Console.WriteLine($"\n📊 Table Validation Results:");
            Console.WriteLine(new string('-', 80));
            Console.WriteLine($"{"Table",-30} {"DB Rows",10} {"XL Rows",10} {"Columns",10} {"Status",10}");
            Console.WriteLine(new string('-', 80));

            foreach (var table in result.TableResults.OrderBy(kvp => kvp.Key))
            {
                var val = table.Value;
                var status = (val.ActualRows == val.ExpectedRows && val.ActualColumns == val.ExpectedColumns) ? "✅" : "❌";
                Console.WriteLine($"{table.Key,-30} {val.ExpectedRows,10:N0} {val.ActualRows,10:N0} {val.ExpectedColumns,10} {status,10}");
                
                foreach (var issue in val.Issues)
                {
                    Console.WriteLine($"    ⚠️  {issue}");
                }
            }
        }

        Console.WriteLine("\n" + new string('=', 80));
    }
}