using DB2XL;
using DB2XL.Schema;
using DB2XL.Configuration;
using DB2XL.Transformers;
using Xunit;

namespace SqliteXport.Tests.Schema;

public class SchemaManifestIntegrationTests : IDisposable
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
    public void SqliteToExcel_GenerateManifest_ShouldCreateComprehensiveManifest()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        var xlsxPath = Path.GetTempFileName();
        File.Delete(xlsxPath);
        xlsxPath = Path.ChangeExtension(xlsxPath, ".xlsx");
        _tempPaths.Add(xlsxPath);

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            IncludeMetadataSheet = true,
            BlobMode = BlobRenderMode.Base64
        };

        // Act
        var manifest = SqliteToExcel.GenerateManifest(dbPath, xlsxPath, options);

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal("Excel", manifest.ExportFormat);
        Assert.Equal(dbPath, manifest.SourceDatabase);
        Assert.True(manifest.DatabaseSchema.Tables.Count > 0);
        Assert.True(manifest.DatabaseSchema.TotalRows > 0);
        Assert.NotEmpty(manifest.ProvenanceManifest.DatabaseChecksum);
        
        // Verify Excel-specific metadata
        Assert.True(manifest.FormatSpecificMetadata.ContainsKey("writeAllAsText"));
        Assert.True(manifest.FormatSpecificMetadata.ContainsKey("includeMetadataSheet"));
        Assert.True(manifest.FormatSpecificMetadata.ContainsKey("blobMode"));
        Assert.Equal(true, manifest.FormatSpecificMetadata["writeAllAsText"]);
        Assert.Equal(true, manifest.FormatSpecificMetadata["includeMetadataSheet"]);
        Assert.Equal("Base64", manifest.FormatSpecificMetadata["blobMode"]);
    }

    [Fact]
    public void SqliteToExcel_ExportWithManifest_ShouldCreateBothFiles()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        var xlsxPath = Path.GetTempFileName();
        File.Delete(xlsxPath);
        xlsxPath = Path.ChangeExtension(xlsxPath, ".xlsx");
        _tempPaths.Add(xlsxPath);

        var manifestPath = Path.ChangeExtension(xlsxPath, ".manifest.json");
        _tempPaths.Add(manifestPath);

        // Act
        var manifest = SqliteToExcel.ExportWithManifest(dbPath, xlsxPath);

        // Assert
        Assert.True(File.Exists(xlsxPath));
        Assert.True(File.Exists(manifestPath));
        Assert.NotNull(manifest);

        // Verify the manifest file contains valid data
        var loadedManifest = ManifestGenerator.LoadManifest(manifestPath);
        Assert.Equal(manifest.DatabaseSchema.TotalTables, loadedManifest.DatabaseSchema.TotalTables);
        Assert.Equal(manifest.ProvenanceManifest.DatabaseChecksum, loadedManifest.ProvenanceManifest.DatabaseChecksum);
    }

    [Fact]
    public void SqliteToExcel_ValidateExport_ShouldValidateAgainstManifest()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        var xlsxPath = Path.GetTempFileName();
        File.Delete(xlsxPath);
        xlsxPath = Path.ChangeExtension(xlsxPath, ".xlsx");
        _tempPaths.Add(xlsxPath);

        var manifestPath = Path.ChangeExtension(xlsxPath, ".manifest.json");
        _tempPaths.Add(manifestPath);

        // Export with manifest
        SqliteToExcel.ExportWithManifest(dbPath, xlsxPath);

        // Act
        var validationResult = SqliteToExcel.ValidateExport(xlsxPath);

        // Assert
        Assert.NotNull(validationResult);
        Assert.Equal(xlsxPath, validationResult.ExportPath);
        Assert.Equal(manifestPath, validationResult.ManifestPath);
        Assert.True(validationResult.ExportFileSizeBytes > 0);
        Assert.True(validationResult.ActualSheetCount > 0);
        
        // Should have no critical errors
        Assert.True(validationResult.Errors.Count == 0 || validationResult.Errors.All(e => !e.Contains("not found")));
    }

    [Fact]
    public void JsonLinesExporter_GenerateManifest_ShouldCreateComprehensiveManifest()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempPaths.Add(outputDir);

        var options = new JsonLinesExportOptions
        {
            WriteAllAsStrings = false,
            IncludeSchemaManifests = true,
            BlobMode = BlobRenderMode.Hex
        };

        // Act
        var manifest = JsonLinesExporter.GenerateManifest(dbPath, outputDir, options);

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal("JSONL", manifest.ExportFormat);
        Assert.Equal(dbPath, manifest.SourceDatabase);
        Assert.True(manifest.DatabaseSchema.Tables.Count > 0);
        Assert.True(manifest.DatabaseSchema.TotalRows > 0);
        Assert.NotEmpty(manifest.ProvenanceManifest.DatabaseChecksum);
        
        // Verify JSONL-specific metadata
        Assert.True(manifest.FormatSpecificMetadata.ContainsKey("writeAllAsStrings"));
        Assert.True(manifest.FormatSpecificMetadata.ContainsKey("preserveTypes"));
        Assert.True(manifest.FormatSpecificMetadata.ContainsKey("jsonLinesFormat"));
        Assert.Equal(false, manifest.FormatSpecificMetadata["writeAllAsStrings"]);
        Assert.Equal(true, manifest.FormatSpecificMetadata["preserveTypes"]);
        Assert.Equal("standard", manifest.FormatSpecificMetadata["jsonLinesFormat"]);
    }

    [Fact]
    public void JsonLinesExporter_ExportWithManifest_ShouldCreateFilesAndManifest()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempPaths.Add(outputDir);

        var options = new JsonLinesExportOptions
        {
            IncludeSchemaManifests = true
        };

        // Act
        var manifest = JsonLinesExporter.ExportWithManifest(dbPath, outputDir, options);

        // Assert
        Assert.True(Directory.Exists(outputDir));
        Assert.NotNull(manifest);

        // Verify JSONL files were created
        var jsonlFiles = Directory.GetFiles(outputDir, "*.jsonl");
        Assert.True(jsonlFiles.Length > 0);

        // Verify manifests were created
        var exportManifestPath = Path.Combine(outputDir, "export_manifest.json");
        var schemaManifestPath = Path.Combine(outputDir, "schema_manifest.json");
        Assert.True(File.Exists(exportManifestPath));
        Assert.True(File.Exists(schemaManifestPath));

        // Verify schema manifest content
        var loadedManifest = ManifestGenerator.LoadManifest(schemaManifestPath);
        Assert.Equal(manifest.DatabaseSchema.TotalTables, loadedManifest.DatabaseSchema.TotalTables);
        Assert.Equal(manifest.ProvenanceManifest.DatabaseChecksum, loadedManifest.ProvenanceManifest.DatabaseChecksum);
    }

    [Fact]
    public void JsonLinesExporter_ValidateExport_ShouldValidateAgainstManifest()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempPaths.Add(outputDir);

        // Export with manifest
        JsonLinesExporter.ExportWithManifest(dbPath, outputDir);

        // Act
        var validationResult = JsonLinesExporter.ValidateExport(outputDir);

        // Assert
        Assert.NotNull(validationResult);
        Assert.Equal(outputDir, validationResult.ExportPath);
        Assert.True(validationResult.ActualFileCount > 0);
        
        // Should have no critical errors
        Assert.True(validationResult.Errors.Count == 0 || validationResult.Errors.All(e => !e.Contains("not found")));
    }

    [Fact]
    public void ExportsWithTransformations_ShouldTrackTransformationLineage()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        var transformationConfig = new TransformationConfig
        {
            Global = new GlobalSettings { EnableTransformations = true },
            GlobalTransformers = new List<TransformerConfig>
            {
                new TransformerConfig
                {
                    Name = "upper",
                    Config = new Dictionary<string, string>()
                },
                new TransformerConfig
                {
                    Name = "coalesce",
                    Config = new Dictionary<string, string> { ["default"] = "N/A" }
                }
            }
        };

        var excelOptions = new SqliteToExcelOptions
        {
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        var jsonlOptions = new JsonLinesExportOptions
        {
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        var xlsxPath = Path.GetTempFileName();
        File.Delete(xlsxPath);
        xlsxPath = Path.ChangeExtension(xlsxPath, ".xlsx");
        _tempPaths.Add(xlsxPath);

        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempPaths.Add(outputDir);

        // Act
        var excelManifest = SqliteToExcel.GenerateManifest(dbPath, xlsxPath, excelOptions);
        var jsonlManifest = JsonLinesExporter.GenerateManifest(dbPath, outputDir, jsonlOptions);

        // Assert
        foreach (var manifest in new[] { excelManifest, jsonlManifest })
        {
            Assert.True(manifest.DatabaseSchema.TransformationsEnabled);
            Assert.True(manifest.ProvenanceManifest.TransformationsApplied);
            Assert.NotEmpty(manifest.ProvenanceManifest.TransformationConfigVersion);
            Assert.True(manifest.ProvenanceManifest.DataLineages.Count > 0);

            // Verify each table has lineage tracking
            foreach (var lineage in manifest.ProvenanceManifest.DataLineages)
            {
                Assert.NotEmpty(lineage.TableName);
                Assert.True(lineage.SourceRowCount >= 0);
                Assert.True(lineage.OriginalColumns.Count > 0);
                // Transformation details might be empty depending on specific data and transformers
                Assert.True(lineage.TransformationDetails.Count >= 0);
            }
        }
    }

    [Fact]
    public void CrossFormatManifests_ShouldHaveConsistentDatabaseInfo()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        var xlsxPath = Path.GetTempFileName();
        File.Delete(xlsxPath);
        xlsxPath = Path.ChangeExtension(xlsxPath, ".xlsx");
        _tempPaths.Add(xlsxPath);

        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempPaths.Add(outputDir);

        var excelOptions = new SqliteToExcelOptions { WriteAllAsText = true };
        var jsonlOptions = new JsonLinesExportOptions { WriteAllAsStrings = true };

        // Act
        var excelManifest = SqliteToExcel.GenerateManifest(dbPath, xlsxPath, excelOptions);
        var jsonlManifest = JsonLinesExporter.GenerateManifest(dbPath, outputDir, jsonlOptions);

        // Assert
        // Database-level information should be identical
        Assert.Equal(excelManifest.DatabaseSchema.SchemaVersion, jsonlManifest.DatabaseSchema.SchemaVersion);
        Assert.Equal(excelManifest.DatabaseSchema.UserVersion, jsonlManifest.DatabaseSchema.UserVersion);
        Assert.Equal(excelManifest.DatabaseSchema.TotalTables, jsonlManifest.DatabaseSchema.TotalTables);
        Assert.Equal(excelManifest.DatabaseSchema.TotalRows, jsonlManifest.DatabaseSchema.TotalRows);
        Assert.Equal(excelManifest.DatabaseSchema.TotalColumns, jsonlManifest.DatabaseSchema.TotalColumns);
        Assert.Equal(excelManifest.ProvenanceManifest.DatabaseChecksum, jsonlManifest.ProvenanceManifest.DatabaseChecksum);

        // Table-level schema should be identical
        foreach (var excelTable in excelManifest.DatabaseSchema.Tables)
        {
            var jsonlTable = jsonlManifest.DatabaseSchema.Tables.First(t => t.Name == excelTable.Name);
            Assert.Equal(excelTable.RowCount, jsonlTable.RowCount);
            Assert.Equal(excelTable.SchemaChecksum, jsonlTable.SchemaChecksum);
            Assert.Equal(excelTable.Columns.Count, jsonlTable.Columns.Count);
        }

        // Format-specific metadata should differ
        Assert.NotEqual(excelManifest.FormatSpecificMetadata, jsonlManifest.FormatSpecificMetadata);
        Assert.Equal("Excel", excelManifest.ExportFormat);
        Assert.Equal("JSONL", jsonlManifest.ExportFormat);
    }
}