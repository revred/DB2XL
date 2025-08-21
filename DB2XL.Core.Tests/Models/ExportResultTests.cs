using DB2XL.Core.Models;

namespace DB2XL.Core.Tests.Models;

public class ExportResultTests
{
    [Fact]
    public void ExportResult_DefaultValues_AreSetCorrectly()
    {
        // Act
        var result = new ExportResult();

        // Assert
        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.OutputPath);
        Assert.Equal(0, result.TablesExported);
        Assert.Equal(0L, result.TotalRowsExported);
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Equal(0L, result.OutputSizeBytes);
        Assert.NotNull(result.TableResults);
        Assert.Empty(result.TableResults);
        Assert.NotNull(result.Warnings);
        Assert.Empty(result.Warnings);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ExportResult_InitProperties_CanBeSet()
    {
        // Arrange
        var duration = TimeSpan.FromMinutes(2);
        var tableResults = new List<TableExportResult>
        {
            new() { TableName = "Table1", RowCount = 100 },
            new() { TableName = "Table2", RowCount = 200 }
        };
        var warnings = new List<string> { "Warning 1", "Warning 2" };

        // Act
        var result = new ExportResult
        {
            Success = true,
            OutputPath = "/path/to/output.xlsx",
            TablesExported = 2,
            TotalRowsExported = 300L,
            Duration = duration,
            OutputSizeBytes = 1024L,
            TableResults = tableResults,
            Warnings = warnings,
            ErrorMessage = null
        };

        // Assert
        Assert.True(result.Success);
        Assert.Equal("/path/to/output.xlsx", result.OutputPath);
        Assert.Equal(2, result.TablesExported);
        Assert.Equal(300L, result.TotalRowsExported);
        Assert.Equal(duration, result.Duration);
        Assert.Equal(1024L, result.OutputSizeBytes);
        Assert.Equal(2, result.TableResults.Count);
        Assert.Equal("Table1", result.TableResults[0].TableName);
        Assert.Equal("Table2", result.TableResults[1].TableName);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Equal("Warning 1", result.Warnings[0]);
        Assert.Equal("Warning 2", result.Warnings[1]);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ExportResult_FailedExport_CanSetErrorMessage()
    {
        // Arrange
        const string errorMessage = "Database connection failed";

        // Act
        var result = new ExportResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            Duration = TimeSpan.FromSeconds(5)
        };

        // Assert
        Assert.False(result.Success);
        Assert.Equal(errorMessage, result.ErrorMessage);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Duration);
    }

    [Fact]
    public void ExportResult_LargeValues_CanBeSet()
    {
        // Arrange
        const long largeRowCount = long.MaxValue;
        const long largeFileSize = long.MaxValue - 1;
        var longDuration = TimeSpan.FromDays(365);

        // Act
        var result = new ExportResult
        {
            TotalRowsExported = largeRowCount,
            OutputSizeBytes = largeFileSize,
            Duration = longDuration
        };

        // Assert
        Assert.Equal(largeRowCount, result.TotalRowsExported);
        Assert.Equal(largeFileSize, result.OutputSizeBytes);
        Assert.Equal(longDuration, result.Duration);
    }
}