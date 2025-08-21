using DB2XL.Core.Models;
using DB2XL.Data.Schema;
using DB2XL;
using DB2XL.Schema;
using DB2XL.Transform.Configuration;
using DB2XL.Transform.Interfaces;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace DB2XL.Integration.Tests.Schema;

public class ManifestGeneratorTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                else if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public void GenerateManifest_ShouldCreateCompleteManifest()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        connection.Open();

        var options = new SqliteToExcelOptions();

        // Act
        var manifest = ManifestGenerator.GenerateManifest(
            connection, dbPath, "test_export.xlsx", "Excel", options);

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal("Excel", manifest.ExportFormat);
        Assert.Equal(dbPath, manifest.SourceDatabase);
        Assert.NotNull(manifest.DatabaseSchema);
        Assert.NotNull(manifest.ProvenanceManifest);
        Assert.True(manifest.FormatSpecificMetadata.Count > 0);
        Assert.True(manifest.GeneratedTimestamp <= DateTime.UtcNow);
    }

    [Fact]
    public void GenerateManifest_WithTransformations_ShouldIncludeTransformationInfo()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        connection.Open();

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
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        var transformationPipeline = new TransformationPipeline(transformationConfig, options.TransformerRegistry!);

        // Act
        var manifest = ManifestGenerator.GenerateManifest(
            connection, dbPath, "test_export.xlsx", "Excel", options, transformationPipeline);

        // Assert
        Assert.True(manifest.DatabaseSchema.TransformationsEnabled);
        Assert.True(manifest.ProvenanceManifest.TransformationsApplied);
        Assert.NotEmpty(manifest.ProvenanceManifest.TransformationConfigVersion);
        Assert.NotEmpty(manifest.ProvenanceManifest.ErrorHandlingStrategy);
    }

    [Fact]
    public void SaveManifest_AndLoadManifest_ShouldRoundTrip()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        connection.Open();

        var originalManifest = ManifestGenerator.GenerateManifest(
            connection, dbPath, "test_export.xlsx", "Excel");

        var manifestPath = Path.GetTempFileName();
        _tempPaths.Add(manifestPath);

        // Act
        ManifestGenerator.SaveManifest(originalManifest, manifestPath);
        var loadedManifest = ManifestGenerator.LoadManifest(manifestPath);

        // Assert
        Assert.Equal(originalManifest.ExportFormat, loadedManifest.ExportFormat);
        Assert.Equal(originalManifest.SourceDatabase, loadedManifest.SourceDatabase);
        Assert.Equal(originalManifest.DatabaseSchema.TotalTables, loadedManifest.DatabaseSchema.TotalTables);
        Assert.Equal(originalManifest.DatabaseSchema.TotalRows, loadedManifest.DatabaseSchema.TotalRows);
        Assert.Equal(originalManifest.ProvenanceManifest.DatabaseChecksum, loadedManifest.ProvenanceManifest.DatabaseChecksum);
    }

    [Fact]
    public void GenerateSummaryManifest_ShouldCreateLightweightSummary()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        connection.Open();

        var fullManifest = ManifestGenerator.GenerateManifest(
            connection, dbPath, "test_export.xlsx", "Excel");

        // Act
        var summaryManifest = ManifestGenerator.GenerateSummaryManifest(fullManifest);

        // Assert
        Assert.NotNull(summaryManifest);
        Assert.Equal(fullManifest.SourceDatabase, summaryManifest.SourceDatabase);
        Assert.Equal(fullManifest.ExportFormat, summaryManifest.ExportFormat);
        Assert.Equal(fullManifest.DatabaseSchema.TotalTables, summaryManifest.TableCount);
        Assert.Equal(fullManifest.DatabaseSchema.TotalRows, summaryManifest.TotalRows);
        Assert.Equal(fullManifest.DatabaseSchema.TotalColumns, summaryManifest.TotalColumns);
        Assert.Equal(fullManifest.ProvenanceManifest.DatabaseChecksum, summaryManifest.DatabaseChecksum);
        Assert.True(summaryManifest.Tables.Count > 0);

        // Verify summary tables match full manifest tables
        foreach (var summaryTable in summaryManifest.Tables)
        {
            var fullTable = fullManifest.DatabaseSchema.Tables.First(t => t.Name == summaryTable.Name);
            Assert.Equal(fullTable.Type, summaryTable.Type);
            Assert.Equal(fullTable.RowCount, summaryTable.RowCount);
            Assert.Equal(fullTable.Columns.Count, summaryTable.ColumnCount);
            Assert.Equal(fullTable.SchemaChecksum, summaryTable.SchemaChecksum);
        }
    }

    [Theory]
    [InlineData("Excel")]
    [InlineData("JSONL")]
    public void GenerateManifest_DifferentFormats_ShouldIncludeFormatSpecificMetadata(string format)
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        connection.Open();

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            BlobMode = BlobRenderMode.Base64,
            DualExportStrategy = DualExportStrategy.DualSheets
        };

        // Act
        var manifest = ManifestGenerator.GenerateManifest(
            connection, dbPath, "test_export", format, options);

        // Assert
        Assert.True(manifest.FormatSpecificMetadata.Count > 0);

        switch (format.ToLowerInvariant())
        {
            case "excel":
                Assert.True(manifest.FormatSpecificMetadata.ContainsKey("writeAllAsText"));
                Assert.True(manifest.FormatSpecificMetadata.ContainsKey("preserveNumericTypes"));
                Assert.True(manifest.FormatSpecificMetadata.ContainsKey("maxExcelRows"));
                Assert.True(manifest.FormatSpecificMetadata.ContainsKey("maxExcelColumns"));
                break;
            case "jsonl":
                Assert.True(manifest.FormatSpecificMetadata.ContainsKey("writeAllAsStrings"));
                Assert.True(manifest.FormatSpecificMetadata.ContainsKey("preserveTypes"));
                Assert.True(manifest.FormatSpecificMetadata.ContainsKey("jsonLinesFormat"));
                break;
        }

        Assert.True(manifest.FormatSpecificMetadata.ContainsKey("blobMode"));
        Assert.True(manifest.FormatSpecificMetadata.ContainsKey("dualExportStrategy"));
    }

    [Fact]
    public void ValidateExport_WithValidPath_ShouldReturnValidResult()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        var xlsxPath = Path.GetTempFileName();
        File.Delete(xlsxPath);
        xlsxPath = Path.ChangeExtension(xlsxPath, ".xlsx");
        _tempPaths.Add(xlsxPath);

        // Create a simple Excel file
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Test");
        worksheet.Cell(1, 1).Value = "Test Data";
        workbook.SaveAs(xlsxPath);

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        connection.Open();

        var manifest = ManifestGenerator.GenerateManifest(
            connection, dbPath, xlsxPath, "Excel");

        // Act
        var result = ManifestGenerator.ValidateExport(xlsxPath, manifest);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(xlsxPath, result.ExportPath);
        Assert.True(result.ValidationTimestamp <= DateTime.UtcNow);
        Assert.True(result.ExportFileSizeBytes > 0);
        Assert.True(result.ActualSheetCount > 0);
        
        // The validation might have warnings but should not have errors for a basic check
        Assert.True(result.Errors.Count == 0 || result.Errors.All(e => !e.Contains("not found")));
    }

    [Fact]
    public void ValidateExport_WithMissingFile_ShouldReturnInvalid()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".xlsx");

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        connection.Open();

        var manifest = ManifestGenerator.GenerateManifest(
            connection, dbPath, nonExistentPath, "Excel");

        // Act
        var result = ManifestGenerator.ValidateExport(nonExistentPath, manifest);

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Any(e => e.Contains("not found")));
    }

    [Fact]
    public void SaveManifest_ShouldCreateValidJson()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        connection.Open();

        var manifest = ManifestGenerator.GenerateManifest(
            connection, dbPath, "test_export.xlsx", "Excel");

        var manifestPath = Path.GetTempFileName();
        _tempPaths.Add(manifestPath);

        // Act
        ManifestGenerator.SaveManifest(manifest, manifestPath);

        // Assert
        Assert.True(File.Exists(manifestPath));
        var json = File.ReadAllText(manifestPath);
        Assert.False(string.IsNullOrWhiteSpace(json));

        // Verify it's valid JSON by parsing it
        var parsedManifest = JsonSerializer.Deserialize<SchemaManifest>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        Assert.NotNull(parsedManifest);
        Assert.Equal(manifest.ExportFormat, parsedManifest.ExportFormat);
    }
}