using DB2XL.Core.Models;

namespace DB2XL.Core.Tests.Models;

public class BundleModelsTests
{
    [Fact]
    public void BundleExportOptions_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var options = new BundleExportOptions();

        // Assert
        Assert.Equal(string.Empty, options.BundleRootPath);
        Assert.Equal("index.xlsx", options.IndexWorkbookName);
        Assert.Equal("manifest", options.ManifestDirectoryName);
        Assert.Equal("tables", options.TablesDirectoryName);
        Assert.False(options.GenerateParquet);
        Assert.False(options.IncludeSamples);
        Assert.Equal(10_000, options.SampleRowLimit);
        Assert.False(options.DeterministicTimestamps);
    }

    [Fact]
    public void BundleExportOptions_WithExpression_ShouldCreateCorrectly()
    {
        // Arrange & Act
        var options = new BundleExportOptions
        {
            BundleRootPath = @"C:\temp\bundle",
            IndexWorkbookName = "custom.xlsx",
            GenerateParquet = true,
            IncludeSamples = true,
            SampleRowLimit = 5000,
            DeterministicTimestamps = true
        };

        // Assert
        Assert.Equal(@"C:\temp\bundle", options.BundleRootPath);
        Assert.Equal("custom.xlsx", options.IndexWorkbookName);
        Assert.True(options.GenerateParquet);
        Assert.True(options.IncludeSamples);
        Assert.Equal(5000, options.SampleRowLimit);
        Assert.True(options.DeterministicTimestamps);
    }

    [Fact]
    public void BundleLayout_GetTableDirectory_ShouldSanitizeProperly()
    {
        // Arrange
        var layout = new BundleLayout
        {
            TablesPath = @"C:\bundle\tables"
        };

        // Act & Assert
        Assert.Equal(@"C:\bundle\tables\orders", layout.GetTableDirectory("orders"));
        Assert.Equal(@"C:\bundle\tables\user-events", layout.GetTableDirectory("user-events"));
        Assert.Equal(@"C:\bundle\tables\log_data", layout.GetTableDirectory("log/data"));
        Assert.Equal(@"C:\bundle\tables\data__file", layout.GetTableDirectory("data<>file"));
        Assert.Equal(@"C:\bundle\tables\_empty_", layout.GetTableDirectory(""));
        Assert.Equal(@"C:\bundle\tables\_empty_", layout.GetTableDirectory("   "));
    }

    [Theory]
    [InlineData("orders", "orders")]
    [InlineData("user-data", "user-data")]
    [InlineData("log/entries", "log_entries")]
    [InlineData("file<>name", "file__name")]
    [InlineData("data|pipe", "data_pipe")]
    [InlineData("query?test", "query_test")]
    [InlineData("path\\with\\slashes", "path_with_slashes")]
    [InlineData("name*with*stars", "name_with_stars")]
    [InlineData("", "_empty_")]
    [InlineData("   ", "_empty_")]
    [InlineData("\t\n\r", "_empty_")]
    public void BundleLayout_PathSanitization_ShouldHandleEdgeCases(string input, string expected)
    {
        // Arrange
        var layout = new BundleLayout { TablesPath = @"C:\tables" };

        // Act
        var result = layout.GetTableDirectory(input);

        // Assert
        Assert.Equal($@"C:\tables\{expected}", result);
    }

    [Fact]
    public void PartitionInfo_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var partition = new PartitionInfo();

        // Assert
        Assert.Equal(string.Empty, partition.TableName);
        Assert.Equal(string.Empty, partition.PartitionLabel);
        Assert.Equal(string.Empty, partition.Strategy);
        Assert.Equal(0, partition.RowCount);
        Assert.Equal(string.Empty, partition.RelativePath);
        Assert.Equal(string.Empty, partition.Sha256Hash);
        Assert.Null(partition.FirstPrimaryKey);
        Assert.Null(partition.LastPrimaryKey);
        Assert.Equal("jsonl", partition.Format);
        Assert.Equal(0, partition.FileSizeBytes);
    }

    [Fact]
    public void PartitionInfo_WithData_ShouldCreateCorrectly()
    {
        // Arrange & Act
        var partition = new PartitionInfo
        {
            TableName = "orders",
            PartitionLabel = "2025Q1",
            Strategy = "by=quarter,field=created_at",
            RowCount = 50000,
            RelativePath = "tables/orders/orders_2025Q1.jsonl",
            Sha256Hash = "abc123def456",
            FirstPrimaryKey = "1",
            LastPrimaryKey = "50000",
            Format = "jsonl",
            FileSizeBytes = 1024000
        };

        // Assert
        Assert.Equal("orders", partition.TableName);
        Assert.Equal("2025Q1", partition.PartitionLabel);
        Assert.Equal("by=quarter,field=created_at", partition.Strategy);
        Assert.Equal(50000, partition.RowCount);
        Assert.Equal("tables/orders/orders_2025Q1.jsonl", partition.RelativePath);
        Assert.Equal("abc123def456", partition.Sha256Hash);
        Assert.Equal("1", partition.FirstPrimaryKey);
        Assert.Equal("50000", partition.LastPrimaryKey);
        Assert.Equal("jsonl", partition.Format);
        Assert.Equal(1024000, partition.FileSizeBytes);
    }

    [Fact]
    public void TablePartitionConfig_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var config = new TablePartitionConfig();

        // Assert
        Assert.Equal(string.Empty, config.TableName);
        Assert.Equal(PartitionStrategy.None, config.Strategy);
        Assert.Equal(200_000, config.RowsPerPartition);
        Assert.Null(config.TimeColumn);
        Assert.Equal(TimePartitionGranularity.Month, config.TimeGranularity);
        Assert.Null(config.FilterExpression);
        Assert.Null(config.FilterLabel);
    }

    [Fact]
    public void TablePartitionConfig_WithRowCountStrategy_ShouldCreateCorrectly()
    {
        // Arrange & Act
        var config = new TablePartitionConfig
        {
            TableName = "large_table",
            Strategy = PartitionStrategy.RowCount,
            RowsPerPartition = 100_000
        };

        // Assert
        Assert.Equal("large_table", config.TableName);
        Assert.Equal(PartitionStrategy.RowCount, config.Strategy);
        Assert.Equal(100_000, config.RowsPerPartition);
    }

    [Fact]
    public void TablePartitionConfig_WithTimeBasedStrategy_ShouldCreateCorrectly()
    {
        // Arrange & Act
        var config = new TablePartitionConfig
        {
            TableName = "events",
            Strategy = PartitionStrategy.TimeBased,
            TimeColumn = "created_at",
            TimeGranularity = TimePartitionGranularity.Day
        };

        // Assert
        Assert.Equal("events", config.TableName);
        Assert.Equal(PartitionStrategy.TimeBased, config.Strategy);
        Assert.Equal("created_at", config.TimeColumn);
        Assert.Equal(TimePartitionGranularity.Day, config.TimeGranularity);
    }

    [Fact]
    public void TablePartitionConfig_WithFilterBasedStrategy_ShouldCreateCorrectly()
    {
        // Arrange & Act
        var config = new TablePartitionConfig
        {
            TableName = "logs",
            Strategy = PartitionStrategy.FilterBased,
            FilterExpression = "level IN ('WARN', 'ERROR')",
            FilterLabel = "WARN_ERROR"
        };

        // Assert
        Assert.Equal("logs", config.TableName);
        Assert.Equal(PartitionStrategy.FilterBased, config.Strategy);
        Assert.Equal("level IN ('WARN', 'ERROR')", config.FilterExpression);
        Assert.Equal("WARN_ERROR", config.FilterLabel);
    }

    [Fact]
    public void BundleLayout_RecordEquality_ShouldWorkCorrectly()
    {
        // Arrange
        var layout1 = new BundleLayout
        {
            RootPath = @"C:\bundle",
            IndexWorkbookPath = @"C:\bundle\index.xlsx",
            ExportTimestamp = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        var layout2 = new BundleLayout
        {
            RootPath = @"C:\bundle",
            IndexWorkbookPath = @"C:\bundle\index.xlsx",
            ExportTimestamp = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        var layout3 = new BundleLayout
        {
            RootPath = @"C:\different",
            IndexWorkbookPath = @"C:\different\index.xlsx",
            ExportTimestamp = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };

        // Act & Assert
        Assert.Equal(layout1, layout2);
        Assert.NotEqual(layout1, layout3);
        Assert.True(layout1 == layout2);
        Assert.False(layout1 == layout3);
    }

    [Fact]
    public void PartitionInfo_RecordEquality_ShouldWorkCorrectly()
    {
        // Arrange
        var partition1 = new PartitionInfo
        {
            TableName = "orders",
            PartitionLabel = "2025Q1",
            RowCount = 1000
        };

        var partition2 = new PartitionInfo
        {
            TableName = "orders",
            PartitionLabel = "2025Q1",
            RowCount = 1000
        };

        var partition3 = new PartitionInfo
        {
            TableName = "orders",
            PartitionLabel = "2025Q2",
            RowCount = 1000
        };

        // Act & Assert
        Assert.Equal(partition1, partition2);
        Assert.NotEqual(partition1, partition3);
        Assert.Equal(partition1.GetHashCode(), partition2.GetHashCode());
        Assert.NotEqual(partition1.GetHashCode(), partition3.GetHashCode());
    }
}