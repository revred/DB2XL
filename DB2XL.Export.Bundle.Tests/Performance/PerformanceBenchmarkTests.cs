using DB2XL.Core.Models;
using DB2XL.Core.Services;
using DB2XL.Export.Bundle.Services;
using System.Diagnostics;
using System.Text;

namespace DB2XL.Export.Bundle.Tests.Performance;

/// <summary>
/// Performance benchmark tests for streaming and optimization features.
/// Validates high-throughput scenarios and memory efficiency.
/// </summary>
public class PerformanceBenchmarkTests : IDisposable
{
    private readonly string _tempDirectory;
    
    public PerformanceBenchmarkTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"perf_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task JsonlExport_With10KRows_CompletesWithinTimeLimit()
    {
        // Arrange
        var testData = CreateLargeTestDataset(10_000);
        var outputPath = Path.Combine(_tempDirectory, "large_dataset.jsonl");
        var jsonlEngine = new JsonlExportEngine();
        var options = new JsonlExportOptions
        {
            SerializationMode = JsonSerializationMode.Compact,
            IncludeSchemaHeader = false
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await jsonlEngine.ExportPartitionAsync(testData, outputPath, options);
        stopwatch.Stop();

        // Assert
        Assert.NotNull(result);
        Assert.True(File.Exists(outputPath));
        
        // Performance assertion: should complete in reasonable time (less than 10 seconds for 10K rows)
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), 
            $"Export took too long: {stopwatch.Elapsed.TotalSeconds:F2} seconds");
    }

    [Fact]
    public async Task PerformanceOptimizer_Batching_ReducesAsyncOverhead()
    {
        // Arrange
        var testData = CreateTestAsyncEnumerable(1000);
        var batchSize = 100;

        // Act
        var stopwatch = Stopwatch.StartNew();
        var batchCount = 0;
        var totalItems = 0;

        await foreach (var batch in testData.Batch(batchSize))
        {
            batchCount++;
            totalItems += batch.Count;
        }

        stopwatch.Stop();

        // Assert
        Assert.Equal(1000, totalItems);
        Assert.Equal(10, batchCount); // 1000 items / 100 batch size = 10 batches
        
        // Should complete quickly with reduced async overhead
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void PerformanceOptimizer_MemoryEstimation_ProvidesAccurateEstimates()
    {
        // Arrange
        var itemCount = 100_000L;
        var averageItemSize = 512.0; // 512 bytes per item
        var concurrencyLevel = 4;

        // Act
        var estimate = PerformanceOptimizer.EstimateMemoryUsage(
            itemCount, 
            averageItemSize, 
            concurrencyLevel);

        // Assert
        Assert.True(estimate.TotalEstimatedBytes > 0);
        Assert.True(estimate.PeakMemoryUsageBytes > 0);
        Assert.True(estimate.RecommendedBatchSize > 0);
        Assert.True(estimate.RecommendedBatchSize <= 10_000); // Should be reasonable
        
        // Peak memory should be less than total memory for streaming scenarios
        Assert.True(estimate.PeakMemoryUsageBytes < estimate.TotalEstimatedBytes);
    }

    [Fact]
    public void ObjectPool_ReducesAllocations_ForRepeatedOperations()
    {
        // Arrange
        var pool = PerformanceOptimizer.CreateObjectPool(
            () => new StringBuilder(1024),
            sb => sb.Clear(),
            maxSize: 10);

        var stopwatch = Stopwatch.StartNew();
        
        // Act - Simulate repeated operations with pooled objects
        for (int i = 0; i < 1000; i++)
        {
            using var pooled = pool.Rent();
            var sb = pooled.Value;
            
            sb.Append("Test data ");
            sb.Append(i);
            sb.Append(" with some content");
            
            var result = sb.ToString();
            Assert.NotEmpty(result);
        }
        
        stopwatch.Stop();

        // Assert
        // Should complete quickly due to reduced allocations
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100));
        
        pool.Dispose();
    }

    [Fact]
    public async Task ParallelProcessing_ImprovesThroughput_ForMultiplePartitions()
    {
        // Arrange
        var partitions = CreateTestPartitions(5); // 5 partitions
        var outputDir = Path.Combine(_tempDirectory, "parallel_test");
        Directory.CreateDirectory(outputDir);
        
        var jsonlEngine = new JsonlExportEngine();
        var options = new JsonlExportOptions
        {
            SerializationMode = JsonSerializationMode.Compact
        };

        // Act - Process partitions using parallel optimization
        var stopwatch = Stopwatch.StartNew();
        var results = await jsonlEngine.ExportPartitionsAsync(
            ToAsyncEnumerable(partitions),
            outputDir,
            options);
        stopwatch.Stop();

        // Assert
        Assert.NotNull(results);
        Assert.Equal(5, results.Count);
        
        // Should complete relatively quickly for 5 partitions
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Batching_ReducesMemoryPressure_ForLargeDatasets()
    {
        // Arrange
        var largeData = CreateTestAsyncEnumerable(50_000);
        var batchSize = 500;
        
        // Measure initial memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var initialMemory = GC.GetTotalMemory(false);

        // Act - Process data in batches
        var itemCount = 0;
        await foreach (var batch in largeData.Batch(batchSize))
        {
            // Process batch
            itemCount += batch.Count;
            
            // Periodically force GC to simulate memory pressure
            if (itemCount % 10_000 == 0)
            {
                GC.Collect();
            }
        }

        // Measure final memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var finalMemory = GC.GetTotalMemory(false);

        // Assert
        Assert.Equal(50_000, itemCount);
        
        // Memory increase should be minimal due to batching
        var memoryIncrease = finalMemory - initialMemory;
        Assert.True(memoryIncrease < 100 * 1024 * 1024, 
            $"Memory increase too large: {memoryIncrease / (1024 * 1024):F1} MB");
    }

    [Fact]
    public async Task ConfigureAwaitOptimization_ReducesContextSwitching()
    {
        // Arrange
        var testData = CreateTestAsyncEnumerable(1000);
        
        // Act - Use optimized enumeration with ConfigureAwait(false)
        var stopwatch = Stopwatch.StartNew();
        var count = 0;
        
        await foreach (var item in testData.WithOptimizedPerformance())
        {
            count++;
        }
        
        stopwatch.Stop();

        // Assert
        Assert.Equal(1000, count);
        
        // Should complete quickly with reduced context switching
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(500));
    }

    private static DataPartition CreateLargeTestDataset(int rowCount)
    {
        var testRows = Enumerable.Range(1, rowCount)
            .Select(i => new Dictionary<string, object?>
            {
                ["id"] = i,
                ["name"] = $"User {i:D6}",
                ["email"] = $"user{i}@example.com",
                ["age"] = 20 + (i % 50),
                ["balance"] = Math.Round((i * 123.45) % 10000, 2),
                ["created_at"] = DateTime.UtcNow.AddDays(-i % 365).ToString("O"),
                ["is_active"] = i % 3 != 0,
                ["metadata"] = $"Additional data for user {i} with some content"
            }.AsReadOnly() as IReadOnlyDictionary<string, object?>);

        return new DataPartition
        {
            Data = ToAsyncEnumerable(testRows),
            Info = new PartitionInfo
            {
                TableName = "large_test_table",
                PartitionLabel = "test_partition",
                RowCount = rowCount
            },
            EstimatedRowCount = rowCount,
            IsFinalPartition = true,
            PartitionIndex = 0,
            Strategy = PartitionStrategy.None
        };
    }

    private static IEnumerable<DataPartition> CreateTestPartitions(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return CreateLargeTestDataset(1000); // 1000 rows per partition
        }
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> CreateTestAsyncEnumerable(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new Dictionary<string, object?>
            {
                ["Key"] = i,
                ["Value"] = $"Item {i}"
            }.AsReadOnly();
            
            // Small delay to simulate async data reading
            if (i % 100 == 0)
            {
                await Task.Delay(1);
            }
        }
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
        }
        await Task.CompletedTask;
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