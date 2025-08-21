using DB2XL.Core.Models;
using DB2XL.Core.Validation;
using ValidationResult = DB2XL.Core.Validation.ValidationResult;

namespace DB2XL.Core.Tests.Validation;

public class BundleExportValidatorTests
{
    private readonly BundleExportValidator _validator;

    public BundleExportValidatorTests()
    {
        _validator = new BundleExportValidator();
    }

    [Fact]
    public void Validate_WithValidOptions_ShouldReturnSuccess()
    {
        // Arrange
        var options = new BundleExportOptions
        {
            BundleRootPath = @"C:\temp\bundle",
            IndexWorkbookName = "test.xlsx",
            ManifestDirectoryName = "manifests",
            TablesDirectoryName = "data",
            SampleRowLimit = 5000
        };

        // Act
        var result = _validator.Validate(options);

        // Assert - Debug output for failure
        if (!result.IsValid)
        {
            var errors = string.Join("; ", result.Errors);
            Assert.True(result.IsValid, $"Validation failed with errors: {errors}");
        }
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_WithNullOptions_ShouldReturnFailure()
    {
        // Act
        var result = _validator.Validate(null!);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("Bundle export options cannot be null", result.Errors[0]);
    }

    [Fact]
    public void Validate_WithEmptyBundleRootPath_ShouldReturnSuccess()
    {
        // Arrange - Empty root path is valid (uses temp directory)
        var options = new BundleExportOptions
        {
            BundleRootPath = string.Empty,
            IndexWorkbookName = "test.xlsx"
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.True(result.IsValid);
    }

    #region Index Workbook Name Tests

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithInvalidIndexWorkbookName_ShouldReturnFailure(string indexWorkbookName)
    {
        // Arrange
        var options = new BundleExportOptions { IndexWorkbookName = indexWorkbookName };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Index workbook name cannot be null or empty", result.Errors);
    }

    [Fact]
    public void Validate_WithoutXlsxExtension_ShouldReturnFailure()
    {
        // Arrange
        var options = new BundleExportOptions { IndexWorkbookName = "test.txt" };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Index workbook name must have .xlsx extension", result.Errors);
    }

    [Fact]
    public void Validate_WithTooLongIndexWorkbookName_ShouldReturnFailure()
    {
        // Arrange
        var longName = new string('a', 260) + ".xlsx";
        var options = new BundleExportOptions { IndexWorkbookName = longName };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Index workbook name cannot exceed 255 characters", result.Errors);
    }

    [Theory]
    [InlineData("test<>.xlsx")]
    [InlineData("test|file.xlsx")]
    [InlineData("test?name.xlsx")]
    [InlineData("test*file.xlsx")]
    public void Validate_WithInvalidCharactersInIndexWorkbookName_ShouldReturnFailure(string indexWorkbookName)
    {
        // Arrange
        var options = new BundleExportOptions { IndexWorkbookName = indexWorkbookName };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Index workbook name contains invalid characters", result.Errors);
    }

    [Fact]
    public void Validate_WithOnlyExtensionIndexWorkbook_ShouldReturnFailure()
    {
        // Arrange
        var options = new BundleExportOptions { IndexWorkbookName = ".xlsx" };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Index workbook name must have a valid filename before the extension", result.Errors);
    }

    #endregion

    #region Directory Name Tests

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithInvalidManifestDirectoryName_ShouldReturnFailure(string directoryName)
    {
        // Arrange
        var options = new BundleExportOptions { ManifestDirectoryName = directoryName };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Manifest directory name cannot be null or empty", result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithInvalidTablesDirectoryName_ShouldReturnFailure(string directoryName)
    {
        // Arrange
        var options = new BundleExportOptions { TablesDirectoryName = directoryName };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Tables directory name cannot be null or empty", result.Errors);
    }

    [Fact]
    public void Validate_WithSameManifestAndTablesDirectoryNames_ShouldReturnFailure()
    {
        // Arrange
        var options = new BundleExportOptions 
        { 
            ManifestDirectoryName = "data",
            TablesDirectoryName = "data"
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Manifest and tables directory names cannot be the same", result.Errors);
    }

    [Fact]
    public void Validate_WithManifestDirectoryConflictingWithWorkbook_ShouldReturnFailure()
    {
        // Arrange
        var options = new BundleExportOptions 
        { 
            IndexWorkbookName = "report.xlsx",
            ManifestDirectoryName = "report"
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Manifest directory name conflicts with index workbook filename", result.Errors);
    }

    [Fact]
    public void Validate_WithTablesDirectoryConflictingWithWorkbook_ShouldReturnFailure()
    {
        // Arrange
        var options = new BundleExportOptions 
        { 
            IndexWorkbookName = "export.xlsx",
            TablesDirectoryName = "export"
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Tables directory name conflicts with index workbook filename", result.Errors);
    }

    [Fact]
    public void Validate_WithTooLongDirectoryName_ShouldReturnFailure()
    {
        // Arrange
        var longName = new string('a', 260);
        var options = new BundleExportOptions { ManifestDirectoryName = longName };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Manifest directory name cannot exceed 255 characters", result.Errors);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT1")]
    public void Validate_WithReservedDirectoryNames_ShouldReturnFailure(string reservedName)
    {
        // Arrange
        var options = new BundleExportOptions { ManifestDirectoryName = reservedName };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Manifest directory name uses a reserved system name", result.Errors);
    }

    [Theory]
    [InlineData(".hidden")]
    [InlineData("dir.")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    public void Validate_WithInvalidDirectoryNameFormat_ShouldReturnFailure(string directoryName)
    {
        // Arrange
        var options = new BundleExportOptions { ManifestDirectoryName = directoryName };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Manifest directory name cannot start or end with dots or spaces", result.Errors);
    }

    #endregion

    #region Bundle Root Path Tests

    [Fact]
    public void Validate_WithTooLongBundleRootPath_ShouldReturnFailure()
    {
        // Arrange
        var longPath = @"C:\" + new string('a', 260);
        var options = new BundleExportOptions { BundleRootPath = longPath };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Bundle root path cannot exceed 260 characters", result.Errors);
    }

    [Fact]
    public void Validate_WithInvalidBundleRootPathCharacters_ShouldReturnFailure()
    {
        // Arrange - Use characters that are actually invalid for paths
        var options = new BundleExportOptions { BundleRootPath = "C:\\invalid\u0001path" };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Bundle root path contains invalid characters", result.Errors);
    }

    #endregion

    #region Sample Configuration Tests

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Validate_WithInvalidSampleRowLimit_ShouldReturnFailure(int sampleRowLimit)
    {
        // Arrange
        var options = new BundleExportOptions { SampleRowLimit = sampleRowLimit };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Sample row limit must be greater than 0", result.Errors);
    }

    [Fact]
    public void Validate_WithTooLargeSampleRowLimit_ShouldReturnFailure()
    {
        // Arrange
        var options = new BundleExportOptions { SampleRowLimit = 2_000_000 };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Sample row limit cannot exceed 1,000,000 rows for performance reasons", result.Errors);
    }

    [Fact]
    public void Validate_WithNoSamplesAndNoParquet_ShouldReturnSuccess()
    {
        // Arrange - This is valid for JSONL-only exports
        var options = new BundleExportOptions 
        { 
            IncludeSamples = false,
            GenerateParquet = false
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion

    #region Partition Configuration Tests

    [Fact]
    public void ValidatePartitionConfig_WithNullConfig_ShouldReturnFailure()
    {
        // Act
        var result = _validator.ValidatePartitionConfig(null!);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal("Partition configuration cannot be null", result.Errors[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidatePartitionConfig_WithInvalidTableName_ShouldReturnFailure(string tableName)
    {
        // Arrange
        var config = new TablePartitionConfig { TableName = tableName };

        // Act
        var result = _validator.ValidatePartitionConfig(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Table name cannot be null or empty", result.Errors);
    }

    [Fact]
    public void ValidatePartitionConfig_WithTooLongTableName_ShouldReturnFailure()
    {
        // Arrange
        var longTableName = new string('a', 130);
        var config = new TablePartitionConfig { TableName = longTableName };

        // Act
        var result = _validator.ValidatePartitionConfig(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Table name cannot exceed 128 characters", result.Errors);
    }

    [Fact]
    public void ValidatePartitionConfig_WithValidNoneStrategy_ShouldReturnSuccess()
    {
        // Arrange
        var config = new TablePartitionConfig 
        { 
            TableName = "valid_table",
            Strategy = PartitionStrategy.None
        };

        // Act
        var result = _validator.ValidatePartitionConfig(config);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidatePartitionConfig_WithInvalidRowCountStrategy_ShouldReturnFailure(int rowsPerPartition)
    {
        // Arrange
        var config = new TablePartitionConfig 
        { 
            TableName = "test_table",
            Strategy = PartitionStrategy.RowCount,
            RowsPerPartition = rowsPerPartition
        };

        // Act
        var result = _validator.ValidatePartitionConfig(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Rows per partition must be greater than 0 for RowCount strategy", result.Errors);
    }

    [Fact]
    public void ValidatePartitionConfig_WithTooLargeRowCountStrategy_ShouldReturnFailure()
    {
        // Arrange
        var config = new TablePartitionConfig 
        { 
            TableName = "test_table",
            Strategy = PartitionStrategy.RowCount,
            RowsPerPartition = 20_000_000
        };

        // Act
        var result = _validator.ValidatePartitionConfig(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Rows per partition cannot exceed 10,000,000 for performance reasons", result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidatePartitionConfig_WithTimeBasedStrategyMissingColumn_ShouldReturnFailure(string timeColumn)
    {
        // Arrange
        var config = new TablePartitionConfig 
        { 
            TableName = "events",
            Strategy = PartitionStrategy.TimeBased,
            TimeColumn = timeColumn
        };

        // Act
        var result = _validator.ValidatePartitionConfig(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Time column name is required for TimeBased strategy", result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidatePartitionConfig_WithFilterBasedStrategyMissingExpression_ShouldReturnFailure(string filterExpression)
    {
        // Arrange
        var config = new TablePartitionConfig 
        { 
            TableName = "logs",
            Strategy = PartitionStrategy.FilterBased,
            FilterExpression = filterExpression,
            FilterLabel = "ERRORS"
        };

        // Act
        var result = _validator.ValidatePartitionConfig(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Filter expression is required for FilterBased strategy", result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidatePartitionConfig_WithFilterBasedStrategyMissingLabel_ShouldReturnFailure(string filterLabel)
    {
        // Arrange
        var config = new TablePartitionConfig 
        { 
            TableName = "logs",
            Strategy = PartitionStrategy.FilterBased,
            FilterExpression = "level = 'ERROR'",
            FilterLabel = filterLabel
        };

        // Act
        var result = _validator.ValidatePartitionConfig(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Filter label is required for FilterBased strategy", result.Errors);
    }

    #endregion

    #region Filter Expression Validation Tests

    [Theory]
    [InlineData("DROP TABLE users")]
    [InlineData("DELETE FROM logs")]
    [InlineData("UPDATE SET value = 1")]
    [InlineData("INSERT INTO table VALUES (1)")]
    [InlineData("EXEC sp_dangerous")]
    [InlineData("EXECUTE dangerous_proc")]
    [InlineData("UNION SELECT * FROM secrets")]
    [InlineData("-- malicious comment")]
    [InlineData("/* block comment */")]
    public void ValidatePartitionConfig_WithSuspiciousFilterExpression_ShouldReturnFailure(string suspiciousFilter)
    {
        // Arrange
        var config = new TablePartitionConfig 
        { 
            TableName = "logs",
            Strategy = PartitionStrategy.FilterBased,
            FilterExpression = suspiciousFilter,
            FilterLabel = "TEST"
        };

        // Act
        var result = _validator.ValidatePartitionConfig(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("potentially unsafe SQL"));
    }

    [Theory]
    [InlineData("level = 'ERROR' AND (")]
    [InlineData("level = 'ERROR') AND extra")]
    [InlineData("((level = 'ERROR')")]
    public void ValidatePartitionConfig_WithUnbalancedParentheses_ShouldReturnFailure(string filterExpression)
    {
        // Arrange
        var config = new TablePartitionConfig 
        { 
            TableName = "logs",
            Strategy = PartitionStrategy.FilterBased,
            FilterExpression = filterExpression,
            FilterLabel = "ERRORS"
        };

        // Act
        var result = _validator.ValidatePartitionConfig(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Filter expression has unbalanced parentheses", result.Errors);
    }

    [Fact]
    public void ValidatePartitionConfig_WithTooLongFilterExpression_ShouldReturnFailure()
    {
        // Arrange
        var longFilter = new string('a', 1001);
        var config = new TablePartitionConfig 
        { 
            TableName = "logs",
            Strategy = PartitionStrategy.FilterBased,
            FilterExpression = longFilter,
            FilterLabel = "LONG"
        };

        // Act
        var result = _validator.ValidatePartitionConfig(config);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Filter expression cannot exceed 1000 characters", result.Errors);
    }

    [Fact]
    public void ValidatePartitionConfig_WithValidFilterExpression_ShouldReturnSuccess()
    {
        // Arrange
        var config = new TablePartitionConfig 
        { 
            TableName = "logs",
            Strategy = PartitionStrategy.FilterBased,
            FilterExpression = "level IN ('WARN', 'ERROR') AND timestamp > '2025-01-01'",
            FilterLabel = "WARN_ERROR_RECENT"
        };

        // Act
        var result = _validator.ValidatePartitionConfig(config);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    #endregion

    #region ValidationResult Tests

    [Fact]
    public void ValidationResult_Success_ShouldHaveCorrectProperties()
    {
        // Act
        var result = ValidationResult.Success();

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidationResult_FailureWithSingleError_ShouldHaveCorrectProperties()
    {
        // Arrange
        var errorMessage = "Test error";

        // Act
        var result = ValidationResult.Failure(errorMessage);

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Equal(errorMessage, result.Errors[0]);
    }

    [Fact]
    public void ValidationResult_FailureWithMultipleErrors_ShouldHaveCorrectProperties()
    {
        // Arrange
        var errors = new[] { "Error 1", "Error 2", "Error 3" };

        // Act
        var result = ValidationResult.Failure(errors);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
        Assert.Equal(errors, result.Errors);
    }

    [Fact]
    public void ValidationResult_FailureWithEnumerable_ShouldHaveCorrectProperties()
    {
        // Arrange
        var errors = new List<string> { "Error A", "Error B" };

        // Act
        var result = ValidationResult.Failure(errors.AsEnumerable());

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal("Error A", result.Errors[0]);
        Assert.Equal("Error B", result.Errors[1]);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void Validate_WithMultipleValidationErrors_ShouldReturnAllErrors()
    {
        // Arrange
        var options = new BundleExportOptions
        {
            IndexWorkbookName = "invalid.txt", // Missing .xlsx extension
            ManifestDirectoryName = "data",
            TablesDirectoryName = "data", // Same as manifest directory
            SampleRowLimit = -1 // Invalid sample limit
        };

        // Act
        var result = _validator.Validate(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.True(result.Errors.Count >= 3);
        Assert.Contains(result.Errors, e => e.Contains(".xlsx extension"));
        Assert.Contains(result.Errors, e => e.Contains("cannot be the same"));
        Assert.Contains(result.Errors, e => e.Contains("must be greater than 0"));
    }

    #endregion
}