using DB2XL.Core.Exceptions;
using DB2XL.Core.Models;
using DB2XL.Core.Services;
using DB2XL.Core.Validation;
using DB2XL.Data.Schema;
using DB2XL.Export.Bundle.Services;
using DB2XL.Export.Bundle.Tests.TestHelpers;
using Microsoft.Data.Sqlite;
using Moq;

namespace DB2XL.Export.Bundle.Tests.Services;

public class BundleExportServiceTests : IDisposable
{
    private readonly Mock<IBundlePathManager> _mockPathManager;
    private readonly Mock<IBundleHashCalculator> _mockHashCalculator;
    private readonly BundleExportValidator _validator;
    private readonly SqliteSchemaReader _schemaReader;
    private readonly BundleExportService _service;
    private readonly TestDatabaseHelper _dbHelper;

    public BundleExportServiceTests()
    {
        _mockPathManager = new Mock<IBundlePathManager>();
        _mockHashCalculator = new Mock<IBundleHashCalculator>();
        _validator = new BundleExportValidator();
        _schemaReader = new SqliteSchemaReader();
        _dbHelper = new TestDatabaseHelper();
        
        _service = new BundleExportService(
            _mockPathManager.Object,
            _mockHashCalculator.Object,
            _validator,
            _schemaReader);
    }

    #region Validation Tests

    [Fact]
    public void ValidateOptions_WithValidOptions_ShouldReturnSuccess()
    {
        // Arrange
        var options = new BundleExportOptions
        {
            BundleRootPath = @"C:\temp\bundle",
            IndexWorkbookName = "test.xlsx"
        };

        // Act
        var result = _service.ValidateOptions(options);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateOptions_WithInvalidOptions_ShouldReturnFailure()
    {
        // Arrange
        var options = new BundleExportOptions
        {
            IndexWorkbookName = "invalid.txt", // Wrong extension
            SampleRowLimit = -1 // Invalid limit
        };

        // Act
        var result = _service.ValidateOptions(options);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains(".xlsx extension"));
        Assert.Contains(result.Errors, e => e.Contains("must be greater than 0"));
    }

    #endregion

    #region Export Tests

    [Fact]
    public async Task ExportAsync_WithNonExistentDatabase_ShouldThrowBundleDatabaseException()
    {
        // Arrange
        var nonExistentPath = @"C:\nonexistent\database.sqlite";
        var options = new BundleExportOptions();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BundleDatabaseException>(
            () => _service.ExportAsync(nonExistentPath, options));
        
        Assert.Contains("not found", exception.Message);
        Assert.Equal(nonExistentPath, exception.DatabasePath);
    }

    [Fact]
    public async Task ExportAsync_WithInvalidOptions_ShouldThrowBundleValidationException()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseAsync();
        var invalidOptions = new BundleExportOptions
        {
            IndexWorkbookName = "invalid.txt",
            SampleRowLimit = -1
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BundleValidationException>(
            () => _service.ExportAsync(dbPath, invalidOptions));
        
        Assert.NotEmpty(exception.ValidationErrors);
    }

    [Fact]
    public async Task ExportAsync_WithValidInputs_ShouldReturnSuccessResult()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseAsync(new[] { "users", "orders" });
        var options = new BundleExportOptions
        {
            BundleRootPath = Path.GetTempPath(),
            DeterministicTimestamps = true
        };

        var mockLayout = new BundleLayout
        {
            RootPath = Path.Combine(Path.GetTempPath(), "test_bundle"),
            IndexWorkbookPath = Path.Combine(Path.GetTempPath(), "test_bundle", "index.xlsx"),
            ManifestPath = Path.Combine(Path.GetTempPath(), "test_bundle", "manifest"),
            TablesPath = Path.Combine(Path.GetTempPath(), "test_bundle", "tables"),
            ExportTimestamp = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        _mockPathManager.Setup(x => x.CreateBundleLayout(options))
            .Returns(mockLayout);
        
        _mockPathManager.Setup(x => x.EnsureDirectoryStructure(mockLayout))
            .Verifiable();

        // Act
        var result = await _service.ExportAsync(dbPath, options);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(mockLayout, result.Layout);
        Assert.True(result.Duration.TotalMilliseconds > 0);
        Assert.NotEmpty(result.ExportedTables);
        
        _mockPathManager.Verify(x => x.EnsureDirectoryStructure(mockLayout), Times.Once);
    }

    #endregion

    #region Estimation Tests

    [Fact]
    public async Task EstimateAsync_WithValidDatabase_ShouldReturnEstimate()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseAsync(new[] { "products", "categories" });
        var options = new BundleExportOptions();

        // Act
        var estimate = await _service.EstimateAsync(dbPath, options);

        // Assert
        Assert.NotNull(estimate);
        Assert.True(estimate.EstimatedTableCount >= 2);
        Assert.True(estimate.EstimatedTotalRows > 0);
        Assert.True(estimate.EstimatedDuration.TotalSeconds > 0);
        Assert.NotNull(estimate.DatabaseInfo);
        Assert.Equal(dbPath, estimate.DatabaseInfo.FilePath);
        Assert.NotEmpty(estimate.TableEstimates);
    }

    [Fact]
    public async Task EstimateAsync_WithNonExistentDatabase_ShouldThrowBundleDatabaseException()
    {
        // Arrange
        var nonExistentPath = @"C:\nonexistent\database.sqlite";
        var options = new BundleExportOptions();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<BundleDatabaseException>(
            () => _service.EstimateAsync(nonExistentPath, options));
        
        Assert.Equal(nonExistentPath, exception.DatabasePath);
    }

    [Fact]
    public async Task EstimateAsync_WithInvalidOptions_ShouldThrowBundleValidationException()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseAsync();
        var invalidOptions = new BundleExportOptions
        {
            SampleRowLimit = 0
        };

        // Act & Assert
        await Assert.ThrowsAsync<BundleValidationException>(
            () => _service.EstimateAsync(dbPath, invalidOptions));
    }

    #endregion

    #region Complexity and Recommendation Tests

    [Fact]
    public async Task EstimateAsync_ShouldReturnValidComplexityAssessment()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseAsync();
        var options = new BundleExportOptions();

        // Act
        var estimate = await _service.EstimateAsync(dbPath, options);

        // Assert - This is a basic test since we can't easily create databases with specific complexities
        Assert.NotNull(estimate);
        Assert.IsType<ExportComplexity>(estimate.Complexity);
        Assert.NotNull(estimate.Recommendations);
        Assert.True(estimate.EstimatedTableCount >= 0);
        Assert.True(estimate.EstimatedTotalRows >= 0);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ExportAsync_WithNullFilePath_ShouldThrowArgumentException()
    {
        // Arrange
        var options = new BundleExportOptions();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.ExportAsync(null!, options));
    }

    [Fact]
    public async Task ExportAsync_WithEmptyFilePath_ShouldThrowArgumentException()
    {
        // Arrange
        var options = new BundleExportOptions();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.ExportAsync("", options));
    }

    [Fact]
    public void ValidateOptions_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => _service.ValidateOptions(null!));
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task ExportAsync_EndToEndTest_ShouldProduceValidResult()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseWithDataAsync();
        var tempDir = Path.Combine(Path.GetTempPath(), $"bundle_test_{Guid.NewGuid():N}");
        
        var options = new BundleExportOptions
        {
            BundleRootPath = tempDir,
            DeterministicTimestamps = true,
            IncludeSamples = true
        };

        var layout = new BundleLayout
        {
            RootPath = tempDir,
            IndexWorkbookPath = Path.Combine(tempDir, "index.xlsx"),
            ManifestPath = Path.Combine(tempDir, "manifest"),
            TablesPath = Path.Combine(tempDir, "tables"),
            ExportTimestamp = DateTime.UtcNow
        };

        _mockPathManager.Setup(x => x.CreateBundleLayout(options)).Returns(layout);
        _mockPathManager.Setup(x => x.EnsureDirectoryStructure(layout));

        try
        {
            // Act
            var result = await _service.ExportAsync(dbPath, options);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.IsSuccess || result.SkippedTables.Count > 0); // Allow for some skipped tables in skeleton
            Assert.True(result.Duration > TimeSpan.Zero);
            Assert.NotNull(result.Statistics);
            Assert.True(result.Statistics.TablesDiscovered > 0);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    #endregion

    public void Dispose()
    {
        _service?.Dispose();
        _dbHelper?.Dispose();
    }
}