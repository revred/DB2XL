using DB2XL.Core.Models;
using DB2XL.Core.Services;
using DB2XL.Core.Validation;
using DB2XL.Core.Exceptions;
using DB2XL.Data.Schema;
using DB2XL.Export.Bundle.Services;
using Microsoft.Data.Sqlite;
using System.IO;

namespace DB2XL.Export.Bundle.Tests.Integration;

/// <summary>
/// Integration tests for the Bundle Orchestration Engine.
/// Verifies end-to-end bundle export functionality.
/// </summary>
public class BundleOrchestrationTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly string _tempDirectory;
    private readonly BundleExportService _bundleExportService;
    
    public BundleOrchestrationTests()
    {
        // Create temporary directory for test outputs
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"bundle_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        
        // Create test database
        _testDbPath = Path.Combine(_tempDirectory, "test.sqlite");
        CreateTestDatabase();
        
        // Initialize bundle export service
        var pathManager = new BundlePathManager();
        var hashCalculator = new BundleHashCalculator();
        var validator = new BundleExportValidator();
        var schemaReader = new SqliteSchemaReader();
        
        _bundleExportService = new BundleExportService(
            pathManager, 
            hashCalculator, 
            validator, 
            schemaReader);
    }
    
    [Fact]
    public async Task ExportAsync_WithSimpleDatabase_CreatesValidBundle()
    {
        // Arrange
        var options = new BundleExportOptions
        {
            BundleRootPath = Path.Combine(_tempDirectory, "bundle_output"),
            IncludeSamples = true,
            SampleRowLimit = 100,
            DeterministicTimestamps = true
        };
        
        // Act
        var result = await _bundleExportService.ExportAsync(_testDbPath, options);
        
        // Assert
        Assert.True(result.IsSuccess, $"Bundle export should succeed. Errors: {string.Join(", ", result.SkippedTables)}");
        Assert.NotEmpty(result.ExportedTables);
        Assert.True(result.Statistics.TablesExported > 0);
        Assert.True(result.Statistics.TotalRowsExported > 0);
        Assert.True(result.Statistics.PartitionFilesCreated > 0);
        
        // Verify bundle structure exists
        Assert.True(Directory.Exists(result.Layout.RootPath));
        Assert.True(Directory.Exists(result.Layout.ManifestPath));
        Assert.True(Directory.Exists(result.Layout.TablesPath));
        
        // Verify manifest files were created
        Assert.NotEmpty(result.ManifestPaths);
        Assert.True(result.ManifestPaths.ContainsKey("schema.json"));
        Assert.True(result.ManifestPaths.ContainsKey("provenance.json"));
        
        // Verify files actually exist
        foreach (var manifestPath in result.ManifestPaths.Values)
        {
            Assert.True(File.Exists(manifestPath), $"Manifest file should exist: {manifestPath}");
        }
        
        // Verify partitions were created
        Assert.NotEmpty(result.Partitions);
        foreach (var partition in result.Partitions)
        {
            var partitionPath = Path.Combine(result.Layout.RootPath, partition.RelativePath);
            Assert.True(File.Exists(partitionPath), $"Partition file should exist: {partitionPath}");
            Assert.True(partition.FileSizeBytes > 0);
            Assert.NotEmpty(partition.Sha256Hash);
        }
    }
    
    [Fact]
    public async Task EstimateAsync_WithSimpleDatabase_ReturnsValidEstimate()
    {
        // Arrange
        var options = new BundleExportOptions
        {
            BundleRootPath = Path.Combine(_tempDirectory, "estimate_test"),
            IncludeSamples = true
        };
        
        // Act
        var estimate = await _bundleExportService.EstimateAsync(_testDbPath, options);
        
        // Assert
        Assert.True(estimate.EstimatedTableCount > 0);
        Assert.True(estimate.EstimatedTotalRows > 0);
        Assert.True(estimate.EstimatedPartitionCount > 0);
        Assert.True(estimate.EstimatedOutputSizeBytes > 0);
        Assert.True(estimate.EstimatedDuration > TimeSpan.Zero);
        Assert.NotNull(estimate.DatabaseInfo);
        Assert.True(estimate.DatabaseInfo.FileSizeBytes > 0);
        Assert.NotEmpty(estimate.TableEstimates);
        
        // Verify complexity assessment
        Assert.True(Enum.IsDefined(typeof(ExportComplexity), estimate.Complexity));
        Assert.NotNull(estimate.Recommendations);
    }
    
    [Fact]
    public void ValidateOptions_WithValidOptions_ReturnsValid()
    {
        // Arrange
        var options = new BundleExportOptions
        {
            BundleRootPath = Path.Combine(_tempDirectory, "valid_test"),
            IncludeSamples = true,
            SampleRowLimit = 1000
        };
        
        // Act
        var result = _bundleExportService.ValidateOptions(options);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
    
    [Fact]
    public void ValidateOptions_WithInvalidOptions_ReturnsErrors()
    {
        // Arrange
        var options = new BundleExportOptions
        {
            BundleRootPath = string.Empty,
            SampleRowLimit = -1 // Invalid
        };
        
        // Act
        var result = _bundleExportService.ValidateOptions(options);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }
    
    [Fact]
    public async Task ExportAsync_WithNonexistentDatabase_ThrowsException()
    {
        // Arrange
        var nonexistentPath = Path.Combine(_tempDirectory, "nonexistent.sqlite");
        var options = new BundleExportOptions
        {
            BundleRootPath = Path.Combine(_tempDirectory, "error_test")
        };
        
        // Act & Assert
        await Assert.ThrowsAsync<BundleDatabaseException>(() => 
            _bundleExportService.ExportAsync(nonexistentPath, options));
    }
    
    [Fact]
    public async Task ExportAsync_CreatesExpectedDirectoryStructure()
    {
        // Arrange
        var bundleRoot = Path.Combine(_tempDirectory, "structure_test");
        var options = new BundleExportOptions
        {
            BundleRootPath = bundleRoot,
            IndexWorkbookName = "custom_index.xlsx",
            ManifestDirectoryName = "custom_manifest",
            TablesDirectoryName = "custom_tables"
        };
        
        // Act
        var result = await _bundleExportService.ExportAsync(_testDbPath, options);
        
        // Assert
        Assert.Equal(bundleRoot, result.Layout.RootPath);
        Assert.Contains("custom_manifest", result.Layout.ManifestPath);
        Assert.Contains("custom_tables", result.Layout.TablesPath);
        
        // Verify custom directory names were used
        Assert.True(Directory.Exists(Path.Combine(bundleRoot, "custom_manifest")));
        Assert.True(Directory.Exists(Path.Combine(bundleRoot, "custom_tables")));
    }
    
    private void CreateTestDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();
        
        using var command = connection.CreateCommand();
        
        // Create test tables with sample data
        command.CommandText = @"
            CREATE TABLE customers (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP
            );
            
            CREATE TABLE orders (
                id INTEGER PRIMARY KEY,
                customer_id INTEGER,
                amount REAL,
                order_date TEXT DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (customer_id) REFERENCES customers(id)
            );
            
            CREATE TABLE products (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                price REAL,
                category TEXT,
                description TEXT
            );
            
            -- Insert sample data
            INSERT INTO customers (name, email) VALUES 
                ('John Doe', 'john@example.com'),
                ('Jane Smith', 'jane@example.com'),
                ('Bob Wilson', 'bob@example.com'),
                ('Alice Brown', 'alice@example.com'),
                ('Charlie Davis', 'charlie@example.com');
            
            INSERT INTO orders (customer_id, amount) VALUES 
                (1, 99.99),
                (2, 149.50),
                (1, 75.25),
                (3, 200.00),
                (4, 49.99),
                (5, 125.75),
                (2, 89.90),
                (3, 175.50);
            
            INSERT INTO products (name, price, category, description) VALUES 
                ('Laptop', 999.99, 'Electronics', 'High-performance laptop'),
                ('Mouse', 25.99, 'Electronics', 'Wireless mouse'),
                ('Keyboard', 89.99, 'Electronics', 'Mechanical keyboard'),
                ('Monitor', 299.99, 'Electronics', '24-inch LCD monitor'),
                ('Chair', 199.99, 'Furniture', 'Ergonomic office chair'),
                ('Desk', 349.99, 'Furniture', 'Standing desk'),
                ('Book', 19.99, 'Education', 'Programming textbook'),
                ('Notebook', 12.99, 'Office', 'Spiral notebook');
        ";
        
        command.ExecuteNonQuery();
    }
    
    public void Dispose()
    {
        try
        {
            _bundleExportService?.Dispose();
            
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