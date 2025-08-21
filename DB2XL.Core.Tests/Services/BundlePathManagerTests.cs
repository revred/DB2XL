using DB2XL.Core.Models;
using DB2XL.Core.Services;

namespace DB2XL.Core.Tests.Services;

public class BundlePathManagerTests : IDisposable
{
    private readonly BundlePathManager _pathManager;
    private readonly List<string> _createdDirectories;

    public BundlePathManagerTests()
    {
        _pathManager = new BundlePathManager();
        _createdDirectories = new List<string>();
    }

    [Fact]
    public void CreateBundleLayout_WithDeterministicTimestamp_ShouldCreateCorrectLayout()
    {
        // Arrange
        var options = new BundleExportOptions
        {
            DeterministicTimestamps = true,
            IndexWorkbookName = "test.xlsx",
            ManifestDirectoryName = "manifests",
            TablesDirectoryName = "data"
        };

        // Act
        var layout = _pathManager.CreateBundleLayout(options);
        _createdDirectories.Add(layout.RootPath);

        // Assert
        Assert.Contains("export_run_2025-01-01T00-00-00Z", layout.RootPath);
        Assert.EndsWith("test.xlsx", layout.IndexWorkbookPath);
        Assert.EndsWith("manifests", layout.ManifestPath);
        Assert.EndsWith("data", layout.TablesPath);
        Assert.Equal(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), layout.ExportTimestamp);
    }

    [Fact]
    public void CreateBundleLayout_WithCustomRootPath_ShouldUseProvidedPath()
    {
        // Arrange
        var customPath = Path.Combine(Path.GetTempPath(), "custom_bundle_test");
        var options = new BundleExportOptions
        {
            BundleRootPath = customPath,
            DeterministicTimestamps = true
        };

        // Act
        var layout = _pathManager.CreateBundleLayout(options);
        _createdDirectories.Add(layout.RootPath);

        // Assert
        Assert.Equal(Path.GetFullPath(customPath), layout.RootPath);
        Assert.Equal(Path.Combine(customPath, "index.xlsx"), layout.IndexWorkbookPath);
        Assert.Equal(Path.Combine(customPath, "manifest"), layout.ManifestPath);
        Assert.Equal(Path.Combine(customPath, "tables"), layout.TablesPath);
    }

    [Fact]
    public void CreateBundleLayout_WithNonDeterministicTimestamp_ShouldUseCurrentTime()
    {
        // Arrange
        var options = new BundleExportOptions
        {
            DeterministicTimestamps = false
        };

        var beforeTime = DateTime.UtcNow.AddMinutes(-1);

        // Act
        var layout = _pathManager.CreateBundleLayout(options);
        _createdDirectories.Add(layout.RootPath);

        var afterTime = DateTime.UtcNow.AddMinutes(1);

        // Assert
        Assert.True(layout.ExportTimestamp >= beforeTime && layout.ExportTimestamp <= afterTime);
    }

    [Fact]
    public void GetPartitionFilePath_WithValidInputs_ShouldCreateCorrectPath()
    {
        // Arrange
        var layout = CreateTestLayout();
        var tableName = "orders";
        var partitionLabel = "2025Q1";
        var extension = "jsonl";

        // Act
        var result = _pathManager.GetPartitionFilePath(layout, tableName, partitionLabel, extension);

        // Assert
        var expected = Path.Combine(layout.TablesPath, "orders", "orders_2025Q1.jsonl");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetPartitionFilePath_WithExtensionDot_ShouldHandleProperly()
    {
        // Arrange
        var layout = CreateTestLayout();

        // Act
        var result = _pathManager.GetPartitionFilePath(layout, "table", "part", ".parquet");

        // Assert
        Assert.EndsWith("table_part.parquet", result);
    }

    [Theory]
    [InlineData("", "label", "ext")]
    [InlineData("table", "", "ext")]
    [InlineData("table", "label", "")]
    [InlineData(null, "label", "ext")]
    [InlineData("table", null, "ext")]
    [InlineData("table", "label", null)]
    public void GetPartitionFilePath_WithInvalidInputs_ShouldThrowArgumentException(
        string tableName, string partitionLabel, string extension)
    {
        // Arrange
        var layout = CreateTestLayout();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            _pathManager.GetPartitionFilePath(layout, tableName, partitionLabel, extension));
    }

    [Fact]
    public void GetSampleFilePath_WithValidInput_ShouldCreateCorrectPath()
    {
        // Arrange
        var layout = CreateTestLayout();
        var tableName = "events";

        // Act
        var result = _pathManager.GetSampleFilePath(layout, tableName);

        // Assert
        var expected = Path.Combine(layout.TablesPath, "events", "sample_events_head_10k.jsonl");
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void GetSampleFilePath_WithInvalidTableName_ShouldThrowArgumentException(string tableName)
    {
        // Arrange
        var layout = CreateTestLayout();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _pathManager.GetSampleFilePath(layout, tableName));
    }

    [Fact]
    public void ToRelativePath_WithValidPaths_ShouldReturnCorrectRelativePath()
    {
        // Arrange
        var bundleRoot = @"C:\temp\bundle";
        var absolutePath = @"C:\temp\bundle\tables\orders\orders_2025Q1.jsonl";

        // Act
        var result = _pathManager.ToRelativePath(bundleRoot, absolutePath);

        // Assert
        Assert.Equal("tables/orders/orders_2025Q1.jsonl", result);
    }

    [Fact]
    public void ToRelativePath_WithPathOutsideBundle_ShouldReturnFileName()
    {
        // Arrange
        var bundleRoot = @"C:\temp\bundle";
        var absolutePath = @"C:\different\path\file.txt";

        // Act
        var result = _pathManager.ToRelativePath(bundleRoot, absolutePath);

        // Assert
        Assert.Equal("file.txt", result);
    }

    [Theory]
    [InlineData("", "path")]
    [InlineData("root", "")]
    [InlineData(null, "path")]
    [InlineData("root", null)]
    public void ToRelativePath_WithInvalidInputs_ShouldThrowArgumentException(
        string bundleRoot, string absolutePath)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _pathManager.ToRelativePath(bundleRoot, absolutePath));
    }

    [Fact]
    public void EnsureDirectoryStructure_WithValidLayout_ShouldCreateDirectories()
    {
        // Arrange
        var layout = CreateTestLayout();
        _createdDirectories.Add(layout.RootPath);

        // Act
        _pathManager.EnsureDirectoryStructure(layout);

        // Assert
        Assert.True(Directory.Exists(layout.RootPath));
        Assert.True(Directory.Exists(layout.ManifestPath));
        Assert.True(Directory.Exists(layout.TablesPath));
    }

    [Fact]
    public void EnsureDirectoryStructure_CalledMultipleTimes_ShouldNotFail()
    {
        // Arrange
        var layout = CreateTestLayout();
        _createdDirectories.Add(layout.RootPath);

        // Act & Assert - Should not throw
        _pathManager.EnsureDirectoryStructure(layout);
        _pathManager.EnsureDirectoryStructure(layout);
        _pathManager.EnsureDirectoryStructure(layout);

        Assert.True(Directory.Exists(layout.RootPath));
    }

    [Fact]
    public void EnsureDirectoryStructure_WithNullLayout_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _pathManager.EnsureDirectoryStructure(null));
    }

    [Fact]
    public void GetManifestFilePath_WithValidInputs_ShouldCreateCorrectPath()
    {
        // Arrange
        var layout = CreateTestLayout();
        var manifestName = "schema.json";

        // Act
        var result = _pathManager.GetManifestFilePath(layout, manifestName);

        // Assert
        var expected = Path.Combine(layout.ManifestPath, manifestName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void GetManifestFilePath_WithInvalidManifestName_ShouldThrowArgumentException(string manifestName)
    {
        // Arrange
        var layout = CreateTestLayout();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _pathManager.GetManifestFilePath(layout, manifestName));
    }

    [Fact]
    public void GetManifestFilePath_WithNullLayout_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _pathManager.GetManifestFilePath(null, "schema.json"));
    }

    [Theory]
    [InlineData("normal_table", "normal_table")]
    [InlineData("table with spaces", "table_with_spaces")]
    [InlineData("table<>special", "table__special")]
    [InlineData("table|pipe", "table_pipe")]
    [InlineData("table?question", "table_question")]
    [InlineData("table*star", "table_star")]
    public void SanitizeFileName_ShouldHandleVariousValidInputs(string input, string expected)
    {
        // This tests the internal sanitization through public methods
        // Arrange
        var layout = CreateTestLayout();

        // Act - Use GetPartitionFilePath to test sanitization
        var result = _pathManager.GetPartitionFilePath(layout, input, "part", "txt");

        // Assert
        Assert.Contains(expected, result);
        Assert.EndsWith("_part.txt", result);
    }

    [Fact]
    public void SanitizeFileName_WithEmptyInput_ShouldThrowArgumentException()
    {
        // Arrange
        var layout = CreateTestLayout();

        // Act & Assert - Empty table name should be rejected at validation level
        Assert.Throws<ArgumentException>(() => 
            _pathManager.GetPartitionFilePath(layout, "", "part", "txt"));
    }

    [Fact] 
    public void SanitizeFileName_WithWhitespaceInput_ShouldThrowArgumentException()
    {
        // Arrange
        var layout = CreateTestLayout();

        // Act & Assert - Whitespace-only table name should be rejected at validation level
        Assert.Throws<ArgumentException>(() => 
            _pathManager.GetPartitionFilePath(layout, "   ", "part", "txt"));
    }

    [Fact]
    public void SanitizeFileName_WithVeryLongName_ShouldTruncate()
    {
        // Arrange
        var layout = CreateTestLayout();
        var longName = new string('a', 250); // Very long name

        // Act
        var result = _pathManager.GetPartitionFilePath(layout, longName, "part", "txt");

        // Assert
        // The path should not be excessively long
        var fileName = Path.GetFileName(result);
        Assert.True(fileName.Length < 220, $"Filename too long: {fileName.Length}");
    }

    private BundleLayout CreateTestLayout()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"bundle_test_{Guid.NewGuid():N}");
        return new BundleLayout
        {
            RootPath = tempPath,
            IndexWorkbookPath = Path.Combine(tempPath, "index.xlsx"),
            ManifestPath = Path.Combine(tempPath, "manifest"),
            TablesPath = Path.Combine(tempPath, "tables"),
            ExportTimestamp = DateTime.UtcNow
        };
    }

    public void Dispose()
    {
        // Clean up any created directories
        foreach (var directory in _createdDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}