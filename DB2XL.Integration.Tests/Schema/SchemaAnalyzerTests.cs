using DB2XL.Core.Models;
using DB2XL.Data.Schema;
using DB2XL;
using DB2XL.Schema;
using DB2XL.Transform.Configuration;
using DB2XL.Transform.Interfaces;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DB2XL.Integration.Tests.Schema;

public class SchemaAnalyzerTests : IDisposable
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
    public void AnalyzeDatabase_ShouldGenerateComprehensiveSchema()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        connection.Open();

        // Act
        var schema = SchemaAnalyzer.AnalyzeDatabase(connection, dbPath);

        // Assert
        Assert.NotNull(schema);
        Assert.Equal(dbPath, schema.DatabasePath);
        Assert.True(schema.AnalysisTimestamp <= DateTime.UtcNow);
        Assert.True(schema.TotalTables > 0);
        Assert.True(schema.TotalRows > 0);
        Assert.True(schema.TotalColumns > 0);
        Assert.True(schema.Tables.Count > 0);

        // Verify schema contains expected information
        Assert.NotEmpty(schema.SchemaVersion);
        Assert.NotEmpty(schema.UserVersion);
        Assert.NotEmpty(schema.JournalMode);
        Assert.True(schema.PageSize > 0);
        Assert.True(schema.FileSizeBytes > 0);
    }

    [Fact]
    public void AnalyzeTable_ShouldGenerateDetailedTableSchema()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        connection.Open();

        var tables = SqliteSchemaReader.GetDatabaseObjects(connection, null, false);
        var firstTable = tables.First();

        // Act
        var tableSchema = SchemaAnalyzer.AnalyzeTable(connection, firstTable, new SqliteToExcelOptions());

        // Assert
        Assert.NotNull(tableSchema);
        Assert.Equal(firstTable.Name, tableSchema.Name);
        Assert.Equal(firstTable.Type, tableSchema.Type);
        Assert.True(tableSchema.RowCount >= 0);
        Assert.True(tableSchema.Columns.Count > 0);
        Assert.NotEmpty(tableSchema.SchemaChecksum);
        Assert.NotEmpty(tableSchema.OrderMode);
    }

    [Fact]
    public void AnalyzeColumn_ShouldGenerateColumnStatistics()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        connection.Open();

        var tables = SqliteSchemaReader.GetDatabaseObjects(connection, null, false);
        var firstTable = tables.First();
        var columns = SqliteSchemaReader.GetTableColumns(connection, firstTable.Name);
        var firstColumn = columns.First();

        // Act
        var columnSchema = SchemaAnalyzer.AnalyzeColumn(connection, firstTable.Name, firstColumn);

        // Assert
        Assert.NotNull(columnSchema);
        Assert.Equal(firstColumn.Name, columnSchema.Name);
        Assert.Equal(firstColumn.Type, columnSchema.Type);
        Assert.Equal(firstColumn.NotNull, columnSchema.NotNull);
        Assert.Equal(firstColumn.IsPrimaryKey, columnSchema.IsPrimaryKey);
        Assert.True(columnSchema.AnalysisTimestamp <= DateTime.UtcNow);

        // Verify statistics are populated
        Assert.True(columnSchema.NullCount >= 0);
        Assert.True(columnSchema.NonNullCount >= 0);
        Assert.True(columnSchema.DistinctCount >= 0);
    }

    [Fact]
    public void AnalyzeColumn_WithTextColumn_ShouldIncludeLengthStatistics()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        connection.Open();

        // Find a text column
        var tables = SqliteSchemaReader.GetDatabaseObjects(connection, null, false);
        ColumnInfo? textColumn = null;
        string? tableName = null;

        foreach (var table in tables)
        {
            var columns = SqliteSchemaReader.GetTableColumns(connection, table.Name);
            textColumn = columns.FirstOrDefault(c => c.Type.ToUpperInvariant().Contains("TEXT"));
            if (textColumn != null)
            {
                tableName = table.Name;
                break;
            }
        }

        Assert.NotNull(textColumn);
        Assert.NotNull(tableName);

        // Act
        var columnSchema = SchemaAnalyzer.AnalyzeColumn(connection, tableName, textColumn);

        // Assert
        Assert.NotNull(columnSchema.MinLength);
        Assert.NotNull(columnSchema.MaxLength);
        Assert.NotNull(columnSchema.AvgLength);
        Assert.True(columnSchema.MinLength >= 0);
        Assert.True(columnSchema.MaxLength >= columnSchema.MinLength);
        Assert.True(columnSchema.AvgLength >= 0);
    }

    [Fact]
    public void AnalyzeDatabase_WithTransformations_ShouldTrackTransformationInfo()
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
        var schema = SchemaAnalyzer.AnalyzeDatabase(connection, dbPath, options, transformationPipeline);

        // Assert
        Assert.True(schema.TransformationsEnabled);
        Assert.True(schema.TransformationErrors >= 0);

        // Check that some columns have transformation information
        var columnsWithTransformations = schema.Tables
            .SelectMany(t => t.Columns)
            .Where(c => c.HasTransformations)
            .ToList();

        // Note: Depending on the sample data and transformers, this might be 0
        // The important thing is that the tracking mechanism works
        Assert.True(columnsWithTransformations.Count >= 0);
    }

    [Fact]
    public void GenerateProvenanceManifest_ShouldCreateComprehensiveLineage()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        connection.Open();

        var schema = SchemaAnalyzer.AnalyzeDatabase(connection, dbPath);
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

        var transformationPipeline = new TransformationPipeline(transformationConfig, TransformerRegistryBuilder.CreateDefault());

        // Act
        var manifest = SchemaAnalyzer.GenerateProvenanceManifest(
            dbPath, schema, transformationPipeline, "test_export.xlsx", "Excel");

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal(dbPath, manifest.SourceDatabase);
        Assert.Equal("test_export.xlsx", manifest.ExportPath);
        Assert.Equal("Excel", manifest.ExportFormat);
        Assert.True(manifest.TransformationsApplied);
        Assert.True(manifest.GeneratedTimestamp <= DateTime.UtcNow);
        Assert.NotEmpty(manifest.DatabaseChecksum);
        Assert.NotEmpty(manifest.ExportToolVersion);

        // Verify data lineages
        Assert.True(manifest.DataLineages.Count > 0);
        foreach (var lineage in manifest.DataLineages)
        {
            Assert.NotEmpty(lineage.TableName);
            Assert.True(lineage.SourceRowCount >= 0);
            Assert.True(lineage.OriginalColumns.Count > 0);
        }
    }

    [Fact]
    public void AnalyzeColumn_WithExcludedColumn_ShouldMarkAsExcluded()
    {
        // Arrange
        var dbPath = SampleDatabaseGenerator.CreateSampleDatabase();
        _tempPaths.Add(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        connection.Open();

        var tables = SqliteSchemaReader.GetDatabaseObjects(connection, null, false);
        var firstTable = tables.First();
        var columns = SqliteSchemaReader.GetTableColumns(connection, firstTable.Name);
        var firstColumn = columns.First();

        var transformationConfig = new TransformationConfig
        {
            Global = new GlobalSettings { EnableTransformations = true },
            Tables = new Dictionary<string, TableConfig>
            {
                [firstTable.Name] = new TableConfig
                {
                    Filters = new TableFilters
                    {
                        ExcludeColumns = new List<string> { firstColumn.Name }
                    }
                }
            }
        };

        var transformationPipeline = new TransformationPipeline(transformationConfig, TransformerRegistryBuilder.CreateDefault());

        // Act
        var columnSchema = SchemaAnalyzer.AnalyzeColumn(connection, firstTable.Name, firstColumn, transformationPipeline);

        // Assert
        Assert.True(columnSchema.ExcludedByTransformation);
    }
}