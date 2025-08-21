using DB2XL.Core.Models;
using DB2XL.Export.Bundle.Extensions;
using DB2XL.Core.Services;
using DB2XL.Export.Bundle.Services;

namespace DB2XL.Export.Bundle.Tests.Services;

public class PartitioningTests
{
    #region Size-Based Partitioner Tests

    [Fact]
    public async Task SizeBasedPartitioner_WithLargeDataset_ShouldCreateMultiplePartitions()
    {
        // Arrange
        var partitioner = new SizeBasedPartitioner(rowsPerPartition: 3);
        var testData = GenerateTestData(10); // 10 rows, should create 4 partitions (3+3+3+1)
        var config = new TablePartitionConfig
        {
            TableName = "test_table",
            Strategy = PartitionStrategy.RowCount,
            RowsPerPartition = 3
        };

        // Act
        var partitions = new List<DataPartition>();
        await foreach (var partition in partitioner.PartitionDataAsync(testData, "test_table", config))
        {
            partitions.Add(partition);
        }

        // Assert
        Assert.Equal(4, partitions.Count);
        
        // Check partition sizes
        Assert.Equal(3, partitions[0].EstimatedRowCount);
        Assert.Equal(3, partitions[1].EstimatedRowCount);
        Assert.Equal(3, partitions[2].EstimatedRowCount);
        Assert.Equal(1, partitions[3].EstimatedRowCount); // Final partial partition
        
        // Check partition metadata
        Assert.Equal("p00001", partitions[0].Info.PartitionLabel);
        Assert.Equal("p00002", partitions[1].Info.PartitionLabel);
        Assert.Equal("p00003", partitions[2].Info.PartitionLabel);
        Assert.Equal("p00004", partitions[3].Info.PartitionLabel);
        
        // Check final partition flag
        Assert.False(partitions[0].IsFinalPartition);
        Assert.False(partitions[1].IsFinalPartition);
        Assert.False(partitions[2].IsFinalPartition);
        Assert.True(partitions[3].IsFinalPartition);
    }

    [Fact]
    public async Task SizeBasedPartitioner_WithEmptyData_ShouldCreateEmptyPartition()
    {
        // Arrange
        var partitioner = new SizeBasedPartitioner(rowsPerPartition: 100);
        var emptyData = AsyncEnumerableExtensions.EmptyAsync<IReadOnlyDictionary<string, object?>>();
        var config = new TablePartitionConfig
        {
            TableName = "empty_table",
            Strategy = PartitionStrategy.RowCount,
            RowsPerPartition = 100
        };

        // Act
        var partitions = new List<DataPartition>();
        await foreach (var partition in partitioner.PartitionDataAsync(emptyData, "empty_table", config))
        {
            partitions.Add(partition);
        }

        // Assert
        Assert.Single(partitions);
        Assert.Equal(0, partitions[0].EstimatedRowCount);
        Assert.True(partitions[0].IsFinalPartition);
        Assert.Equal("p00001", partitions[0].Info.PartitionLabel);
    }

    [Fact]
    public void SizeBasedPartitioner_EstimatePartitionCount_ShouldReturnCorrectEstimate()
    {
        // Arrange
        var partitioner = new SizeBasedPartitioner(rowsPerPartition: 1000);
        var metadata = new TableMetadata
        {
            TableName = "test_table",
            EstimatedRowCount = 3500
        };
        var config = new TablePartitionConfig
        {
            TableName = "test_table",
            Strategy = PartitionStrategy.RowCount,
            RowsPerPartition = 1000
        };

        // Act
        var estimatedCount = partitioner.EstimatePartitionCount(metadata, config);

        // Assert
        Assert.Equal(4, estimatedCount); // 3500 rows / 1000 = 3.5, rounded up to 4
    }

    [Fact]
    public void SizeBasedPartitioner_ValidateConfig_WithValidSettings_ShouldReturnSuccess()
    {
        // Arrange
        var partitioner = new SizeBasedPartitioner(rowsPerPartition: 50000);
        var metadata = new TableMetadata
        {
            TableName = "test_table",
            EstimatedRowCount = 100000
        };
        var config = new TablePartitionConfig
        {
            TableName = "test_table",
            Strategy = PartitionStrategy.RowCount,
            RowsPerPartition = 50000
        };

        // Act
        var result = partitioner.ValidatePartitionConfig(metadata, config);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void SizeBasedPartitioner_ValidateConfig_WithTooSmallPartitions_ShouldReturnWarnings()
    {
        // Arrange
        var partitioner = new SizeBasedPartitioner(rowsPerPartition: 500);
        var metadata = new TableMetadata
        {
            TableName = "test_table",
            EstimatedRowCount = 100000
        };
        var config = new TablePartitionConfig
        {
            TableName = "test_table",
            Strategy = PartitionStrategy.RowCount,
            RowsPerPartition = 500
        };

        // Act
        var result = partitioner.ValidatePartitionConfig(metadata, config);

        // Assert
        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("small partition size"));
    }

    #endregion

    #region Time-Based Partitioner Tests

    [Fact]
    public async Task TimeBasedPartitioner_WithDateColumn_ShouldPartitionByMonth()
    {
        // Arrange
        var partitioner = new TimeBasedPartitioner("created_at", TimePartitionGranularity.Month);
        var testData = GenerateTestDataWithDates();
        var config = new TablePartitionConfig
        {
            TableName = "events",
            Strategy = PartitionStrategy.TimeBased,
            TimeColumn = "created_at",
            TimeGranularity = TimePartitionGranularity.Month
        };

        // Act
        var partitions = new List<DataPartition>();
        await foreach (var partition in partitioner.PartitionDataAsync(testData, "events", config))
        {
            partitions.Add(partition);
        }

        // Assert
        Assert.NotEmpty(partitions);
        
        // Check that partitions have time-based labels
        Assert.All(partitions, p => 
        {
            Assert.Matches(@"\d{4}-\d{2}", p.Info.PartitionLabel); // YYYY-MM format
        });
        
        // Check strategy description
        Assert.All(partitions, p => 
        {
            Assert.Contains("by=time", p.Info.Strategy);
            Assert.Contains("column=created_at", p.Info.Strategy);
            Assert.Contains("granularity=Month", p.Info.Strategy);
        });
    }

    [Fact]
    public async Task TimeBasedPartitioner_WithNullDates_ShouldHandleGracefully()
    {
        // Arrange
        var partitioner = new TimeBasedPartitioner("created_at", TimePartitionGranularity.Month);
        var testData = GenerateTestDataWithNullDates();
        var config = new TablePartitionConfig
        {
            TableName = "events",
            Strategy = PartitionStrategy.TimeBased,
            TimeColumn = "created_at",
            TimeGranularity = TimePartitionGranularity.Month
        };

        // Act
        var partitions = new List<DataPartition>();
        await foreach (var partition in partitioner.PartitionDataAsync(testData, "events", config))
        {
            partitions.Add(partition);
        }

        // Assert
        Assert.NotEmpty(partitions);
        
        // Should have a partition for null dates
        Assert.Contains(partitions, p => p.Info.PartitionLabel == "null_dates");
    }

    [Fact]
    public void TimeBasedPartitioner_ValidateConfig_WithMissingTimeColumn_ShouldReturnError()
    {
        // Arrange
        var partitioner = new TimeBasedPartitioner("created_at", TimePartitionGranularity.Month);
        var metadata = new TableMetadata
        {
            TableName = "test_table",
            Columns = new[]
            {
                new ColumnMetadata { Name = "id", DeclaredType = "INTEGER" },
                new ColumnMetadata { Name = "name", DeclaredType = "TEXT" }
            }
        };
        var config = new TablePartitionConfig
        {
            TableName = "test_table",
            Strategy = PartitionStrategy.TimeBased,
            TimeColumn = "created_at",
            TimeGranularity = TimePartitionGranularity.Month
        };

        // Act
        var result = partitioner.ValidatePartitionConfig(metadata, config);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("not found"));
    }

    [Fact]
    public void TimeBasedPartitioner_GetRecommendedPartitioning_WithDateTimeColumn_ShouldRecommendTimeBased()
    {
        // Arrange
        var partitioner = new TimeBasedPartitioner("dummy", TimePartitionGranularity.Month);
        var metadata = new TableMetadata
        {
            TableName = "events",
            EstimatedRowCount = 500000,
            Columns = new[]
            {
                new ColumnMetadata { Name = "id", DeclaredType = "INTEGER" },
                new ColumnMetadata { Name = "created_at", DeclaredType = "DATETIME" },
                new ColumnMetadata { Name = "event_data", DeclaredType = "TEXT" }
            }
        };
        var exportOptions = new BundleExportOptions();

        // Act
        var config = partitioner.GetRecommendedPartitioning(metadata, exportOptions);

        // Assert
        Assert.Equal(PartitionStrategy.TimeBased, config.Strategy);
        Assert.Equal("created_at", config.TimeColumn);
    }

    #endregion

    #region Partition Coordinator Tests

    [Fact]
    public void PartitionCoordinator_CreatePartitioner_WithSizeStrategy_ShouldReturnSizeBasedPartitioner()
    {
        // Arrange
        var coordinator = new PartitionCoordinator();
        var config = new TablePartitionConfig
        {
            TableName = "test_table",
            Strategy = PartitionStrategy.RowCount,
            RowsPerPartition = 100000
        };

        // Act
        var partitioner = coordinator.CreatePartitioner(PartitionStrategy.RowCount, config);

        // Assert
        Assert.IsAssignableFrom<ISizeBasedPartitioner>(partitioner);
        var sizePartitioner = (ISizeBasedPartitioner)partitioner;
        Assert.Equal(100000, sizePartitioner.RowsPerPartition);
    }

    [Fact]
    public void PartitionCoordinator_CreatePartitioner_WithTimeStrategy_ShouldReturnTimeBasedPartitioner()
    {
        // Arrange
        var coordinator = new PartitionCoordinator();
        var config = new TablePartitionConfig
        {
            TableName = "test_table",
            Strategy = PartitionStrategy.TimeBased,
            TimeColumn = "created_at",
            TimeGranularity = TimePartitionGranularity.Quarter
        };

        // Act
        var partitioner = coordinator.CreatePartitioner(PartitionStrategy.TimeBased, config);

        // Assert
        Assert.IsAssignableFrom<ITimeBasedPartitioner>(partitioner);
        var timePartitioner = (ITimeBasedPartitioner)partitioner;
        Assert.Equal("created_at", timePartitioner.TimeColumn);
        Assert.Equal(TimePartitionGranularity.Quarter, timePartitioner.Granularity);
    }

    [Fact]
    public void PartitionCoordinator_RecommendPartitioningStrategy_WithSmallTable_ShouldRecommendNoPartitioning()
    {
        // Arrange
        var coordinator = new PartitionCoordinator();
        var metadata = new TableMetadata
        {
            TableName = "small_table",
            EstimatedRowCount = 10000, // Small table
            Columns = new[]
            {
                new ColumnMetadata { Name = "id", DeclaredType = "INTEGER" },
                new ColumnMetadata { Name = "data", DeclaredType = "TEXT" }
            }
        };
        var exportOptions = new BundleExportOptions();

        // Act
        var config = coordinator.RecommendPartitioningStrategy(metadata, exportOptions);

        // Assert
        Assert.Equal(PartitionStrategy.None, config.Strategy);
    }

    [Fact]
    public void PartitionCoordinator_GetSupportedStrategies_ShouldReturnAllStrategies()
    {
        // Arrange
        var coordinator = new PartitionCoordinator();

        // Act
        var strategies = coordinator.GetSupportedStrategies();

        // Assert
        Assert.Contains(PartitionStrategy.None, strategies);
        Assert.Contains(PartitionStrategy.RowCount, strategies);
        Assert.Contains(PartitionStrategy.TimeBased, strategies);
        Assert.Contains(PartitionStrategy.FilterBased, strategies);
    }

    [Fact]
    public void PartitionCoordinator_EstimateTotalPartitions_ShouldSumAllTablePartitions()
    {
        // Arrange
        var coordinator = new PartitionCoordinator();
        var tableConfigs = new[]
        {
            new TablePartitionConfig
            {
                TableName = "table1",
                Strategy = PartitionStrategy.RowCount,
                RowsPerPartition = 100000
            },
            new TablePartitionConfig
            {
                TableName = "table2",
                Strategy = PartitionStrategy.None
            }
        };
        var tableMetadata = new Dictionary<string, TableMetadata>
        {
            ["table1"] = new TableMetadata
            {
                TableName = "table1",
                EstimatedRowCount = 250000 // Should create 3 partitions
            },
            ["table2"] = new TableMetadata
            {
                TableName = "table2",
                EstimatedRowCount = 50000 // Should create 1 partition (no partitioning)
            }
        };

        // Act
        var totalPartitions = coordinator.EstimateTotalPartitions(tableConfigs, tableMetadata);

        // Assert
        Assert.Equal(4, totalPartitions); // 3 + 1
    }

    #endregion

    #region Helper Methods

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> GenerateTestData(int rowCount)
    {
        for (int i = 1; i <= rowCount; i++)
        {
            yield return new Dictionary<string, object?>
            {
                ["id"] = i,
                ["name"] = $"Record {i}",
                ["value"] = i * 10
            };
        }
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> GenerateTestDataWithDates()
    {
        var baseDates = new[]
        {
            new DateTime(2025, 1, 15),
            new DateTime(2025, 1, 28),
            new DateTime(2025, 2, 10),
            new DateTime(2025, 2, 25),
            new DateTime(2025, 3, 5)
        };

        for (int i = 0; i < baseDates.Length; i++)
        {
            yield return new Dictionary<string, object?>
            {
                ["id"] = i + 1,
                ["created_at"] = baseDates[i],
                ["event_type"] = $"Event {i + 1}"
            };
        }
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> GenerateTestDataWithNullDates()
    {
        var data = new[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = 1,
                ["created_at"] = new DateTime(2025, 1, 15),
                ["event_type"] = "Valid Date Event"
            },
            new Dictionary<string, object?>
            {
                ["id"] = 2,
                ["created_at"] = null,
                ["event_type"] = "Null Date Event"
            },
            new Dictionary<string, object?>
            {
                ["id"] = 3,
                ["created_at"] = new DateTime(2025, 2, 10),
                ["event_type"] = "Another Valid Date Event"
            }
        };

        foreach (var row in data)
        {
            yield return row;
        }
        await Task.CompletedTask;
    }

    #endregion
}