using DB2XL;
using DB2XL.Configuration;
using DB2XL.Transformers;
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
}