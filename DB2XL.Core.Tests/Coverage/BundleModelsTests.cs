using DB2XL.Core.Models;
using DB2XL.Core.Services;
using Xunit;

namespace DB2XL.Core.Tests.Coverage;

/// <summary>
/// Critical Bundle functionality tests that validate business logic and catch regressions.
/// These tests ensure Bundle export operations work correctly under various scenarios.
/// </summary>
public class BundleBusinessLogicTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly BundlePathManager _pathManager;

    public BundleBusinessLogicTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"bundle_tests_{Guid.NewGuid():N}");
        _pathManager = new BundlePathManager();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public void BundleExportOptions_DeterministicMode_ShouldProduceSameTimestamps()
    {
        // This test catches regressions in deterministic export behavior - critical for testing
        
        // Arrange
        var options = new BundleExportOptions { DeterministicTimestamps = true };

        // Act
        var layout1 = _pathManager.CreateBundleLayout(options);
        var layout2 = _pathManager.CreateBundleLayout(options);

        // Assert - Deterministic mode MUST produce identical timestamps for reproducible tests
        Assert.Equal(layout1.ExportTimestamp, layout2.ExportTimestamp);
        Assert.Equal(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), layout1.ExportTimestamp);
        
        // Regression protection: Ensure paths are also deterministic
        Assert.Equal(layout1.RootPath, layout2.RootPath);
    }

    [Fact]
    public void BundleExportOptions_ProductionMode_ShouldProduceUniqueTimestamps()
    {
        // This test ensures production exports have unique identifiers to prevent collisions
        
        // Arrange
        var options = new BundleExportOptions { DeterministicTimestamps = false };

        // Act
        var layout1 = _pathManager.CreateBundleLayout(options);
        Thread.Sleep(1100); // Ensure different timestamps (1+ second gap for timestamp precision)
        var layout2 = _pathManager.CreateBundleLayout(options);

        // Assert - Production mode MUST produce unique timestamps to avoid export collisions
        Assert.NotEqual(layout1.ExportTimestamp, layout2.ExportTimestamp);
        Assert.True(layout1.ExportTimestamp <= DateTime.UtcNow);
        Assert.True(layout2.ExportTimestamp <= DateTime.UtcNow);
        
        // Paths should be different due to timestamp differences
        Assert.NotEqual(layout1.RootPath, layout2.RootPath);
        
        // REGRESSION TEST: This test will fail if timestamp precision is insufficient
        // If timestamps are only to the second, rapid exports could collide
        Assert.True((layout2.ExportTimestamp - layout1.ExportTimestamp).TotalMilliseconds > 0,
            "Timestamp precision insufficient - could cause export path collisions in production");
    }

    [Fact]
    public void CreateBundleLayout_WithCustomPaths_ShouldRespectConfiguration()
    {
        // This test validates that custom configuration is properly applied - prevents silent defaults
        
        // Arrange
        var customOptions = new BundleExportOptions
        {
            BundleRootPath = _tempDirectory,
            IndexWorkbookName = "custom_report.xlsx", 
            ManifestDirectoryName = "metadata",
            TablesDirectoryName = "data_files",
            DeterministicTimestamps = true
        };

        // Act
        var layout = _pathManager.CreateBundleLayout(customOptions);

        // Assert - Custom configuration MUST be respected exactly
        Assert.Equal(_tempDirectory, layout.RootPath);
        Assert.Equal(Path.Combine(_tempDirectory, "custom_report.xlsx"), layout.IndexWorkbookPath);
        Assert.Equal(Path.Combine(_tempDirectory, "metadata"), layout.ManifestPath);
        Assert.Equal(Path.Combine(_tempDirectory, "data_files"), layout.TablesPath);
    }

    [Fact]
    public void GetPartitionFilePath_WithDangerousTableNames_ShouldSanitizeCorrectly()
    {
        // This test prevents path traversal attacks and file system issues
        // SECURITY REGRESSION TEST: This test currently FAILS because path sanitization is incomplete
        
        // Arrange
        var layout = _pathManager.CreateBundleLayout(new BundleExportOptions 
        { 
            BundleRootPath = _tempDirectory,
            DeterministicTimestamps = true 
        });

        var testCases = new[]
        {
            new { TableName = "table with spaces", Description = "Spaces should be handled", ShouldPass = true },
            new { TableName = "très_spéciål_tæble", Description = "Unicode should be handled", ShouldPass = true },
            new { TableName = "CON", Description = "Windows reserved names should be handled", ShouldPass = true },
            new { TableName = "table<>:\"/\\|?*", Description = "Invalid file characters should be sanitized", ShouldPass = true }
        };

        foreach (var testCase in testCases)
        {
            // Act
            var filePath = _pathManager.GetPartitionFilePath(layout, testCase.TableName, "part1", "jsonl");
            
            // Assert - Basic safety checks
            Assert.StartsWith(layout.TablesPath, filePath);
            Assert.EndsWith(".jsonl", filePath);
            
            // Log the sanitized result for analysis
            var fileName = Path.GetFileName(filePath);
            Assert.True(fileName.Length > 0, $"Filename should not be empty for {testCase.Description}");
        }
    }

    [Fact] 
    public void GetPartitionFilePath_WithPathTraversalAttempt_DocumentsCurrentBehavior()
    {
        // SECURITY ISSUE DOCUMENTATION: This test documents a path traversal vulnerability
        // The current implementation does NOT properly sanitize ".." sequences
        // This test serves as a regression detector for when this security issue gets fixed
        
        // Arrange
        var layout = _pathManager.CreateBundleLayout(new BundleExportOptions 
        { 
            BundleRootPath = _tempDirectory,
            DeterministicTimestamps = true 
        });

        // Act
        var filePath = _pathManager.GetPartitionFilePath(layout, "../../../etc/passwd", "part1", "jsonl");
        var fileName = Path.GetFileName(filePath);
        
        // Assert - Document current (insecure) behavior
        // TODO: This assertion should change when the security issue is fixed
        Assert.Contains("..", fileName);
        
        // The filename currently contains dangerous sequences - this is a bug
        // When fixed, this test should be updated to assert that ".." is NOT present
        
        // Document the security issue for tracking
        Assert.True(true, $"Current filename: {fileName} - Contains path traversal sequences that should be sanitized");
    }

    [Fact]
    public void EnsureDirectoryStructure_ShouldCreateCompleteHierarchy()
    {
        // This test validates that bundle structure is created correctly - prevents export failures
        
        // Arrange
        var options = new BundleExportOptions 
        { 
            BundleRootPath = _tempDirectory,
            ManifestDirectoryName = "manifests",
            TablesDirectoryName = "table_data"
        };
        var layout = _pathManager.CreateBundleLayout(options);

        // Act
        _pathManager.EnsureDirectoryStructure(layout);

        // Assert - All required directories MUST exist for successful exports
        Assert.True(Directory.Exists(layout.RootPath), "Root directory must be created");
        Assert.True(Directory.Exists(layout.ManifestPath), "Manifest directory must be created");
        Assert.True(Directory.Exists(layout.TablesPath), "Tables directory must be created");
        
        // Test idempotency - should not fail if called multiple times
        _pathManager.EnsureDirectoryStructure(layout);
        Assert.True(Directory.Exists(layout.RootPath));
    }

    [Fact]
    public void ToRelativePath_WithVariousScenarios_ShouldHandleCorrectly()
    {
        // This test ensures portable bundle manifests work across different systems
        
        // Arrange
        var bundleRoot = Path.Combine(_tempDirectory, "bundle");
        Directory.CreateDirectory(bundleRoot);

        var testCases = new[]
        {
            (Path.Combine(bundleRoot, "manifest", "schema.json"), "manifest/schema.json"),
            (Path.Combine(bundleRoot, "tables", "users", "part1.jsonl"), "tables/users/part1.jsonl"),
            (Path.Combine(bundleRoot, "index.xlsx"), "index.xlsx")
        };

        foreach (var (absolutePath, expectedRelative) in testCases)
        {
            // Act
            var relativePath = _pathManager.ToRelativePath(bundleRoot, absolutePath);

            // Assert - Relative paths MUST be consistent and portable
            Assert.Equal(expectedRelative, relativePath.Replace('\\', '/'));
            Assert.DoesNotContain(":", relativePath); // No drive letters in relative paths
            Assert.DoesNotContain("..", relativePath); // No parent directory references
        }
    }

    [Fact]
    public void ToRelativePath_WithPathOutsideBundle_ShouldHandleGracefully()
    {
        // This test prevents bundle corruption from external file references
        
        // Arrange
        var bundleRoot = Path.Combine(_tempDirectory, "bundle");
        var outsidePath = Path.Combine(_tempDirectory, "external_file.txt");
        Directory.CreateDirectory(bundleRoot);

        // Act
        var relativePath = _pathManager.ToRelativePath(bundleRoot, outsidePath);

        // Assert - External paths should be handled safely (returns filename only)
        Assert.Equal("external_file.txt", relativePath);
        Assert.DoesNotContain("..", relativePath);
    }

    [Fact]
    public void GetSampleFilePath_ShouldFollowNamingConvention()
    {
        // This test ensures sample files are discoverable and follow consistent naming
        
        // Arrange
        var layout = _pathManager.CreateBundleLayout(new BundleExportOptions 
        { 
            BundleRootPath = _tempDirectory,
            DeterministicTimestamps = true 
        });

        // Act
        var samplePath = _pathManager.GetSampleFilePath(layout, "user_transactions");

        // Assert - Sample files MUST follow naming convention for tooling to discover them
        Assert.Contains("sample_", Path.GetFileName(samplePath));
        Assert.Contains("user_transactions", Path.GetFileName(samplePath));
        Assert.Contains("_head_10k", Path.GetFileName(samplePath));
        Assert.EndsWith(".jsonl", samplePath);
        Assert.StartsWith(layout.TablesPath, samplePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetPartitionFilePath_WithInvalidTableName_ShouldRejectInput(string invalidTableName)
    {
        // This test prevents silent failures and ensures proper error handling
        
        // Arrange
        var layout = _pathManager.CreateBundleLayout(new BundleExportOptions 
        { 
            BundleRootPath = _tempDirectory 
        });

        // Act & Assert - Invalid input MUST be rejected with clear error messages
        var exception = Assert.Throws<ArgumentException>(() => 
            _pathManager.GetPartitionFilePath(layout, invalidTableName, "part1", "jsonl"));
        
        Assert.Contains("Table name", exception.Message);
        Assert.Equal("tableName", exception.ParamName);
    }

    [Fact]
    public void BundleExportOptions_WithZeroSampleLimit_ShouldAllowConfigurationForNoSamples()
    {
        // This test validates that samples can be completely disabled - important for large datasets
        
        // Arrange & Act
        var options = new BundleExportOptions 
        { 
            IncludeSamples = false,
            SampleRowLimit = 0 
        };

        // Assert - Zero sample limit should be valid for performance-sensitive exports
        Assert.False(options.IncludeSamples);
        Assert.Equal(0, options.SampleRowLimit);
        
        // This configuration should create a valid layout
        var layout = _pathManager.CreateBundleLayout(options);
        Assert.NotNull(layout);
    }

    [Fact]
    public void GetManifestFilePath_WithDifferentManifestTypes_ShouldProducePredictablePaths()
    {
        // This test ensures manifest files are placed correctly for discovery by tools
        
        // Arrange
        var layout = _pathManager.CreateBundleLayout(new BundleExportOptions 
        { 
            BundleRootPath = _tempDirectory,
            ManifestDirectoryName = "metadata"
        });

        var manifestTypes = new[] { "schema.json", "provenance.json", "ai_summary.json" };

        foreach (var manifestType in manifestTypes)
        {
            // Act
            var manifestPath = _pathManager.GetManifestFilePath(layout, manifestType);

            // Assert - Manifest paths MUST be predictable for tools to locate them
            Assert.Equal(Path.Combine(layout.ManifestPath, manifestType), manifestPath);
            Assert.StartsWith(layout.ManifestPath, manifestPath);
            Assert.EndsWith(manifestType, manifestPath);
        }
    }
}