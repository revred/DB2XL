using DB2XL.Core.Models;

namespace DB2XL.Core.Tests.Models;

public class TableExportResultTests
{
    [Fact]
    public void TableExportResult_DefaultValues_AreSetCorrectly()
    {
        // Act
        var result = new TableExportResult();

        // Assert
        Assert.Equal(string.Empty, result.TableName);
        Assert.Equal(0L, result.RowCount);
        Assert.Equal(0, result.ColumnCount);
        Assert.Null(result.Checksum);
        Assert.False(result.WasSplit);
        Assert.Equal(0, result.SplitParts);
    }

    [Fact]
    public void TableExportResult_InitProperties_CanBeSet()
    {
        // Arrange
        const string tableName = "TestTable";
        const long rowCount = 1000L;
        const int columnCount = 5;
        const string checksum = "abcd1234567890ef";
        const bool wasSplit = true;
        const int splitParts = 3;

        // Act
        var result = new TableExportResult
        {
            TableName = tableName,
            RowCount = rowCount,
            ColumnCount = columnCount,
            Checksum = checksum,
            WasSplit = wasSplit,
            SplitParts = splitParts
        };

        // Assert
        Assert.Equal(tableName, result.TableName);
        Assert.Equal(rowCount, result.RowCount);
        Assert.Equal(columnCount, result.ColumnCount);
        Assert.Equal(checksum, result.Checksum);
        Assert.Equal(wasSplit, result.WasSplit);
        Assert.Equal(splitParts, result.SplitParts);
    }

    [Fact]
    public void TableExportResult_SingleTableNotSplit_CorrectValues()
    {
        // Arrange & Act
        var result = new TableExportResult
        {
            TableName = "SingleTable",
            RowCount = 500L,
            ColumnCount = 10,
            Checksum = "sha256hash",
            WasSplit = false,
            SplitParts = 1
        };

        // Assert
        Assert.Equal("SingleTable", result.TableName);
        Assert.Equal(500L, result.RowCount);
        Assert.Equal(10, result.ColumnCount);
        Assert.Equal("sha256hash", result.Checksum);
        Assert.False(result.WasSplit);
        Assert.Equal(1, result.SplitParts);
    }

    [Fact]
    public void TableExportResult_LargeTable_CanHandleLargeValues()
    {
        // Arrange
        const long maxRowCount = long.MaxValue;
        const int maxColumnCount = int.MaxValue;

        // Act
        var result = new TableExportResult
        {
            TableName = "LargeTable",
            RowCount = maxRowCount,
            ColumnCount = maxColumnCount,
            WasSplit = true,
            SplitParts = 1000
        };

        // Assert
        Assert.Equal("LargeTable", result.TableName);
        Assert.Equal(maxRowCount, result.RowCount);
        Assert.Equal(maxColumnCount, result.ColumnCount);
        Assert.True(result.WasSplit);
        Assert.Equal(1000, result.SplitParts);
    }

    [Fact]
    public void TableExportResult_EmptyTableName_CanBeSet()
    {
        // Act
        var result = new TableExportResult
        {
            TableName = "",
            RowCount = 0L,
            ColumnCount = 0
        };

        // Assert
        Assert.Equal("", result.TableName);
        Assert.Equal(0L, result.RowCount);
        Assert.Equal(0, result.ColumnCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abcd1234")]
    [InlineData("a1b2c3d4e5f6789012345678901234567890abcdef")]
    public void TableExportResult_WithDifferentChecksums_StoresCorrectly(string? checksum)
    {
        // Act
        var result = new TableExportResult
        {
            TableName = "TestTable",
            Checksum = checksum
        };

        // Assert
        Assert.Equal(checksum, result.Checksum);
    }
}