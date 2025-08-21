using System.Text.Json;
using DB2XL.Core.Models;
using DB2XL.Core.Services;
using DB2XL.Export.Bundle.Extensions;
using DB2XL.Export.Bundle.Services;

namespace DB2XL.Export.Bundle.Tests.Services;

/// <summary>
/// Comprehensive tests for JsonlExportEngine covering all export scenarios.
/// </summary>
public sealed class JsonlExportEngineTests : IDisposable
{
    private readonly JsonlExportEngine _engine;
    private readonly string _tempDirectory;

    public JsonlExportEngineTests()
    {
        _engine = new JsonlExportEngine();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "jsonl_tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    #region Single Partition Export Tests

    [Fact]
    public async Task ExportPartitionAsync_WithSimpleData_ShouldCreateValidJsonlFile()
    {
        // Arrange
        var partition = CreateTestPartition("test_table", "p001", CreateSimpleTestData());
        var outputFile = Path.Combine(_tempDirectory, "simple_test.jsonl");
        var options = new JsonlExportOptions();

        // Act
        var result = await _engine.ExportPartitionAsync(partition, outputFile, options);

        // Assert
        Assert.True(result.IsSuccessful);
        Assert.True(File.Exists(outputFile));
        Assert.Equal(3, result.RecordCount);
        Assert.True(result.FileSizeBytes > 0);
        Assert.NotEmpty(result.FileChecksum);
        Assert.Empty(result.Warnings);

        // Verify file content
        var lines = await File.ReadAllLinesAsync(outputFile);
        Assert.Equal(4, lines.Length); // 3 records + 1 schema header (default)
        
        // Verify each line is valid JSON
        foreach (var line in lines)
        {
            var exception = Record.Exception(() => JsonDocument.Parse(line));
            Assert.Null(exception);
        }
    }

    [Fact]
    public async Task ExportPartitionAsync_WithNullValues_ShouldHandleNullsCorrectly()
    {
        // Arrange
        var partition = CreateTestPartition("null_table", "p001", CreateDataWithNulls());
        var outputFile = Path.Combine(_tempDirectory, "null_test.jsonl");
        var options = new JsonlExportOptions 
        { 
            NullHandling = JsonNullHandling.Null,
            IncludeSchemaHeader = false 
        };

        // Act
        var result = await _engine.ExportPartitionAsync(partition, outputFile, options);

        // Assert
        Assert.True(result.IsSuccessful);
        Assert.Equal(2, result.RecordCount);

        var lines = await File.ReadAllLinesAsync(outputFile);
        var firstRecord = JsonDocument.Parse(lines[0]).RootElement;
        
        Assert.True(firstRecord.TryGetProperty("nullValue", out var nullProp));
        Assert.Equal(JsonValueKind.Null, nullProp.ValueKind);
    }

    [Fact]
    public async Task ExportPartitionAsync_WithSkipNullHandling_ShouldOmitNullFields()
    {
        // Arrange
        var partition = CreateTestPartition("skip_null_table", "p001", CreateDataWithNulls());
        var outputFile = Path.Combine(_tempDirectory, "skip_null_test.jsonl");
        var options = new JsonlExportOptions 
        { 
            NullHandling = JsonNullHandling.Skip,
            IncludeSchemaHeader = false 
        };

        // Act
        var result = await _engine.ExportPartitionAsync(partition, outputFile, options);

        // Assert
        Assert.True(result.IsSuccessful);
        
        var lines = await File.ReadAllLinesAsync(outputFile);
        var firstRecord = JsonDocument.Parse(lines[0]).RootElement;
        
        Assert.False(firstRecord.TryGetProperty("nullValue", out _));
        Assert.True(firstRecord.TryGetProperty("validValue", out _));
    }

    [Fact]
    public async Task ExportPartitionAsync_WithDateTimeData_ShouldSerializeDatesCorrectly()
    {
        // Arrange
        var partition = CreateTestPartition("datetime_table", "p001", CreateDateTimeData());
        var outputFile = Path.Combine(_tempDirectory, "datetime_test.jsonl");
        var options = new JsonlExportOptions 
        { 
            DateTimeFormat = JsonDateTimeFormat.ISO8601,
            IncludeSchemaHeader = false 
        };

        // Act
        var result = await _engine.ExportPartitionAsync(partition, outputFile, options);

        // Assert
        Assert.True(result.IsSuccessful);
        
        var lines = await File.ReadAllLinesAsync(outputFile);
        var record = JsonDocument.Parse(lines[0]).RootElement;
        
        Assert.True(record.TryGetProperty("timestamp", out var timestampProp));
        Assert.Equal(JsonValueKind.String, timestampProp.ValueKind);
        
        var timestampStr = timestampProp.GetString();
        Assert.Contains("T", timestampStr); // ISO8601 format indicator
        Assert.Contains("Z", timestampStr); // UTC indicator
    }

    [Fact]
    public async Task ExportPartitionAsync_WithUnixDateTimeFormat_ShouldSerializeAsNumber()
    {
        // Arrange
        var partition = CreateTestPartition("unix_table", "p001", CreateDateTimeData());
        var outputFile = Path.Combine(_tempDirectory, "unix_test.jsonl");
        var options = new JsonlExportOptions 
        { 
            DateTimeFormat = JsonDateTimeFormat.Unix,
            IncludeSchemaHeader = false 
        };

        // Act
        var result = await _engine.ExportPartitionAsync(partition, outputFile, options);

        // Assert
        Assert.True(result.IsSuccessful);
        
        var lines = await File.ReadAllLinesAsync(outputFile);
        var record = JsonDocument.Parse(lines[0]).RootElement;
        
        Assert.True(record.TryGetProperty("timestamp", out var timestampProp));
        Assert.Equal(JsonValueKind.Number, timestampProp.ValueKind);
    }

    [Fact]
    public async Task ExportPartitionAsync_WithProvenance_ShouldIncludeMetadata()
    {
        // Arrange
        var partition = CreateTestPartition("provenance_table", "p001", CreateSimpleTestData());
        var outputFile = Path.Combine(_tempDirectory, "provenance_test.jsonl");
        var options = new JsonlExportOptions 
        { 
            IncludeProvenance = true,
            IncludeSchemaHeader = false 
        };

        // Act
        var result = await _engine.ExportPartitionAsync(partition, outputFile, options);

        // Assert
        Assert.True(result.IsSuccessful);
        
        var lines = await File.ReadAllLinesAsync(outputFile);
        var record = JsonDocument.Parse(lines[0]).RootElement;
        
        Assert.True(record.TryGetProperty("_meta", out var metaProp));
        Assert.Equal(JsonValueKind.Object, metaProp.ValueKind);
        
        Assert.True(metaProp.TryGetProperty("exportTimestamp", out _));
        Assert.True(metaProp.TryGetProperty("sourceFormat", out var sourceProp));
        Assert.Equal("sqlite", sourceProp.GetString());
    }

    [Fact]
    public async Task ExportPartitionAsync_WithRowChecksums_ShouldIncludeChecksums()
    {
        // Arrange
        var partition = CreateTestPartition("checksum_table", "p001", CreateSimpleTestData());
        var outputFile = Path.Combine(_tempDirectory, "checksum_test.jsonl");
        var options = new JsonlExportOptions 
        { 
            IncludeRowChecksums = true,
            IncludeSchemaHeader = false 
        };

        // Act
        var result = await _engine.ExportPartitionAsync(partition, outputFile, options);

        // Assert
        Assert.True(result.IsSuccessful);
        
        var lines = await File.ReadAllLinesAsync(outputFile);
        var record = JsonDocument.Parse(lines[0]).RootElement;
        
        Assert.True(record.TryGetProperty("_checksum", out var checksumProp));
        Assert.Equal(JsonValueKind.String, checksumProp.ValueKind);
        Assert.NotEmpty(checksumProp.GetString()!);
    }

    [Fact]
    public async Task ExportPartitionAsync_WithEmptyData_ShouldCreateEmptyJsonlFile()
    {
        // Arrange
        var partition = CreateTestPartition("empty_table", "p001", AsyncEnumerableExtensions.EmptyAsync<IReadOnlyDictionary<string, object?>>());
        var outputFile = Path.Combine(_tempDirectory, "empty_test.jsonl");
        var options = new JsonlExportOptions();

        // Act
        var result = await _engine.ExportPartitionAsync(partition, outputFile, options);

        // Assert
        Assert.True(result.IsSuccessful);
        Assert.Equal(0, result.RecordCount);
        Assert.True(File.Exists(outputFile));
        
        var lines = await File.ReadAllLinesAsync(outputFile);
        Assert.Single(lines); // Only schema header
    }

    #endregion

    #region Multiple Partition Export Tests

    [Fact]
    public async Task ExportPartitionsAsync_WithMultiplePartitions_ShouldCreateAllFiles()
    {
        // Arrange
        var partitions = CreateMultipleTestPartitions().ToAsyncEnumerable();
        var options = new JsonlExportOptions { EnableParallelProcessing = false };

        // Act
        var results = await _engine.ExportPartitionsAsync(partitions, _tempDirectory, options);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.IsSuccessful));
        Assert.All(results, r => Assert.True(File.Exists(r.FilePath)));
        
        // Verify total record counts
        var totalRecords = results.Sum(r => r.RecordCount);
        Assert.Equal(9, totalRecords); // 3 partitions × 3 records each
    }

    [Fact]
    public async Task ExportPartitionsAsync_WithParallelProcessing_ShouldCompleteSuccessfully()
    {
        // Arrange
        var partitions = CreateMultipleTestPartitions().ToAsyncEnumerable();
        var options = new JsonlExportOptions 
        { 
            EnableParallelProcessing = true,
            MaxDegreeOfParallelism = 2
        };

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var results = await _engine.ExportPartitionsAsync(partitions, _tempDirectory, options);
        stopwatch.Stop();

        // Assert
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.IsSuccessful));
        
        // Parallel processing should complete within reasonable time
        Assert.True(stopwatch.ElapsedMilliseconds < 5000);
    }

    #endregion

    #region Schema Manifest Generation Tests

    [Fact]
    public async Task GenerateSchemaManifestAsync_WithExportResults_ShouldCreateComprehensiveManifest()
    {
        // Arrange
        var partition = CreateTestPartition("manifest_table", "p001", CreateMixedDataTypes());
        var outputFile = Path.Combine(_tempDirectory, "manifest_test.jsonl");
        var options = new JsonlExportOptions();
        
        var exportResult = await _engine.ExportPartitionAsync(partition, outputFile, options);
        var tableMetadata = CreateTestTableMetadata("manifest_table");

        // Act
        var manifest = await _engine.GenerateSchemaManifestAsync([exportResult], tableMetadata);

        // Assert
        Assert.Equal("manifest_table", manifest.TableName);
        Assert.Equal("1.0", manifest.SchemaVersion);
        Assert.True(manifest.ExportTimestamp > DateTime.UtcNow.AddMinutes(-1));
        Assert.NotEmpty(manifest.Fields);
        Assert.Single(manifest.Partitions);
        Assert.Equal(exportResult.RecordCount, manifest.TotalRecordCount);
        Assert.NotEmpty(manifest.TableMetadata);
        Assert.NotNull(manifest.ProcessingRecommendations);
        
        // Verify processing recommendations
        Assert.True(manifest.ProcessingRecommendations.RecommendedBatchSize > 0);
        Assert.True(manifest.ProcessingRecommendations.ComplexityScore >= 1);
        Assert.True(manifest.ProcessingRecommendations.ComplexityScore <= 10);
    }

    [Fact]
    public async Task GenerateSchemaManifestAsync_WithMultiplePartitions_ShouldAggregateCorrectly()
    {
        // Arrange
        var partitions = CreateMultipleTestPartitions().ToAsyncEnumerable();
        var options = new JsonlExportOptions();
        var results = await _engine.ExportPartitionsAsync(partitions, _tempDirectory, options);
        var tableMetadata = CreateTestTableMetadata("multi_table");

        // Act
        var manifest = await _engine.GenerateSchemaManifestAsync(results, tableMetadata);

        // Assert
        Assert.Equal(3, manifest.Partitions.Count);
        Assert.Equal(results.Sum(r => r.RecordCount), manifest.TotalRecordCount);
        
        // Verify partition manifests
        foreach (var partitionManifest in manifest.Partitions)
        {
            Assert.NotEmpty(partitionManifest.RelativePath);
            Assert.NotEmpty(partitionManifest.PartitionLabel);
            Assert.True(partitionManifest.RecordCount > 0);
            Assert.True(partitionManifest.FileSizeBytes > 0);
            Assert.NotEmpty(partitionManifest.Checksum);
        }
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task ValidateJsonlFileAsync_WithValidFile_ShouldPassValidation()
    {
        // Arrange
        var partition = CreateTestPartition("valid_table", "p001", CreateSimpleTestData());
        var outputFile = Path.Combine(_tempDirectory, "valid_test.jsonl");
        var options = new JsonlExportOptions();
        
        var exportResult = await _engine.ExportPartitionAsync(partition, outputFile, options);
        var tableMetadata = CreateTestTableMetadata("valid_table");
        var manifest = await _engine.GenerateSchemaManifestAsync([exportResult], tableMetadata);

        // Act
        var validationResult = await _engine.ValidateJsonlFileAsync(outputFile, manifest);

        // Assert
        Assert.True(validationResult.IsValid);
        Assert.Empty(validationResult.Errors);
        Assert.True(validationResult.Metrics.LinesValidated > 0);
        Assert.True(validationResult.Metrics.ValidObjects > 0);
        Assert.True(validationResult.Metrics.ValidationTime > TimeSpan.Zero);
    }

    [Fact]
    public async Task ValidateJsonlFileAsync_WithNonExistentFile_ShouldReturnError()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_tempDirectory, "nonexistent.jsonl");
        var manifest = new JsonlSchemaManifest { TableName = "test" };

        // Act
        var validationResult = await _engine.ValidateJsonlFileAsync(nonExistentFile, manifest);

        // Assert
        Assert.False(validationResult.IsValid);
        Assert.Contains("File not found", validationResult.Errors.First());
    }

    #endregion

    #region Performance and Edge Cases

    [Fact]
    public async Task ExportPartitionAsync_WithLargeDataset_ShouldCompleteEfficiently()
    {
        // Arrange
        var largeDataset = CreateLargeTestDataset(10000);
        var partition = CreateTestPartition("large_table", "p001", largeDataset);
        var outputFile = Path.Combine(_tempDirectory, "large_test.jsonl");
        var options = new JsonlExportOptions();

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _engine.ExportPartitionAsync(partition, outputFile, options);
        stopwatch.Stop();

        // Assert
        Assert.True(result.IsSuccessful);
        Assert.Equal(10000, result.RecordCount);
        Assert.True(result.Metrics.RecordsPerSecond > 100); // Should process at least 100 records/sec
        Assert.True(stopwatch.ElapsedMilliseconds < 30000); // Should complete within 30 seconds
    }

    [Fact]
    public async Task ExportPartitionAsync_WithSpecialCharacters_ShouldEscapeCorrectly()
    {
        // Arrange
        var specialData = CreateDataWithSpecialCharacters();
        var partition = CreateTestPartition("special_table", "p001", specialData);
        var outputFile = Path.Combine(_tempDirectory, "special_test.jsonl");
        var options = new JsonlExportOptions { IncludeSchemaHeader = false };

        // Act
        var result = await _engine.ExportPartitionAsync(partition, outputFile, options);

        // Assert
        Assert.True(result.IsSuccessful);
        
        var content = await File.ReadAllTextAsync(outputFile);
        
        // Verify content is valid JSON and special characters are preserved
        var lines = await File.ReadAllLinesAsync(outputFile);
        var record = JsonDocument.Parse(lines[0]).RootElement;
        
        Assert.True(record.TryGetProperty("emoji", out var emojiProp));
        Assert.Equal("🚀💫", emojiProp.GetString());
        
        Assert.True(record.TryGetProperty("quotes", out var quotesProp));
        Assert.Equal("She said \"Hello!\"", quotesProp.GetString());
    }

    #endregion

    #region Helper Methods

    private static DataPartition CreateTestPartition(string tableName, string partitionLabel, IAsyncEnumerable<IReadOnlyDictionary<string, object?>> data)
    {
        return new DataPartition
        {
            Data = data,
            Info = new PartitionInfo
            {
                TableName = tableName,
                PartitionLabel = partitionLabel,
                Strategy = "by=test",
                RelativePath = $"tables/{tableName}/{tableName}_{partitionLabel}.jsonl",
                Format = "jsonl"
            },
            EstimatedRowCount = 3,
            IsFinalPartition = true,
            PartitionIndex = 0,
            Strategy = PartitionStrategy.None
        };
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> CreateSimpleTestData()
    {
        var records = new[]
        {
            new Dictionary<string, object?> { ["id"] = 1, ["name"] = "Alice", ["age"] = 30 },
            new Dictionary<string, object?> { ["id"] = 2, ["name"] = "Bob", ["age"] = 25 },
            new Dictionary<string, object?> { ["id"] = 3, ["name"] = "Charlie", ["age"] = 35 }
        };

        foreach (var record in records)
        {
            yield return record;
        }
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> CreateDataWithNulls()
    {
        var records = new[]
        {
            new Dictionary<string, object?> { ["id"] = 1, ["validValue"] = "test", ["nullValue"] = null },
            new Dictionary<string, object?> { ["id"] = 2, ["validValue"] = "another", ["nullValue"] = null }
        };

        foreach (var record in records)
        {
            yield return record;
        }
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> CreateDateTimeData()
    {
        var records = new[]
        {
            new Dictionary<string, object?> { ["id"] = 1, ["timestamp"] = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc) },
            new Dictionary<string, object?> { ["id"] = 2, ["timestamp"] = new DateTime(2025, 1, 16, 14, 45, 0, DateTimeKind.Utc) }
        };

        foreach (var record in records)
        {
            yield return record;
        }
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> CreateMixedDataTypes()
    {
        var records = new[]
        {
            new Dictionary<string, object?> 
            { 
                ["id"] = 1, 
                ["name"] = "Test", 
                ["isActive"] = true, 
                ["score"] = 85.5,
                ["timestamp"] = DateTime.UtcNow,
                ["data"] = new byte[] { 1, 2, 3, 4 }
            }
        };

        foreach (var record in records)
        {
            yield return record;
        }
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> CreateDataWithSpecialCharacters()
    {
        var records = new[]
        {
            new Dictionary<string, object?> 
            { 
                ["id"] = 1, 
                ["emoji"] = "🚀💫",
                ["quotes"] = "She said \"Hello!\"",
                ["newlines"] = "Line 1\nLine 2",
                ["unicode"] = "Café, naïve, résumé"
            }
        };

        foreach (var record in records)
        {
            yield return record;
        }
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> CreateLargeTestDataset(int recordCount)
    {
        for (int i = 1; i <= recordCount; i++)
        {
            yield return new Dictionary<string, object?>
            {
                ["id"] = i,
                ["name"] = $"Record_{i}",
                ["value"] = i * 10,
                ["timestamp"] = DateTime.UtcNow.AddMinutes(-i),
                ["isEven"] = i % 2 == 0
            };
        }
        await Task.CompletedTask;
    }

    private static List<DataPartition> CreateMultipleTestPartitions()
    {
        var partitions = new List<DataPartition>();
        
        for (int i = 1; i <= 3; i++)
        {
            var data = CreateSimpleTestData();
            partitions.Add(CreateTestPartition($"table_{i}", $"p{i:D3}", data));
        }

        return partitions;
    }

    private static TableMetadata CreateTestTableMetadata(string tableName)
    {
        return new TableMetadata
        {
            TableName = tableName,
            EstimatedRowCount = 1000,
            Columns = new List<ColumnMetadata>
            {
                new() { Name = "id", DeclaredType = "INTEGER", IsPrimaryKey = true, IsNullable = false },
                new() { Name = "name", DeclaredType = "TEXT", IsPrimaryKey = false, IsNullable = true },
                new() { Name = "age", DeclaredType = "INTEGER", IsPrimaryKey = false, IsNullable = true }
            }.AsReadOnly(),
            Indexes = new List<Core.Models.IndexInfo>().AsReadOnly(),
            ForeignKeys = new List<ForeignKeyInfo>().AsReadOnly(),
            PrimaryKeyColumns = new[] { "id" }.AsReadOnly()
        };
    }

    #endregion
}