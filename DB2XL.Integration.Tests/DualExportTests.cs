using DB2XL.Core.Models;
using DB2XL.Data.Schema;
using DB2XL;
using DB2XL.Transform.Configuration;
using DB2XL.Transform.Interfaces;
using ClosedXML.Excel;
using Xunit;

namespace DB2XL.Integration.Tests;

public class DualExportTests : IDisposable
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
    public void Export_TransformedOnly_ShouldExportOnlyTransformedData()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);

        var transformationConfig = new TransformationConfig
        {
            Global = new GlobalSettings { EnableTransformations = true },
            GlobalTransformers = new List<TransformerConfig>
            {
                new TransformerConfig
                {
                    Name = "coalesce",
                    Config = new Dictionary<string, string> { ["default"] = "N/A" }
                }
            }
        };

        var options = new SqliteToExcelOptions
        {
            DualExportStrategy = DualExportStrategy.TransformedOnly,
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        Assert.True(File.Exists(xlsxPath), "Excel file should be created");
        
        using var workbook = new XLWorkbook(xlsxPath);
        var worksheetNames = workbook.Worksheets.Select(ws => ws.Name).ToList();
        
        // Should not have any "_Raw" or "_Transformed" suffixes - just table names
        Assert.DoesNotContain(worksheetNames, name => name.Contains("_Raw"));
        Assert.DoesNotContain(worksheetNames, name => name.Contains("_Transformed"));
        
        // Should have regular table names
        Assert.Contains(worksheetNames, name => name == "Customers" || name == "Products");
    }

    [Fact]
    public void Export_RawOnly_ShouldExportOnlyRawData()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);

        var transformationConfig = new TransformationConfig
        {
            Global = new GlobalSettings { EnableTransformations = true },
            GlobalTransformers = new List<TransformerConfig>
            {
                new TransformerConfig
                {
                    Name = "upper",
                    Config = new Dictionary<string, string>()
                }
            }
        };

        var options = new SqliteToExcelOptions
        {
            DualExportStrategy = DualExportStrategy.RawOnly,
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        Assert.True(File.Exists(xlsxPath), "Excel file should be created");
        
        using var workbook = new XLWorkbook(xlsxPath);
        var worksheetNames = workbook.Worksheets.Select(ws => ws.Name).ToList();
        
        // Should not have any "_Raw" or "_Transformed" suffixes - just table names
        Assert.DoesNotContain(worksheetNames, name => name.Contains("_Raw"));
        Assert.DoesNotContain(worksheetNames, name => name.Contains("_Transformed"));
        
        // Data should be raw/untransformed (this is implicit - no transformations applied)
        Assert.Contains(worksheetNames, name => name == "Customers" || name == "Products");
    }

    [Fact]
    public void Export_DualSheets_ShouldCreateBothRawAndTransformedSheets()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);

        var transformationConfig = new TransformationConfig
        {
            Global = new GlobalSettings { EnableTransformations = true },
            GlobalTransformers = new List<TransformerConfig>
            {
                new TransformerConfig
                {
                    Name = "coalesce",
                    Config = new Dictionary<string, string> { ["default"] = "N/A" }
                }
            }
        };

        var options = new SqliteToExcelOptions
        {
            DualExportStrategy = DualExportStrategy.DualSheets,
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault(),
            RawDataSuffix = "_Raw",
            TransformedDataSuffix = "_Transformed"
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        Assert.True(File.Exists(xlsxPath), "Excel file should be created");
        
        using var workbook = new XLWorkbook(xlsxPath);
        var worksheetNames = workbook.Worksheets.Select(ws => ws.Name).ToList();
        
        // Should have both raw and transformed sheets for each table
        Assert.Contains(worksheetNames, name => name.Contains("_Raw"));
        Assert.Contains(worksheetNames, name => name.Contains("_Transformed"));
        
        // Should have pairs of sheets for the same table
        var customersRaw = worksheetNames.FirstOrDefault(name => name.StartsWith("Customers") && name.Contains("_Raw"));
        var customersTransformed = worksheetNames.FirstOrDefault(name => name.StartsWith("Customers") && name.Contains("_Transformed"));
        
        Assert.NotNull(customersRaw);
        Assert.NotNull(customersTransformed);
    }

    [Fact]
    public void Export_DualWorkbooks_ShouldCreateSeparateFiles()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);
        var transformedPath = GetExpectedTransformedPath(xlsxPath);
        _tempFiles.Add(transformedPath);

        var transformationConfig = new TransformationConfig
        {
            Global = new GlobalSettings { EnableTransformations = true },
            GlobalTransformers = new List<TransformerConfig>
            {
                new TransformerConfig
                {
                    Name = "coalesce",
                    Config = new Dictionary<string, string> { ["default"] = "N/A" }
                }
            }
        };

        var options = new SqliteToExcelOptions
        {
            DualExportStrategy = DualExportStrategy.DualWorkbooks,
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        Assert.True(File.Exists(xlsxPath), "Original Excel file should be created");
        Assert.True(File.Exists(transformedPath), "Transformed Excel file should be created");
        
        // Check that both files are valid Excel files
        using var rawWorkbook = new XLWorkbook(xlsxPath);
        using var transformedWorkbook = new XLWorkbook(transformedPath);
        
        // Both should have similar structure but different data
        var rawSheetNames = rawWorkbook.Worksheets.Select(ws => ws.Name).ToHashSet();
        var transformedSheetNames = transformedWorkbook.Worksheets.Select(ws => ws.Name).ToHashSet();
        
        // Should have similar sheet structure (excluding metadata differences)
        var commonSheets = rawSheetNames.Intersect(transformedSheetNames).ToList();
        Assert.True(commonSheets.Count > 0, "Should have common table sheets");
        
        // Neither should have dual sheet suffixes
        Assert.DoesNotContain(rawSheetNames, name => name.Contains("_Raw") || name.Contains("_Transformed"));
        Assert.DoesNotContain(transformedSheetNames, name => name.Contains("_Raw") || name.Contains("_Transformed"));
    }

    [Fact]
    public void Export_DualSheets_WithCustomSuffixes_ShouldUseCustomSuffixes()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);

        var transformationConfig = new TransformationConfig
        {
            Global = new GlobalSettings { EnableTransformations = true },
            GlobalTransformers = new List<TransformerConfig>
            {
                new TransformerConfig
                {
                    Name = "coalesce",
                    Config = new Dictionary<string, string> { ["default"] = "N/A" }
                }
            }
        };

        var options = new SqliteToExcelOptions
        {
            DualExportStrategy = DualExportStrategy.DualSheets,
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault(),
            RawDataSuffix = "_Original",
            TransformedDataSuffix = "_Processed"
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        using var workbook = new XLWorkbook(xlsxPath);
        var worksheetNames = workbook.Worksheets.Select(ws => ws.Name).ToList();
        
        // Should use custom suffixes
        Assert.Contains(worksheetNames, name => name.Contains("_Original"));
        Assert.Contains(worksheetNames, name => name.Contains("_Processed"));
        
        // Should not use default suffixes
        Assert.DoesNotContain(worksheetNames, name => name.Contains("_Raw"));
        Assert.DoesNotContain(worksheetNames, name => name.Contains("_Transformed"));
    }

    [Fact]
    public void Export_DualSheets_WithoutTransformations_ShouldOnlyCreateRawSheets()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);

        var options = new SqliteToExcelOptions
        {
            DualExportStrategy = DualExportStrategy.DualSheets,
            // No transformation config provided
            RawDataSuffix = "_Raw",
            TransformedDataSuffix = "_Transformed"
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        using var workbook = new XLWorkbook(xlsxPath);
        var worksheetNames = workbook.Worksheets.Select(ws => ws.Name).ToList();
        
        // Should only have raw sheets since no transformations are configured
        Assert.Contains(worksheetNames, name => name.Contains("_Raw"));
        Assert.DoesNotContain(worksheetNames, name => name.Contains("_Transformed"));
    }

    [Fact]
    public void Export_DualWorkbooks_WithoutTransformations_ShouldOnlyCreateOriginalFile()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempFiles.Add(dbPath);
        var xlsxPath = Path.ChangeExtension(dbPath, ".xlsx");
        _tempFiles.Add(xlsxPath);
        var transformedPath = GetExpectedTransformedPath(xlsxPath);
        _tempFiles.Add(transformedPath);

        var options = new SqliteToExcelOptions
        {
            DualExportStrategy = DualExportStrategy.DualWorkbooks
            // No transformation config provided
        };

        // Act
        SqliteToExcel.Export(dbPath, xlsxPath, options);

        // Assert
        Assert.True(File.Exists(xlsxPath), "Original Excel file should be created");
        
        // Transformed file should not be created since no transformations are configured
        Assert.False(File.Exists(transformedPath), "Transformed Excel file should not be created without transformation config");
    }

    private static string GetExpectedTransformedPath(string originalPath)
    {
        var directory = Path.GetDirectoryName(originalPath) ?? "";
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalPath);
        var extension = Path.GetExtension(originalPath);
        
        return Path.Combine(directory, $"{fileNameWithoutExtension}_Transformed{extension}");
    }
}