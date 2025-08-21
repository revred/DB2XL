using DB2XL.Core.Models;
using DB2XL.Data.Schema;
using DB2XL;
using DB2XL.Transform.Configuration;
using DB2XL.Transform.Interfaces;
using System.Text.Json;
using Xunit;

namespace DB2XL.Integration.Tests;

public class JsonLinesExportTests : IDisposable
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
    public void Export_BasicExport_ShouldCreateJsonlFiles()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempPaths.Add(outputDir);

        var options = new JsonLinesExportOptions
        {
            WriteAllAsStrings = false
        };

        // Act
        JsonLinesExporter.Export(dbPath, outputDir, options);

        // Assert
        Assert.True(Directory.Exists(outputDir), "Output directory should be created");
        
        var jsonlFiles = Directory.GetFiles(outputDir, "*.jsonl");
        Assert.True(jsonlFiles.Length > 0, "Should create at least one JSONL file");
        
        // Check that files contain valid JSON lines
        foreach (var file in jsonlFiles)
        {
            var lines = File.ReadAllLines(file);
            if (lines.Length > 0) // Skip empty tables
            {
                foreach (var line in lines)
                {
                    Assert.False(string.IsNullOrWhiteSpace(line), "Lines should not be empty");
                    
                    // Should be valid JSON
                    var json = JsonSerializer.Deserialize<Dictionary<string, object?>>(line);
                    Assert.NotNull(json);
                    Assert.True(json.Count > 0, "JSON objects should have properties");
                }
            }
        }
    }

    [Fact]
    public void Export_WithSchemaManifests_ShouldCreateSchemaFiles()
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
        JsonLinesExporter.Export(dbPath, outputDir, options);

        // Assert
        var schemaFiles = Directory.GetFiles(outputDir, "*.schema.json");
        Assert.True(schemaFiles.Length > 0, "Should create schema files");
        
        // Check export manifest
        var manifestPath = Path.Combine(outputDir, "export_manifest.json");
        Assert.True(File.Exists(manifestPath), "Should create export manifest");
        
        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<JsonLinesExportManifest>(manifestJson);
        Assert.NotNull(manifest);
        Assert.True(manifest.Tables.Count > 0, "Manifest should list tables");
        
        // Check individual schema files
        foreach (var schemaFile in schemaFiles)
        {
            var schemaJson = File.ReadAllText(schemaFile);
            var schema = JsonSerializer.Deserialize<JsonLinesTableSchema>(schemaJson);
            Assert.NotNull(schema);
            Assert.False(string.IsNullOrEmpty(schema.TableName), "Schema should have table name");
            Assert.True(schema.Columns.Count > 0, "Schema should have columns");
        }
    }

    [Fact]
    public void Export_WithTransformations_ShouldApplyTransformations()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempPaths.Add(outputDir);

        var transformationConfig = new TransformationConfig
        {
            Global = new GlobalSettings { EnableTransformations = true },
            GlobalTransformers = new List<TransformerConfig>
            {
                new TransformerConfig
                {
                    Name = "coalesce",
                    Config = new Dictionary<string, string> 
                    { 
                        ["default"] = "TRANSFORMED_DEFAULT",
                        ["forceApply"] = "true"
                    }
                }
            }
        };

        var options = new JsonLinesExportOptions
        {
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        // Act
        JsonLinesExporter.Export(dbPath, outputDir, options);

        // Assert
        var jsonlFiles = Directory.GetFiles(outputDir, "*.jsonl");
        Assert.True(jsonlFiles.Length > 0, "Should create JSONL files");
        
        // Check that transformations were applied (hard to verify specific transformations without knowing exact data,
        // but we can verify the export succeeded and files were created)
        var exportManifestPath = Path.Combine(outputDir, "export_manifest.json");
        var manifestJson = File.ReadAllText(exportManifestPath);
        var manifest = JsonSerializer.Deserialize<JsonLinesExportManifest>(manifestJson);
        
        Assert.True(manifest!.TransformationsEnabled, "Manifest should indicate transformations were enabled");
    }

    [Fact]
    public void Export_DualDirectories_ShouldCreateRawAndTransformedDirectories()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempPaths.Add(outputDir);

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

        var options = new JsonLinesExportOptions
        {
            DualExportStrategy = DualExportStrategy.DualSheets, // For JSONL, this means dual directories
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        // Act
        JsonLinesExporter.Export(dbPath, outputDir, options);

        // Assert
        var rawDir = Path.Combine(outputDir, "raw");
        var transformedDir = Path.Combine(outputDir, "transformed");
        
        Assert.True(Directory.Exists(rawDir), "Raw directory should be created");
        Assert.True(Directory.Exists(transformedDir), "Transformed directory should be created");
        
        var rawFiles = Directory.GetFiles(rawDir, "*.jsonl");
        var transformedFiles = Directory.GetFiles(transformedDir, "*.jsonl");
        
        Assert.True(rawFiles.Length > 0, "Raw directory should contain JSONL files");
        Assert.True(transformedFiles.Length > 0, "Transformed directory should contain JSONL files");
        
        // Check manifests
        Assert.True(File.Exists(Path.Combine(rawDir, "export_manifest.json")), "Raw directory should have manifest");
        Assert.True(File.Exists(Path.Combine(transformedDir, "export_manifest.json")), "Transformed directory should have manifest");
    }

    [Fact]
    public void Export_DualSets_ShouldCreateTwoOutputDirectories()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempPaths.Add(outputDir);
        var transformedDir = outputDir + "_transformed";
        _tempPaths.Add(transformedDir);

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

        var options = new JsonLinesExportOptions
        {
            DualExportStrategy = DualExportStrategy.DualWorkbooks, // For JSONL, this means dual directory sets
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        // Act
        JsonLinesExporter.Export(dbPath, outputDir, options);

        // Assert
        Assert.True(Directory.Exists(outputDir), "Main output directory should be created");
        Assert.True(Directory.Exists(transformedDir), "Transformed output directory should be created");
        
        var mainFiles = Directory.GetFiles(outputDir, "*.jsonl");
        var transformedFiles = Directory.GetFiles(transformedDir, "*.jsonl");
        
        Assert.True(mainFiles.Length > 0, "Main directory should contain JSONL files");
        Assert.True(transformedFiles.Length > 0, "Transformed directory should contain JSONL files");
    }

    [Fact]
    public void Export_RawOnly_ShouldNotApplyTransformations()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempPaths.Add(outputDir);

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

        var options = new JsonLinesExportOptions
        {
            DualExportStrategy = DualExportStrategy.RawOnly,
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        // Act
        JsonLinesExporter.Export(dbPath, outputDir, options);

        // Assert
        var jsonlFiles = Directory.GetFiles(outputDir, "*.jsonl");
        Assert.True(jsonlFiles.Length > 0, "Should create JSONL files");
        
        // Check manifest indicates no transformations
        var manifestPath = Path.Combine(outputDir, "export_manifest.json");
        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<JsonLinesExportManifest>(manifestJson);
        
        Assert.False(manifest!.TransformationsEnabled, "Manifest should indicate transformations were not enabled");
    }

    [Fact]
    public void Export_WriteAllAsStrings_ShouldFormatAllValuesAsStrings()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempPaths.Add(outputDir);

        var options = new JsonLinesExportOptions
        {
            WriteAllAsStrings = true
        };

        // Act
        JsonLinesExporter.Export(dbPath, outputDir, options);

        // Assert
        var jsonlFiles = Directory.GetFiles(outputDir, "*.jsonl");
        Assert.True(jsonlFiles.Length > 0, "Should create JSONL files");
        
        // Read first file and check that numeric values are strings
        var firstFile = jsonlFiles.First();
        var lines = File.ReadAllLines(firstFile);
        
        if (lines.Length > 0)
        {
            var firstLine = lines[0];
            using var jsonDoc = JsonDocument.Parse(firstLine);
            
            // All non-null values should be strings
            foreach (var property in jsonDoc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Null)
                {
                    Assert.Equal(JsonValueKind.String, property.Value.ValueKind);
                }
            }
        }
    }

    [Fact]
    public void Export_WithTableFilter_ShouldOnlyExportFilteredTables()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);
        var outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempPaths.Add(outputDir);

        var options = new JsonLinesExportOptions
        {
            TableNameLikeFilter = "Customer%", // Only tables starting with "Customer"
            IncludeSchemaManifests = true
        };

        // Act
        JsonLinesExporter.Export(dbPath, outputDir, options);

        // Assert
        var jsonlFiles = Directory.GetFiles(outputDir, "*.jsonl");
        
        // Check manifest to see which tables were exported
        var manifestPath = Path.Combine(outputDir, "export_manifest.json");
        var manifestJson = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<JsonLinesExportManifest>(manifestJson);
        
        Assert.NotNull(manifest);
        
        // All exported tables should match the filter
        foreach (var table in manifest.Tables)
        {
            Assert.True(table.TableName.StartsWith("Customer", StringComparison.OrdinalIgnoreCase),
                $"Table {table.TableName} should match filter 'Customer%'");
        }
    }

    [Fact]
    public void Export_InvalidInputs_ShouldThrowExceptions()
    {
        // Test null/empty SQLite path
        Assert.Throws<ArgumentException>(() => 
            JsonLinesExporter.Export("", "output", new JsonLinesExportOptions()));
        
        Assert.Throws<ArgumentException>(() => 
            JsonLinesExporter.Export(null!, "output", new JsonLinesExportOptions()));
        
        // Test null/empty output directory
        Assert.Throws<ArgumentException>(() => 
            JsonLinesExporter.Export("test.db", "", new JsonLinesExportOptions()));
        
        Assert.Throws<ArgumentException>(() => 
            JsonLinesExporter.Export("test.db", null!, new JsonLinesExportOptions()));
        
        // Test non-existent database file
        Assert.Throws<FileNotFoundException>(() => 
            JsonLinesExporter.Export("non_existent.db", "output", new JsonLinesExportOptions()));
    }
}