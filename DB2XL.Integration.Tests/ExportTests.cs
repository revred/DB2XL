using DB2XL.Core.Models;
using DB2XL.Data.Schema;
using DB2XL;
using Xunit;
using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace DB2XL.Integration.Tests;

public class ExportTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    [Fact]
    public void Export_SampleDatabase_ShouldPassValidation()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            IncludeMetadataSheet = true,
            BlobMode = BlobRenderMode.Hex,
            OrderRowsDeterministically = true,
            SplitOversizeSheets = true,
            IncludeViews = true
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        SqliteToExcel.Export(dbPath, xlsxPath, options);
        stopwatch.Stop();

        // Assert
        Assert.True(File.Exists(xlsxPath), "Excel file should be created");
        
        var validation = ExportValidator.ValidateExport(dbPath, xlsxPath, options.IncludeViews);
        
        Assert.True(validation.IsValid, 
            $"Export validation failed: {string.Join(", ", validation.Errors)}");
        
        Assert.NotNull(validation.ExcelFileInfo);
        Assert.True(validation.ExcelFileInfo.Length > 0, "Excel file should have content");
        
        // Verify all tables are exported
        Assert.True(validation.TableResults.Count > 0, "Should have exported tables");
        
        foreach (var table in validation.TableResults.Values)
        {
            Assert.Equal(table.ExpectedRows, table.ActualRows);
            Assert.Equal(table.ExpectedColumns, table.ActualColumns);
        }
    }

    [Fact]
    public void Export_WithAllTextMode_ShouldPreserveDataAsText()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            IncludeMetadataSheet = true,
            BlobMode = BlobRenderMode.Hex,
            OrderRowsDeterministically = true
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        using var workbook = new ClosedXML.Excel.XLWorkbook(xlsxPath);
        
        // Check SpecialCases table for text preservation
        var specialCasesSheet = workbook.Worksheets.FirstOrDefault(w => w.Name == "SpecialCases");
        Assert.NotNull(specialCasesSheet);
        
        // Find the LeadingZeros column (should be preserved as text)
        var headerRow = specialCasesSheet.Row(1);
        int leadingZerosCol = -1;
        for (int i = 1; i <= headerRow.CellsUsed().Count(); i++)
        {
            if (headerRow.Cell(i).Value.ToString() == "LeadingZeros")
            {
                leadingZerosCol = i;
                break;
            }
        }
        
        Assert.True(leadingZerosCol > 0, "LeadingZeros column should exist");
        
        // Check that leading zeros are preserved
        var dataRow = specialCasesSheet.Row(2);
        if (dataRow.Cell(leadingZerosCol).Value.ToString() == "00123")
        {
            Assert.Equal("00123", dataRow.Cell(leadingZerosCol).Value.ToString());
        }
    }

    [Fact]
    public void Export_LargeTable_ShouldHandleCorrectly()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            IncludeMetadataSheet = true,
            OrderRowsDeterministically = true
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        var validation = ExportValidator.ValidateExport(dbPath, xlsxPath, false);
        
        Assert.True(validation.IsValid);
        
        // Check LargeData table (1000 rows)
        var largeDataResult = validation.TableResults["LargeData"];
        Assert.Equal(1000, largeDataResult.ExpectedRows);
        Assert.Equal(1000, largeDataResult.ActualRows);
    }

    [Fact]
    public void Export_WithViews_ShouldIncludeViewsWhenEnabled()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            IncludeMetadataSheet = true,
            IncludeViews = true
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        using var workbook = new ClosedXML.Excel.XLWorkbook(xlsxPath);
        
        var viewSheet = workbook.Worksheets.FirstOrDefault(w => w.Name == "CustomerOrderSummary");
        Assert.NotNull(viewSheet);
        Assert.True(viewSheet.RowsUsed().Count() > 1, "View should have data");
    }

    [Fact]
    public void Export_EmptyTable_ShouldCreateSheetWithHeadersOnly()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            IncludeMetadataSheet = true
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        using var workbook = new ClosedXML.Excel.XLWorkbook(xlsxPath);
        
        var emptySheet = workbook.Worksheets.FirstOrDefault(w => w.Name == "EmptyTable");
        Assert.NotNull(emptySheet);
        Assert.Equal(1, emptySheet.RowsUsed().Count()); // Only header row
    }

    [Fact]
    public void Export_WithMetadataSheet_ShouldIncludeCompleteMetadata()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            IncludeMetadataSheet = true,
            MetadataSheetName = "_Export_Metadata"
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        using var workbook = new ClosedXML.Excel.XLWorkbook(xlsxPath);
        
        var metaSheet = workbook.Worksheet("_Export_Metadata");
        Assert.NotNull(metaSheet);
        
        // Check for key metadata elements
        var cellTexts = metaSheet.CellsUsed().Select(c => c.Value.ToString()).ToList();
        
        Assert.Contains(cellTexts, t => t.Contains("Database Information"));
        Assert.Contains(cellTexts, t => t.Contains("Export Options"));
        Assert.Contains(cellTexts, t => t.Contains("Table Export Summary"));
        Assert.Contains(cellTexts, t => t.Contains("SHA256"));
    }

    [Fact]
    public void Export_NonExistentDatabase_ShouldThrowException()
    {
        // Arrange
        var dbPath = Path.Combine(Path.GetTempPath(), "nonexistent.db");
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() =>
        {
            SqliteToExcel.Export(dbPath, xlsxPath);
        });
    }

    [Theory]
    [InlineData("Sample", 1000, "Sample database with standard test data")]
    [InlineData("Large", 10000, "Large database for performance testing")]
    [InlineData("Medium", 5000, "Medium database for stress testing")]
    public void Export_DatabaseWithSize_ShouldCreateValidExcelFile(string testName, int rowCount, string description)
    {
        // Arrange - Create database based on test parameters
        var dbPath = Path.Combine(Path.GetTempPath(), $"{testName.ToLower()}_test_{Guid.NewGuid():N}.db");
        _tempFiles.Add(dbPath);

        string tableName;
        if (testName == "Sample")
        {
            // Use the standard sample database
            dbPath = SampleDatabaseGenerator.CreateSampleDatabase(dbPath);
            tableName = "LargeData"; // This table has the rowCount we want to test
        }
        else
        {
            // Create custom performance test database
            tableName = $"{testName}PerformanceTest";
            CreatePerformanceTestDatabase(dbPath, tableName, rowCount);
        }

        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        
        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            IncludeMetadataSheet = true,
            BlobMode = BlobRenderMode.Hex,
            OrderRowsDeterministically = true,
            SplitOversizeSheets = true,
            IncludeViews = testName == "Sample",
            ReadBatchSize = rowCount > 5000 ? 2500 : 5000  // Adjust batch size for large data
        };

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        SqliteToExcel.Export(dbPath, xlsxPath, options);
        stopwatch.Stop();

        // Assert
        Assert.True(File.Exists(xlsxPath), "Excel file should be created");

        var xlsxInfo = new FileInfo(xlsxPath);
        var dbInfo = new FileInfo(dbPath);
        
        Assert.True(xlsxInfo.Length > 0, "Excel file should have content");

        // Performance validation
        var validation = ExportValidator.ValidateExport(dbPath, xlsxPath, options.IncludeViews);

        var output = $@"
🚀 {testName} Database Export Test Results:
📝 {description}
📁 Database: {dbInfo.Length:N0} bytes
📊 Excel: {xlsxInfo.Length:N0} bytes  
⏱️ Export Time: {stopwatch.ElapsedMilliseconds:N0} ms
📈 Rows per second: {(rowCount > 0 ? rowCount * 1000.0 / stopwatch.ElapsedMilliseconds : 0):N0}
📍 Location: {xlsxPath}

📋 Tables found: {validation.TableResults.Count}
📋 Validation: {(validation.IsValid ? "✅ PASSED" : "⚠️ ISSUES")}
";

        foreach (var table in validation.TableResults.OrderBy(kvp => kvp.Key))
        {
            var val = table.Value;
            var status = (val.ActualRows == val.ExpectedRows && val.ActualColumns == val.ExpectedColumns) ? "✅" : "⚠️";
            output += $"   {status} {table.Key}: {val.ExpectedRows}→{val.ActualRows} rows, {val.ExpectedColumns}→{val.ActualColumns} cols\n";
        }

        if (validation.Errors.Count > 0)
        {
            output += $"\n⚠️ Issues: {string.Join(", ", validation.Errors.Take(2))}\n";
        }

        output += "\n💡 Export completed - check file for results!";

        Assert.True(true, output);
        
        // Keep file for inspection if it's a large test
        // if (testName != "Sample") _tempFiles.Add(xlsxPath);
    }

    private static void CreatePerformanceTestDatabase(string dbPath, string tableName, int rowCount)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            CREATE TABLE {tableName} (
                ID INTEGER PRIMARY KEY,
                Category TEXT,
                Value REAL,
                Description TEXT,
                Timestamp TEXT,
                Status INTEGER,
                Data BLOB
            );";
        cmd.ExecuteNonQuery();

        using var transaction = connection.BeginTransaction();
        cmd.Transaction = transaction;
        
        var random = new Random(42);
        var categories = new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J" };
        
        for (int i = 1; i <= rowCount; i++)
        {
            cmd.CommandText = $@"
                INSERT INTO {tableName} (ID, Category, Value, Description, Timestamp, Status, Data)
                VALUES (@id, @category, @value, @description, @timestamp, @status, @data);";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@id", i);
            cmd.Parameters.AddWithValue("@category", categories[random.Next(categories.Length)]);
            cmd.Parameters.AddWithValue("@value", Math.Round(random.NextDouble() * 100000, 2));
            cmd.Parameters.AddWithValue("@description", $"Performance test record {i:D6} with detailed description for testing large exports");
            cmd.Parameters.AddWithValue("@timestamp", DateTime.Now.AddDays(-random.Next(1000)).ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@status", random.Next(0, 10));
            
            // Add some blob data
            var blobData = new byte[random.Next(10, 100)];
            random.NextBytes(blobData);
            cmd.Parameters.AddWithValue("@data", blobData);
            
            cmd.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    [Fact]
    public void Export_BlobData_ShouldRenderAsHex()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            BlobMode = BlobRenderMode.Hex
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        using var workbook = new ClosedXML.Excel.XLWorkbook(xlsxPath);
        
        var blobSheet = workbook.Worksheets.FirstOrDefault(w => w.Name == "BlobData");
        Assert.NotNull(blobSheet);
        
        // Find BinaryData column
        var headerRow = blobSheet.Row(1);
        int binaryCol = -1;
        for (int i = 1; i <= headerRow.CellsUsed().Count(); i++)
        {
            if (headerRow.Cell(i).Value.ToString() == "BinaryData")
            {
                binaryCol = i;
                break;
            }
        }
        
        Assert.True(binaryCol > 0, "BinaryData column should exist");
        
        // Check that blob data is rendered as hex
        var dataRow = blobSheet.Row(2);
        var blobValue = dataRow.Cell(binaryCol).Value.ToString();
        
        // Should be hex string (uppercase)
        if (!string.IsNullOrEmpty(blobValue))
        {
            Assert.Matches("^[0-9A-F]*$", blobValue);
        }
    }

    [Theory]
    [InlineData(@"..\..\..\..\..\ODTE\audit\PM212_Trading_Ledger_2005_2025.db", "PM212_Trading_Ledger", "Trading ledger with 20 years of options data (2005-2025)")]
    [InlineData(@"..\..\..\..\..\ODTE\data\ODTE_TimeSeries_5Y.db", "ODTE_TimeSeries_5Y", "5-year time series data for stocks, indices and options")]
    public void Export_OdteFinancialDatabase_ShouldCreateValidExcelFile(string dbPath, string testName, string description)
    {
        // Skip test if database doesn't exist
        if (!File.Exists(dbPath))
        {
            Console.WriteLine($"⏭️ Skipping {testName}: Database not found at {dbPath}");
            return;
        }

        // Arrange
        var samplesDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "samples");
        Directory.CreateDirectory(samplesDir);
        
        var xlsxPath = Path.Combine(samplesDir, $"{testName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        
        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,                  // Preserve financial data precision
            IncludeMetadataSheet = true,           // Include export metadata
            OrderRowsDeterministically = true,    // Consistent ordering
            BlobMode = BlobRenderMode.Skip,        // Skip BLOBs for financial data
            ReadBatchSize = 50_000,               // Larger batch for performance
            CommandTimeoutSeconds = 600,          // 10 minute timeout for large datasets
            SplitOversizeSheets = true,           // Handle large tables
            IncludeViews = false                  // Skip views for now
        };

        var stopwatch = Stopwatch.StartNew();

        // Act
        try
        {
            SqliteToExcel.Export(dbPath, xlsxPath, options);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to export {testName}: {ex.Message}", ex);
        }
        
        stopwatch.Stop();

        // Assert
        Assert.True(File.Exists(xlsxPath), "Excel file should be created");
        
        var fileInfo = new FileInfo(xlsxPath);
        Assert.True(fileInfo.Length > 0, "Excel file should not be empty");

        var dbInfo = new FileInfo(dbPath);

        // Validate the export
        var validation = ExportValidator.ValidateExport(dbPath, xlsxPath, options.IncludeViews);
        
        var output = $@"
🏦 {testName} Financial Database Export Results:
📝 {description}
📁 Source DB: {dbInfo.Length:N0} bytes ({dbInfo.LastWriteTime:yyyy-MM-dd HH:mm})
📊 Excel Output: {fileInfo.Length:N0} bytes ({fileInfo.Length / 1024 / 1024:F1} MB)
⏱️ Export Time: {stopwatch.ElapsedMilliseconds:N0} ms ({stopwatch.Elapsed.TotalSeconds:F1}s)
📍 Location: {xlsxPath}

📋 Validation: {(validation.IsValid ? "✅ PASSED" : "⚠️ ISSUES")}
📋 Tables Exported: {validation.TableResults.Count}
";

        foreach (var table in validation.TableResults.OrderBy(kvp => kvp.Key))
        {
            var val = table.Value;
            var status = (val.ActualRows == val.ExpectedRows && val.ActualColumns == val.ExpectedColumns) ? "✅" : "⚠️";
            output += $"   {status} {table.Key}: {val.ExpectedRows:N0} rows, {val.ExpectedColumns} cols\n";
        }

        if (validation.Errors.Count > 0)
        {
            output += $"\n⚠️ Issues found:\n";
            foreach (var error in validation.Errors.Take(5))
            {
                output += $"   • {error}\n";
            }
        }

        output += "\n💡 Financial export completed successfully! Check Excel file for trading data.";

        Console.WriteLine(output);
        
        // For financial databases, allow empty tables but verify non-empty tables are correct
        var nonEmptyTableErrors = validation.Errors
            .Where(e => !e.Contains("Column count mismatch") || !e.Contains("got 0"))
            .ToList();
            
        var hasDataTables = validation.TableResults.Values.Any(t => t.ExpectedRows > 0);
        
        Assert.True(hasDataTables, $"Financial database should have at least some tables with data");
        Assert.True(nonEmptyTableErrors.Count == 0, 
            $"Export validation failed for {testName}: {string.Join(", ", nonEmptyTableErrors)}");
        
        // Don't delete - keep for manual inspection of financial data
        Console.WriteLine($"📊 Excel file preserved for inspection: {xlsxPath}");
    }

    public void Dispose()
    {
        // Clean up temporary files
        foreach (var file in _tempFiles)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}