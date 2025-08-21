using DB2XL.Core.Models;

namespace DB2XL.Core.Tests.Models;

public class ValidationResultTests
{
    [Fact]
    public void ValidationResult_DefaultValues_AreSetCorrectly()
    {
        // Act
        var result = new ValidationResult();

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.Errors);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Warnings);
        Assert.Empty(result.Warnings);
        Assert.Null(result.EstimatedOutputSize);
        Assert.NotNull(result.TablesFound);
        Assert.Empty(result.TablesFound);
    }

    [Fact]
    public void ValidationResult_ValidResult_CanBeCreated()
    {
        // Arrange
        var tablesFound = new List<string> { "Table1", "Table2", "Table3" };
        const long estimatedSize = 1024L * 1024L; // 1MB

        // Act
        var result = new ValidationResult
        {
            IsValid = true,
            EstimatedOutputSize = estimatedSize,
            TablesFound = tablesFound
        };

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Equal(estimatedSize, result.EstimatedOutputSize);
        Assert.Equal(3, result.TablesFound.Count);
        Assert.Equal("Table1", result.TablesFound[0]);
        Assert.Equal("Table2", result.TablesFound[1]);
        Assert.Equal("Table3", result.TablesFound[2]);
    }

    [Fact]
    public void ValidationResult_InvalidResult_CanBeCreated()
    {
        // Arrange
        var errors = new List<string> { "Database not found", "Invalid connection string" };
        var warnings = new List<string> { "Large table detected" };

        // Act
        var result = new ValidationResult
        {
            IsValid = false,
            Errors = errors,
            Warnings = warnings
        };

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal("Database not found", result.Errors[0]);
        Assert.Equal("Invalid connection string", result.Errors[1]);
        Assert.Equal(1, result.Warnings.Count);
        Assert.Equal("Large table detected", result.Warnings[0]);
        Assert.Null(result.EstimatedOutputSize);
        Assert.Empty(result.TablesFound);
    }

    [Fact]
    public void ValidationResult_WithWarningsButValid_CanBeCreated()
    {
        // Arrange
        var warnings = new List<string> { "Performance warning", "Memory usage warning" };
        var tablesFound = new List<string> { "SmallTable" };

        // Act
        var result = new ValidationResult
        {
            IsValid = true,
            Warnings = warnings,
            TablesFound = tablesFound,
            EstimatedOutputSize = 1000L
        };

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Equal("Performance warning", result.Warnings[0]);
        Assert.Equal("Memory usage warning", result.Warnings[1]);
        Assert.Single(result.TablesFound);
        Assert.Equal("SmallTable", result.TablesFound[0]);
        Assert.Equal(1000L, result.EstimatedOutputSize);
    }

    [Fact]
    public void ValidationResult_NoTablesFound_CanBeCreated()
    {
        // Act
        var result = new ValidationResult
        {
            IsValid = false,
            Errors = new List<string> { "No tables match criteria" },
            TablesFound = new List<string>()
        };

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("No tables match criteria", result.Errors[0]);
        Assert.Empty(result.TablesFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(1024L)]
    [InlineData(long.MaxValue)]
    public void ValidationResult_WithDifferentEstimatedSizes_StoresCorrectly(long? estimatedSize)
    {
        // Act
        var result = new ValidationResult
        {
            IsValid = true,
            EstimatedOutputSize = estimatedSize
        };

        // Assert
        Assert.Equal(estimatedSize, result.EstimatedOutputSize);
    }

    [Fact]
    public void ValidationResult_CanModifyCollections()
    {
        // Arrange
        var result = new ValidationResult
        {
            Errors = new List<string> { "Initial error" },
            Warnings = new List<string> { "Initial warning" },
            TablesFound = new List<string> { "Initial table" }
        };

        // Act
        result.Errors.Add("Second error");
        result.Warnings.Add("Second warning");
        result.TablesFound.Add("Second table");

        // Assert
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal("Second error", result.Errors[1]);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Equal("Second warning", result.Warnings[1]);
        Assert.Equal(2, result.TablesFound.Count);
        Assert.Equal("Second table", result.TablesFound[1]);
    }
}