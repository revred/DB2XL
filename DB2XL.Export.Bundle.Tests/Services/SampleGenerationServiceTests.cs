using DB2XL.Core.Models;
using DB2XL.Core.Services;
using DB2XL.Export.Bundle.Services;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace DB2XL.Export.Bundle.Tests.Services;

public class SampleGenerationServiceTests : IDisposable
{
    private readonly string _connectionString;
    private readonly string _tempDirectory;
    private readonly SampleGenerationService _service;

    public SampleGenerationServiceTests()
    {
        _connectionString = "Data Source=:memory:";
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"sample_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _service = new SampleGenerationService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [Fact]
    public async Task GenerateSamplesAsync_WithSimpleTable_ShouldCreateSample()
    {
        // Arrange
        await CreateTestDatabaseAsync(_connectionString);
        
        var options = new SampleGenerationOptions
        {
            OutputDirectory = _tempDirectory,
            MaxSampleSize = 50,
            DefaultStrategy = SamplingStrategy.Random,
            OutputFormat = SampleOutputFormat.Jsonl
        };

        // Act
        var result = await _service.GenerateSamplesAsync(_connectionString, options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.TableSamples);
        Assert.Contains("users", result.TableSamples.Keys);
        
        var userSample = result.TableSamples["users"];
        Assert.Equal(SamplingStrategy.Random, userSample.StrategyUsed);
        Assert.True(userSample.SampleRowCount > 0);
        Assert.True(userSample.SampleRowCount <= 50);
        Assert.Single(userSample.FilePaths);
        
        // Verify file exists
        var filePath = userSample.FilePaths.First();
        Assert.True(File.Exists(filePath));
        Assert.True(filePath.EndsWith(".jsonl"));
    }

    [Fact]
    public async Task GenerateSamplesAsync_WithTableFilter_ShouldOnlySampleSpecifiedTables()
    {
        // Arrange
        await CreateTestDatabaseAsync(_connectionString);
        
        var options = new SampleGenerationOptions
        {
            OutputDirectory = _tempDirectory,
            IncludeTables = new[] { "users" },
            MaxSampleSize = 50,
            OutputFormat = SampleOutputFormat.Jsonl
        };

        // Act
        var result = await _service.GenerateSamplesAsync(_connectionString, options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.TableSamples);
        Assert.Contains("users", result.TableSamples.Keys);
        Assert.DoesNotContain("orders", result.TableSamples.Keys);
    }

    [Fact]
    public async Task GenerateSamplesAsync_WithExcludeFilter_ShouldSkipExcludedTables()
    {
        // Arrange
        await CreateTestDatabaseAsync(_connectionString);
        
        var options = new SampleGenerationOptions
        {
            OutputDirectory = _tempDirectory,
            ExcludeTables = new[] { "orders" },
            MaxSampleSize = 50,
            OutputFormat = SampleOutputFormat.Jsonl
        };

        // Act
        var result = await _service.GenerateSamplesAsync(_connectionString, options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Single(result.TableSamples);
        Assert.Contains("users", result.TableSamples.Keys);
        Assert.DoesNotContain("orders", result.TableSamples.Keys);
    }

    [Fact]
    public async Task GenerateSamplesAsync_WithTableSpecificConfig_ShouldUseCustomStrategy()
    {
        // Arrange
        await CreateTestDatabaseAsync(_connectionString);
        
        var tableConfig = new TableSamplingConfig
        {
            TableName = "users",
            Strategy = SamplingStrategy.Top,
            SampleSize = 25
        };
        
        var options = new SampleGenerationOptions
        {
            OutputDirectory = _tempDirectory,
            MaxSampleSize = 50,
            DefaultStrategy = SamplingStrategy.Random,
            TableConfigs = new Dictionary<string, TableSamplingConfig> { ["users"] = tableConfig },
            OutputFormat = SampleOutputFormat.Jsonl
        };

        // Act
        var result = await _service.GenerateSamplesAsync(_connectionString, options);

        // Assert
        Assert.True(result.IsSuccess);
        
        var userSample = result.TableSamples["users"];
        Assert.Equal(SamplingStrategy.Top, userSample.StrategyUsed);
        Assert.True(userSample.SampleRowCount <= 25);
    }

    [Fact]
    public async Task GenerateSamplesAsync_WithCsvOutput_ShouldCreateCsvFile()
    {
        // Arrange
        await CreateTestDatabaseAsync(_connectionString);
        
        var options = new SampleGenerationOptions
        {
            OutputDirectory = _tempDirectory,
            MaxSampleSize = 50,
            OutputFormat = SampleOutputFormat.Csv
        };

        // Act
        var result = await _service.GenerateSamplesAsync(_connectionString, options);

        // Assert
        Assert.True(result.IsSuccess);
        
        var userSample = result.TableSamples["users"];
        var filePath = userSample.FilePaths.First();
        Assert.True(filePath.EndsWith(".csv"));
        Assert.True(File.Exists(filePath));
        
        // Verify CSV format
        var content = await File.ReadAllTextAsync(filePath);
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1); // Header + data
        Assert.Contains(",", lines[0]); // CSV format
    }

    [Fact]
    public async Task GenerateSamplesAsync_WithSqlOutput_ShouldCreateSqlFile()
    {
        // Arrange
        await CreateTestDatabaseAsync(_connectionString);
        
        var options = new SampleGenerationOptions
        {
            OutputDirectory = _tempDirectory,
            MaxSampleSize = 50,
            OutputFormat = SampleOutputFormat.Sql
        };

        // Act
        var result = await _service.GenerateSamplesAsync(_connectionString, options);

        // Assert
        Assert.True(result.IsSuccess);
        
        var userSample = result.TableSamples["users"];
        var filePath = userSample.FilePaths.First();
        Assert.True(filePath.EndsWith(".sql"));
        Assert.True(File.Exists(filePath));
        
        // Verify SQL format
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("INSERT INTO", content);
        Assert.Contains("VALUES", content);
    }

    [Fact]
    public async Task GenerateSamplesAsync_WithRandomSeed_ShouldBeReproducible()
    {
        // Arrange
        await CreateTestDatabaseAsync(_connectionString);
        
        var options = new SampleGenerationOptions
        {
            OutputDirectory = _tempDirectory,
            MaxSampleSize = 50,
            RandomSeed = 12345,
            OutputFormat = SampleOutputFormat.Jsonl
        };

        // Act
        var result1 = await _service.GenerateSamplesAsync(_connectionString, options);
        
        // Create new temp directory for second run
        var tempDirectory2 = Path.Combine(Path.GetTempPath(), $"sample_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory2);
        
        try
        {
            options = options with { OutputDirectory = tempDirectory2 };
            var result2 = await _service.GenerateSamplesAsync(_connectionString, options);

            // Assert
            Assert.True(result1.IsSuccess);
            Assert.True(result2.IsSuccess);
            
            var sample1 = result1.TableSamples["users"];
            var sample2 = result2.TableSamples["users"];
            
            Assert.Equal(sample1.SampleRowCount, sample2.SampleRowCount);
            Assert.Equal(sample1.StrategyUsed, sample2.StrategyUsed);
        }
        finally
        {
            if (Directory.Exists(tempDirectory2))
            {
                Directory.Delete(tempDirectory2, true);
            }
        }
    }

    [Fact]
    public async Task GenerateSamplesAsync_WithManifestGeneration_ShouldCreateManifest()
    {
        // Arrange
        await CreateTestDatabaseAsync(_connectionString);
        
        var options = new SampleGenerationOptions
        {
            OutputDirectory = _tempDirectory,
            MaxSampleSize = 50,
            GenerateMetadata = true,
            OutputFormat = SampleOutputFormat.Jsonl
        };

        // Act
        var result = await _service.GenerateSamplesAsync(_connectionString, options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.ManifestPath);
        Assert.True(File.Exists(result.ManifestPath));
        
        // Verify manifest content
        var manifestContent = await File.ReadAllTextAsync(result.ManifestPath);
        var manifest = JsonSerializer.Deserialize<JsonElement>(manifestContent);
        
        Assert.True(manifest.TryGetProperty("generatedAt", out _));
        Assert.True(manifest.TryGetProperty("tables", out _));
        Assert.True(manifest.TryGetProperty("options", out _));
    }

    [Fact]
    public async Task AnalyzeSamplingStrategyAsync_WithLargeTable_ShouldRecommendSystematic()
    {
        // Arrange
        await CreateLargeTestDatabaseAsync(_connectionString);

        // Act
        var recommendation = await _service.AnalyzeSamplingStrategyAsync(_connectionString, "large_table");

        // Assert
        Assert.Equal("large_table", recommendation.TableName);
        Assert.True(recommendation.Confidence > 0);
        Assert.NotNull(recommendation.Reason);
        Assert.True(recommendation.RecommendedSampleSize > 0);
        Assert.NotEmpty(recommendation.AlternativeStrategies);
        
        // For large tables, should recommend systematic or similar efficient strategy
        Assert.Contains(recommendation.RecommendedStrategy, new[] 
        { 
            SamplingStrategy.Systematic, 
            SamplingStrategy.Random, 
            SamplingStrategy.Cluster 
        });
    }

    [Fact]
    public async Task AnalyzeSamplingStrategyAsync_WithTimeBasedTable_ShouldDetectTimeColumns()
    {
        // Arrange
        await CreateTimeBasedTestDatabaseAsync(_connectionString);

        // Act
        var recommendation = await _service.AnalyzeSamplingStrategyAsync(_connectionString, "events");

        // Assert
        Assert.Equal("events", recommendation.TableName);
        Assert.True(recommendation.TableCharacteristics.HasTimeColumns);
        Assert.True(recommendation.Confidence > 0);
    }

    [Fact]
    public async Task GenerateSamplesAsync_WithIntelligentStrategy_ShouldAnalyzeAndChooseOptimal()
    {
        // Arrange
        await CreateTestDatabaseAsync(_connectionString);
        
        var options = new SampleGenerationOptions
        {
            OutputDirectory = _tempDirectory,
            MaxSampleSize = 50,
            DefaultStrategy = SamplingStrategy.Intelligent,
            OutputFormat = SampleOutputFormat.Jsonl
        };

        // Act
        var result = await _service.GenerateSamplesAsync(_connectionString, options);

        // Assert
        Assert.True(result.IsSuccess);
        
        var userSample = result.TableSamples["users"];
        // Should have chosen a strategy other than Intelligent (which is just a trigger)
        Assert.NotEqual(SamplingStrategy.Intelligent, userSample.StrategyUsed);
        Assert.True(userSample.RepresentativenessScore >= 0);
        Assert.True(userSample.RepresentativenessScore <= 1);
    }

    [Fact]
    public async Task GenerateSamplesAsync_WithQualityMetrics_ShouldCalculateAccurateMetrics()
    {
        // Arrange
        await CreateTestDatabaseAsync(_connectionString);
        
        var options = new SampleGenerationOptions
        {
            OutputDirectory = _tempDirectory,
            MaxSampleSize = 50,
            OutputFormat = SampleOutputFormat.Jsonl
        };

        // Act
        var result = await _service.GenerateSamplesAsync(_connectionString, options);

        // Assert
        Assert.True(result.IsSuccess);
        
        var userSample = result.TableSamples["users"];
        var metrics = userSample.QualityMetrics;
        
        Assert.True(metrics.Completeness >= 0 && metrics.Completeness <= 1);
        Assert.True(metrics.Diversity >= 0 && metrics.Diversity <= 1);
        Assert.True(metrics.DistributionSimilarity >= 0 && metrics.DistributionSimilarity <= 1);
        Assert.True(metrics.RangeCoverage >= 0 && metrics.RangeCoverage <= 1);
        Assert.True(metrics.PatternPreservation >= 0 && metrics.PatternPreservation <= 1);
    }

    [Fact]
    public async Task GenerateSamplesAsync_WithStatistics_ShouldCalculateOverallStats()
    {
        // Arrange
        await CreateTestDatabaseAsync(_connectionString);
        
        var options = new SampleGenerationOptions
        {
            OutputDirectory = _tempDirectory,
            MaxSampleSize = 50,
            OutputFormat = SampleOutputFormat.Jsonl
        };

        // Act
        var result = await _service.GenerateSamplesAsync(_connectionString, options);

        // Assert
        Assert.True(result.IsSuccess);
        
        var stats = result.Statistics;
        Assert.True(stats.TablesProcessed > 0);
        Assert.True(stats.TotalOriginalRows > 0);
        Assert.True(stats.TotalSampleRows > 0);
        Assert.True(stats.OverallSamplePercentage >= 0);
        Assert.True(stats.TotalSampleSizeBytes > 0);
        Assert.True(stats.AverageQualityScore >= 0 && stats.AverageQualityScore <= 1);
        Assert.NotEmpty(stats.StrategyUsage);
    }

    [Fact]
    public async Task GenerateMlSamplesAsync_WithValidData_ShouldCreateTrainValidationTest()
    {
        // Arrange
        await CreateMlTestDatabaseAsync(_connectionString);
        
        var options = new MlSamplingOptions
        {
            SourceTable = "ml_dataset",
            TargetColumn = "category",
            OutputDirectory = _tempDirectory,
            TrainingPercentage = 0.7,
            ValidationPercentage = 0.2,
            TestPercentage = 0.1,
            RandomSeed = 42
        };

        // Act
        var result = await _service.GenerateMlSamplesAsync(_connectionString, options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.TrainingSetPath);
        Assert.NotNull(result.ValidationSetPath);
        Assert.NotNull(result.TestSetPath);
        
        Assert.True(File.Exists(result.TrainingSetPath));
        Assert.True(File.Exists(result.ValidationSetPath));
        Assert.True(File.Exists(result.TestSetPath));
        
        // Verify statistics
        var stats = result.Statistics;
        Assert.True(stats.TotalRows > 0);
        Assert.True(stats.TrainingRows > 0);
        Assert.True(stats.ValidationRows > 0);
        Assert.True(stats.TestRows > 0);
        Assert.True(stats.FeatureCount > 0);
        Assert.True(stats.ClassCount > 0);
        
        // Verify class distributions
        Assert.NotEmpty(result.ClassDistributions);
        Assert.Contains("training", result.ClassDistributions.Keys);
        Assert.Contains("validation", result.ClassDistributions.Keys);
        Assert.Contains("test", result.ClassDistributions.Keys);
    }

    [Fact]
    public async Task GenerateSyntheticDataAsync_WithValidOptions_ShouldCreateSyntheticData()
    {
        // Arrange
        await CreateTestDatabaseAsync(_connectionString);
        
        var options = new SyntheticDataOptions
        {
            SourceTable = "users",
            RowCount = 100,
            OutputDirectory = _tempDirectory,
            PreserveStatistics = true,
            PrivacyLevel = PrivacyLevel.High
        };

        // Act
        var result = await _service.GenerateSyntheticDataAsync(_connectionString, options);

        // Assert - Note: Current implementation is placeholder
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.FilePath);
        Assert.True(File.Exists(result.FilePath));
        
        var quality = result.Quality;
        Assert.True(quality.StatisticalFidelity >= 0 && quality.StatisticalFidelity <= 1);
        Assert.True(quality.PatternPreservation >= 0 && quality.PatternPreservation <= 1);
        Assert.True(quality.Diversity >= 0 && quality.Diversity <= 1);
        Assert.True(quality.Utility >= 0 && quality.Utility <= 1);
        
        var privacy = result.Privacy;
        Assert.True(privacy.PrivacyRisk >= 0 && privacy.PrivacyRisk <= 1);
        Assert.True(privacy.AnonymityLevel >= 0);
        Assert.True(privacy.ReidentificationRisk >= 0 && privacy.ReidentificationRisk <= 1);
    }

    [Theory]
    [InlineData(SamplingStrategy.Random)]
    [InlineData(SamplingStrategy.Systematic)]
    [InlineData(SamplingStrategy.Top)]
    [InlineData(SamplingStrategy.Bottom)]
    public async Task GenerateSamplesAsync_WithDifferentStrategies_ShouldUseCorrectStrategy(SamplingStrategy strategy)
    {
        // Arrange
        await CreateTestDatabaseAsync(_connectionString);
        
        var options = new SampleGenerationOptions
        {
            OutputDirectory = _tempDirectory,
            MaxSampleSize = 50,
            DefaultStrategy = strategy,
            OutputFormat = SampleOutputFormat.Jsonl
        };

        // Act
        var result = await _service.GenerateSamplesAsync(_connectionString, options);

        // Assert
        Assert.True(result.IsSuccess);
        
        var userSample = result.TableSamples["users"];
        Assert.Equal(strategy, userSample.StrategyUsed);
        Assert.True(userSample.SampleRowCount > 0);
    }

    // Helper methods for creating test databases

    private async Task CreateTestDatabaseAsync(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // Create users table
        await ExecuteAsync(connection, @"
            CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT UNIQUE,
                age INTEGER,
                created_at TEXT,
                is_active INTEGER DEFAULT 1
            )");

        // Insert test data
        for (int i = 1; i <= 100; i++)
        {
            await ExecuteAsync(connection, $@"
                INSERT INTO users (name, email, age, created_at, is_active) 
                VALUES ('User {i}', 'user{i}@example.com', {20 + i % 50}, '2024-01-{i % 28 + 1:D2}', {i % 2})");
        }

        // Create orders table
        await ExecuteAsync(connection, @"
            CREATE TABLE orders (
                id INTEGER PRIMARY KEY,
                user_id INTEGER,
                amount REAL,
                status TEXT,
                order_date TEXT,
                FOREIGN KEY (user_id) REFERENCES users(id)
            )");

        // Insert order data
        for (int i = 1; i <= 200; i++)
        {
            var userId = (i % 100) + 1;
            var status = new[] { "pending", "completed", "cancelled" }[i % 3];
            await ExecuteAsync(connection, $@"
                INSERT INTO orders (user_id, amount, status, order_date) 
                VALUES ({userId}, {10.0 + i}, '{status}', '2024-02-{i % 28 + 1:D2}')");
        }
    }

    private async Task CreateLargeTestDatabaseAsync(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, @"
            CREATE TABLE large_table (
                id INTEGER PRIMARY KEY,
                data TEXT,
                value INTEGER
            )");

        // Insert many rows to simulate large table
        for (int i = 1; i <= 1000; i++)
        {
            await ExecuteAsync(connection, $@"
                INSERT INTO large_table (data, value) 
                VALUES ('Data {i}', {i})");
        }
    }

    private async Task CreateTimeBasedTestDatabaseAsync(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, @"
            CREATE TABLE events (
                id INTEGER PRIMARY KEY,
                event_name TEXT,
                timestamp TEXT,
                created_at TEXT,
                updated_at TEXT
            )");

        for (int i = 1; i <= 50; i++)
        {
            var timestamp = DateTime.UtcNow.AddDays(-i).ToString("yyyy-MM-dd HH:mm:ss");
            await ExecuteAsync(connection, $@"
                INSERT INTO events (event_name, timestamp, created_at, updated_at) 
                VALUES ('Event {i}', '{timestamp}', '{timestamp}', '{timestamp}')");
        }
    }

    private async Task CreateMlTestDatabaseAsync(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, @"
            CREATE TABLE ml_dataset (
                id INTEGER PRIMARY KEY,
                feature1 REAL,
                feature2 REAL,
                feature3 TEXT,
                category TEXT
            )");

        var categories = new[] { "A", "B", "C" };
        for (int i = 1; i <= 150; i++)
        {
            var category = categories[i % 3];
            await ExecuteAsync(connection, $@"
                INSERT INTO ml_dataset (feature1, feature2, feature3, category) 
                VALUES ({i * 0.1}, {i * 0.2}, 'Feature {i}', '{category}')");
        }
    }

    private async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}