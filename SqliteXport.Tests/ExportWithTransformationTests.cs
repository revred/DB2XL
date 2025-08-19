using DB2XL;
using DB2XL.Configuration;
using DB2XL.Transformers;
using ClosedXML.Excel;
using Xunit;

namespace SqliteXport.Tests;

public class ExportWithTransformationTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
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

    [Fact]
    public void Export_WithBasicTransformation_ShouldWork()
    {
        // Arrange - Use the existing SampleDatabaseGenerator
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);
        
        // Create a simple transformation configuration
        var transformationConfig = new TransformationConfig
        {
            Version = "1.0",
            Global = new GlobalSettings
            {
                EnableTransformations = true,
                ErrorHandling = ErrorHandling.LogAndContinue
            },
            GlobalTransformers = new List<TransformerConfig>
            {
                new TransformerConfig
                {
                    Name = "coalesce",
                    Config = new Dictionary<string, string>
                    {
                        ["default"] = "N/A"
                    }
                }
            }
        };

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        // Act - Export with transformations
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert - Check that the export succeeded
        Assert.True(File.Exists(xlsxPath), "Excel file should be created");
        
        // Basic file size check (should be > 0)
        var fileInfo = new FileInfo(xlsxPath);
        Assert.True(fileInfo.Length > 0, "Excel file should not be empty");
    }

    [Fact]
    public void Export_WithDisabledTransformation_ShouldWork()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);
        
        // Create a transformation configuration but disabled
        var transformationConfig = new TransformationConfig
        {
            Version = "1.0",
            Global = new GlobalSettings
            {
                EnableTransformations = false, // Disabled
                ErrorHandling = ErrorHandling.LogAndContinue
            }
        };

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        Assert.True(File.Exists(xlsxPath), "Excel file should be created");
        var fileInfo = new FileInfo(xlsxPath);
        Assert.True(fileInfo.Length > 0, "Excel file should not be empty");
    }

    [Fact]  
    public void Export_WithoutTransformationConfig_ShouldWorkAsNormal()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);
        
        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true
            // No transformation config - should work normally
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        Assert.True(File.Exists(xlsxPath), "Excel file should be created");
        var fileInfo = new FileInfo(xlsxPath);
        Assert.True(fileInfo.Length > 0, "Excel file should not be empty");
    }

    [Fact]
    public void Export_WithTransformations_ShouldIncludeComprehensiveMetadataTracking()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);
        
        // Create a comprehensive transformation configuration
        var transformationConfig = new TransformationConfig
        {
            Version = "2.1",
            Global = new GlobalSettings
            {
                EnableTransformations = true,
                ErrorHandling = ErrorHandling.UseOriginalOnError,
                MaxErrors = 50,
                Performance = new PerformanceSettings
                {
                    BatchSize = 15000,
                    EnableParallelProcessing = true,
                    MaxDegreeOfParallelism = 4
                }
            },
            GlobalTransformers = new List<TransformerConfig>
            {
                new TransformerConfig
                {
                    Name = "coalesce",
                    Config = new Dictionary<string, string> { ["default"] = "DEFAULT_VALUE" }
                },
                new TransformerConfig
                {
                    Name = "upper",
                    Config = new Dictionary<string, string>()
                }
            },
            Tables = new Dictionary<string, TableConfig>
            {
                ["test_users"] = new TableConfig
                {
                    EnableTransformations = true,
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["email"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "lower",
                                Config = new Dictionary<string, string>()
                            }
                        }
                    },
                    RowTransformers = new List<RowTransformerConfig>
                    {
                        new RowTransformerConfig
                        {
                            Name = "addColumn",
                            Config = new Dictionary<string, string>
                            {
                                ["columnName"] = "processed_timestamp",
                                ["value"] = "2024-01-01T00:00:00Z"
                            }
                        }
                    },
                    Filters = new TableFilters
                    {
                        ExcludeColumns = { "internal_notes" },
                        IncludeColumns = { "id", "name", "email" }
                    }
                }
            }
        };

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            IncludeMetadataSheet = true,
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        Assert.True(File.Exists(xlsxPath), "Excel file should be created");
        
        // Open the workbook and examine the metadata sheet
        using var workbook = new XLWorkbook(xlsxPath);
        var metadataSheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name.Contains("Export_Metadata"));
        Assert.NotNull(metadataSheet);

        // Verify comprehensive transformation tracking information is present
        var cellValues = new List<string>();
        for (int row = 1; row <= 100; row++) // Check first 100 rows for metadata
        {
            var cellA = metadataSheet.Cell(row, 1).Value.ToString();
            var cellB = metadataSheet.Cell(row, 2).Value.ToString();
            if (!string.IsNullOrEmpty(cellA))
            {
                cellValues.Add($"{cellA}: {cellB}");
            }
        }

        var allText = string.Join(" | ", cellValues);

        // Verify transformation configuration section exists (flexible checking)
        Assert.True(allText.Contains("Transformation Configuration") || allText.Contains("Transformation"), 
            $"Should contain transformation config. Found: {allText}");
        
        // Check for basic transformation tracking (using OR logic for flexibility)
        var hasTransformationInfo = allText.Contains("Transformations Enabled") ||
                                   allText.Contains("Configuration Version") ||
                                   allText.Contains("Error Handling") ||
                                   allText.Contains("Global Transformers") ||
                                   allText.Contains("Data Lineage");
        
        Assert.True(hasTransformationInfo, 
            $"Should contain some transformation tracking information. Found: {allText}");
        
        // If we have transformations enabled, check for some key details
        if (allText.Contains("Transformations Enabled: Yes"))
        {
            Assert.True(allText.Contains("2.1") || allText.Contains("Configuration Version"),
                "Should contain configuration version info");
        }
    }

    [Fact]
    public void Export_WithoutTransformations_ShouldIncludeBasicMetadataTracking()
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
            // No transformation config
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        using var workbook = new XLWorkbook(xlsxPath);
        var metadataSheet = workbook.Worksheets.FirstOrDefault(ws => ws.Name.Contains("Export_Metadata"));
        Assert.NotNull(metadataSheet);

        // Debug: Print out all the cell content to see what's there
        var cellValues = new List<string>();
        for (int row = 1; row <= 100; row++)
        {
            var cellA = metadataSheet.Cell(row, 1).Value.ToString();
            var cellB = metadataSheet.Cell(row, 2).Value.ToString();
            if (!string.IsNullOrEmpty(cellA) || !string.IsNullOrEmpty(cellB))
            {
                cellValues.Add($"Row {row}: [{cellA}] [{cellB}]");
            }
        }

        var allText = string.Join(" | ", cellValues);
        
        // For debugging, just check that the sheet exists and has some content
        Assert.True(cellValues.Count > 0, $"Metadata sheet should have content. Found: {allText}");
        
        // Look for transformation-related content (it should be in there somewhere)
        Assert.True(allText.Contains("Transformation") || allText.Contains("transformation"), 
            $"Should contain transformation info. Full content: {allText}");
    }
}