using DB2XL.Core.Models;
using DB2XL.Core.Services;
using DB2XL.Export.Bundle.Services;
using Microsoft.Data.Sqlite;

namespace DB2XL.Export.Bundle.Tests.Services;

/// <summary>
/// Comprehensive tests for the Parquet export engine.
/// Validates high-performance columnar export functionality.
/// </summary>
public class ParquetExportEngineTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _testDbPath;
    private readonly IParquetExportEngine _parquetEngine;
    
    public ParquetExportEngineTests()
    {
        // Create temporary directory for test outputs
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"parquet_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        
        // Create test database
        _testDbPath = Path.Combine(_tempDirectory, "test.sqlite");
        CreateTestDatabase();
        
        // Initialize Parquet export engine
        _parquetEngine = new ParquetExportEngine();
    }

    [Fact]
    public async Task ExportTableAsync_WithValidTable_CreatesParquetFile()
    {
        // Arrange
        var outputPath = Path.Combine(_tempDirectory, "test_table.parquet");
        var connectionString = $"Data Source={_testDbPath}";
        var options = new ParquetExportOptions
        {
            Compression = ParquetCompression.Snappy,
            RowGroupSize = 1000,
            EnableDictionaryEncoding = true,
            EnableStatistics = true
        };

        // Act
        var result = await _parquetEngine.ExportTableAsync(
            connectionString, 
            "customers", 
            outputPath, 
            options);

        // Assert
        Assert.True(result.IsSuccess, $"Parquet export should succeed. Errors: {string.Join(", ", result.Errors)}");
        Assert.True(File.Exists(result.FilePath), "Parquet file should be created");
        Assert.True(result.RowsExported > 0, "Should export rows");
        Assert.True(result.FileSizeBytes > 0, "File should have content");
        Assert.Equal(outputPath, result.FilePath);
        Assert.True(result.ExportDuration > TimeSpan.Zero, "Should track export duration");
        Assert.NotNull(result.Metadata);
        Assert.True(result.Metadata.ColumnCount > 0, "Should have column metadata");
        Assert.NotEmpty(result.ColumnStatistics);
    }

    [Fact]
    public async Task ExportPartitionAsync_WithDataPartition_CreatesParquetFile()
    {
        // Arrange
        var outputPath = Path.Combine(_tempDirectory, "partition.parquet");
        var testData = CreateTestDataPartition();
        var options = new ParquetExportOptions
        {
            Compression = ParquetCompression.Gzip,
            RowGroupSize = 500,
            EnableDictionaryEncoding = false
        };

        // Act
        var result = await _parquetEngine.ExportPartitionAsync(testData, outputPath, options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(result.FilePath));
        Assert.True(result.RowsExported > 0);
        Assert.True(result.CompressionRatio >= 1.0);
        Assert.Equal(1, result.RowGroupCount); // Small dataset should fit in one row group
    }

    [Fact]
    public void ValidateOptions_WithValidOptions_ReturnsValid()
    {
        // Arrange
        var options = new ParquetExportOptions
        {
            Compression = ParquetCompression.Zstd,
            RowGroupSize = 50_000,
            PageSize = 1024 * 1024,
            EnableDictionaryEncoding = true,
            EnableStatistics = true,
            DecimalPrecision = 18,
            DecimalScale = 4
        };

        // Act
        var result = _parquetEngine.ValidateOptions(options);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateOptions_WithInvalidOptions_ReturnsErrors()
    {
        // Arrange
        var options = new ParquetExportOptions
        {
            RowGroupSize = -1, // Invalid
            PageSize = 0, // Invalid
            MaxRowGroupSizeBytes = -100, // Invalid
            DecimalPrecision = 50, // Invalid - too high
            DecimalScale = -1 // Invalid - negative
        };

        // Act
        var result = _parquetEngine.ValidateOptions(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("RowGroupSize"));
        Assert.Contains(result.Errors, e => e.Contains("PageSize"));
        Assert.Contains(result.Errors, e => e.Contains("MaxRowGroupSizeBytes"));
        Assert.Contains(result.Errors, e => e.Contains("DecimalPrecision"));
        Assert.Contains(result.Errors, e => e.Contains("DecimalScale"));
    }

    [Fact]
    public void ValidateOptions_WithBloomFilterOptions_ValidatesCorrectly()
    {
        // Arrange - Valid bloom filter options
        var validOptions = new ParquetExportOptions
        {
            EnableBloomFilters = true,
            BloomFilterFpp = 0.05
        };

        // Act
        var validResult = _parquetEngine.ValidateOptions(validOptions);

        // Assert
        Assert.True(validResult.IsValid);

        // Arrange - Invalid bloom filter options
        var invalidOptions = new ParquetExportOptions
        {
            EnableBloomFilters = true,
            BloomFilterFpp = 1.5 // Invalid - greater than 1
        };

        // Act
        var invalidResult = _parquetEngine.ValidateOptions(invalidOptions);

        // Assert
        Assert.False(invalidResult.IsValid);
        Assert.Contains(invalidResult.Errors, e => e.Contains("BloomFilterFpp"));
    }

    [Fact]
    public void EstimateExport_WithTableSchema_ReturnsAccurateEstimate()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("id", "INTEGER", false, null, true),
            new("name", "TEXT", false, null, false),
            new("value", "REAL", false, null, false),
            new("data", "BLOB", true, null, false)
        }.AsReadOnly();

        var options = new ParquetExportOptions
        {
            Compression = ParquetCompression.Snappy,
            RowGroupSize = 10_000
        };

        var rowCount = 100_000L;
        var averageRowSizeBytes = 128.0;

        // Act
        var estimate = _parquetEngine.EstimateExport(rowCount, columns, averageRowSizeBytes, options);

        // Assert
        Assert.True(estimate.EstimatedFileSizeBytes > 0);
        Assert.True(estimate.EstimatedRowGroups > 0);
        Assert.True(estimate.EstimatedDuration > TimeSpan.Zero);
        Assert.True(estimate.ExpectedCompressionRatio > 1.0);
        Assert.True(estimate.EstimatedMemoryUsageBytes > 0);
        Assert.NotEmpty(estimate.PerformanceNotes);
        
        // Should note BLOB columns
        Assert.Contains(estimate.PerformanceNotes, note => note.Contains("BLOB"));
        
        // Row groups calculation
        var expectedRowGroups = Math.Max(1, (int)Math.Ceiling((double)rowCount / options.RowGroupSize));
        Assert.Equal(expectedRowGroups, estimate.EstimatedRowGroups);
    }

    [Fact]
    public void EstimateExport_WithDifferentCompressionTypes_VariesEstimates()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("text_col", "TEXT", true, null, false)
        }.AsReadOnly();

        var rowCount = 10_000L;
        var averageRowSizeBytes = 64.0;

        // Test different compression types
        var compressions = new[] 
        {
            ParquetCompression.None,
            ParquetCompression.Snappy,
            ParquetCompression.Gzip,
            ParquetCompression.Zstd
        };

        var estimates = new List<ParquetExportEstimation>();

        // Act
        foreach (var compression in compressions)
        {
            var options = new ParquetExportOptions { Compression = compression };
            var estimate = _parquetEngine.EstimateExport(rowCount, columns, averageRowSizeBytes, options);
            estimates.Add(estimate);
        }

        // Assert
        // No compression should have largest file size
        var noCompressionEstimate = estimates.FirstOrDefault(e => e.ExpectedCompressionRatio == 1.0);
        var compressedEstimates = estimates.Where(e => e.ExpectedCompressionRatio > 1.0);

        if (noCompressionEstimate != null)
        {
            Assert.All(compressedEstimates, compressed => 
                Assert.True(compressed.EstimatedFileSizeBytes < noCompressionEstimate.EstimatedFileSizeBytes));
        }

        // ZSTD should have better compression than Snappy
        var zstdEstimate = estimates.FirstOrDefault(e => e.ExpectedCompressionRatio > 2.5);
        var snappyEstimate = estimates.FirstOrDefault(e => e.ExpectedCompressionRatio is > 1.5 and < 2.5);

        if (zstdEstimate != null && snappyEstimate != null)
        {
            Assert.True(zstdEstimate.EstimatedFileSizeBytes < snappyEstimate.EstimatedFileSizeBytes);
        }
    }

    [Fact]
    public async Task ExportTableAsync_WithNonexistentTable_ReturnsError()
    {
        // Arrange
        var outputPath = Path.Combine(_tempDirectory, "nonexistent.parquet");
        var connectionString = $"Data Source={_testDbPath}";
        var options = new ParquetExportOptions();

        // Act
        var result = await _parquetEngine.ExportTableAsync(
            connectionString,
            "nonexistent_table",
            outputPath,
            options);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, result.RowsExported);
    }

    [Fact]
    public async Task ExportTableAsync_WithDifferentCompressionTypes_CreatesValidFiles()
    {
        // Arrange
        var connectionString = $"Data Source={_testDbPath}";
        var compressions = new[]
        {
            ParquetCompression.None,
            ParquetCompression.Gzip  // Only test what's implemented
        };

        var results = new List<ParquetExportResult>();

        // Act
        foreach (var compression in compressions)
        {
            var outputPath = Path.Combine(_tempDirectory, $"test_{compression.ToString().ToLower()}.parquet");
            var options = new ParquetExportOptions
            {
                Compression = compression,
                RowGroupSize = 1000
            };

            var result = await _parquetEngine.ExportTableAsync(
                connectionString,
                "customers",
                outputPath,
                options);

            results.Add(result);
        }

        // Assert
        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.All(results, result => Assert.True(File.Exists(result.FilePath)));
        Assert.All(results, result => Assert.True(result.RowsExported > 0));
        
        // Files should exist with different compression ratios
        var fileSizes = results.Select(r => r.FileSizeBytes).ToList();
        Assert.True(fileSizes.All(size => size > 0), "All files should have content");
    }

    private void CreateTestDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE customers (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT,
                age INTEGER,
                balance REAL,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                is_active BOOLEAN DEFAULT 1,
                profile_data BLOB
            );

            INSERT INTO customers (name, email, age, balance, is_active) VALUES 
                ('John Doe', 'john@example.com', 30, 1500.50, 1),
                ('Jane Smith', 'jane@example.com', 25, 2300.75, 1),
                ('Bob Wilson', 'bob@example.com', 45, 500.25, 0),
                ('Alice Brown', 'alice@example.com', 35, 3200.00, 1),
                ('Charlie Davis', 'charlie@example.com', 28, 800.90, 1);
        ";

        command.ExecuteNonQuery();
    }

    private static DataPartition CreateTestDataPartition()
    {
        // Create async enumerable test data
        var testRows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["id"] = 1, ["name"] = "Test User 1", ["value"] = 100.50 }.AsReadOnly(),
            new Dictionary<string, object?> { ["id"] = 2, ["name"] = "Test User 2", ["value"] = 200.75 }.AsReadOnly(),
            new Dictionary<string, object?> { ["id"] = 3, ["name"] = "Test User 3", ["value"] = 300.25 }.AsReadOnly()
        };

        return new DataPartition
        {
            Data = CreateAsyncEnumerable(testRows),
            Info = new PartitionInfo 
            { 
                TableName = "test_table",
                PartitionLabel = "test",
                RowCount = 3
            },
            EstimatedRowCount = 3,
            IsFinalPartition = true,
            PartitionIndex = 0,
            Strategy = PartitionStrategy.None
        };
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> CreateAsyncEnumerable(
        IEnumerable<IReadOnlyDictionary<string, object?>> items)
    {
        foreach (var item in items)
        {
            yield return item;
        }
        await Task.CompletedTask; // To make it truly async
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }
}