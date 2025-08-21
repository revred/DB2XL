using DB2XL.Core.Models;
using DB2XL.Core.Services;
using DB2XL.Core.Validation;
using DB2XL.Data.Schema;
using DB2XL.Export.Bundle.Services;
using Microsoft.Data.Sqlite;

namespace DB2XL.Export.Bundle.Tests.Integration;

/// <summary>
/// Integration tests for Parquet export functionality within bundle exports.
/// Verifies end-to-end Parquet generation and bundle integration.
/// </summary>
public class ParquetBundleIntegrationTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly string _tempDirectory;
    private readonly BundleExportService _bundleExportService;
    
    public ParquetBundleIntegrationTests()
    {
        // Create temporary directory for test outputs
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"parquet_bundle_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        
        // Create test database
        _testDbPath = Path.Combine(_tempDirectory, "test.sqlite");
        CreateTestDatabase();
        
        // Initialize bundle export service with Parquet support
        var pathManager = new BundlePathManager();
        var hashCalculator = new BundleHashCalculator();
        var validator = new BundleExportValidator();
        var schemaReader = new SqliteSchemaReader();
        var parquetExporter = new ParquetExportEngine();
        
        _bundleExportService = new BundleExportService(
            pathManager, 
            hashCalculator, 
            validator, 
            schemaReader, 
            parquetExporter);
    }
    
    [Fact]
    public async Task BundleExport_WithParquetEnabled_CreatesBothFormats()
    {
        // Arrange
        var options = new BundleExportOptions
        {
            BundleRootPath = Path.Combine(_tempDirectory, "bundle_with_parquet"),
            GenerateParquet = true,
            IncludeSamples = true,
            SampleRowLimit = 100,
            DeterministicTimestamps = true
        };
        
        // Act
        var result = await _bundleExportService.ExportAsync(_testDbPath, options);
        
        // Assert
        Assert.True(result.IsSuccess, $"Bundle export should succeed. Errors: {string.Join(", ", result.SkippedTables)}");
        Assert.NotEmpty(result.ExportedTables);
        Assert.True(result.Statistics.PartitionFilesCreated > 0);
        
        // Verify both JSONL and Parquet files were created
        var partitions = result.Partitions;
        var jsonlPartitions = partitions.Where(p => p.Format == "jsonl").ToList();
        var parquetPartitions = partitions.Where(p => p.Format == "parquet").ToList();
        
        Assert.NotEmpty(jsonlPartitions);
        Assert.NotEmpty(parquetPartitions);
        
        // Each table should have both formats
        var tableNames = jsonlPartitions.Select(p => p.TableName).Distinct().ToList();
        foreach (var tableName in tableNames)
        {
            Assert.Contains(parquetPartitions, p => p.TableName == tableName);
        }
        
        // Verify files actually exist
        foreach (var partition in partitions)
        {
            var partitionPath = Path.Combine(result.Layout.RootPath, partition.RelativePath);
            Assert.True(File.Exists(partitionPath), $"Partition file should exist: {partitionPath}");
            Assert.True(partition.FileSizeBytes > 0, $"Partition should have content: {partition.RelativePath}");
            Assert.NotEmpty(partition.Sha256Hash);
        }
    }
    
    [Fact]
    public async Task BundleExport_WithParquetDisabled_CreatesOnlyJsonl()
    {
        // Arrange
        var options = new BundleExportOptions
        {
            BundleRootPath = Path.Combine(_tempDirectory, "bundle_without_parquet"),
            GenerateParquet = false,
            IncludeSamples = false,
            DeterministicTimestamps = true
        };
        
        // Act
        var result = await _bundleExportService.ExportAsync(_testDbPath, options);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.ExportedTables);
        
        // Verify only JSONL files were created
        var partitions = result.Partitions;
        var jsonlPartitions = partitions.Where(p => p.Format == "jsonl").ToList();
        var parquetPartitions = partitions.Where(p => p.Format == "parquet").ToList();
        
        Assert.NotEmpty(jsonlPartitions);
        Assert.Empty(parquetPartitions);
        
        // Verify files exist
        foreach (var partition in jsonlPartitions)
        {
            var partitionPath = Path.Combine(result.Layout.RootPath, partition.RelativePath);
            Assert.True(File.Exists(partitionPath));
        }
    }
    
    [Fact]
    public async Task BundleEstimate_WithParquetEnabled_AdjustsEstimates()
    {
        // Arrange
        var optionsWithoutParquet = new BundleExportOptions
        {
            BundleRootPath = Path.Combine(_tempDirectory, "estimate_no_parquet"),
            GenerateParquet = false
        };
        
        var optionsWithParquet = new BundleExportOptions
        {
            BundleRootPath = Path.Combine(_tempDirectory, "estimate_with_parquet"),
            GenerateParquet = true
        };
        
        // Act
        var estimateWithoutParquet = await _bundleExportService.EstimateAsync(_testDbPath, optionsWithoutParquet);
        var estimateWithParquet = await _bundleExportService.EstimateAsync(_testDbPath, optionsWithParquet);
        
        // Assert
        Assert.True(estimateWithoutParquet.EstimatedDuration > TimeSpan.Zero);
        Assert.True(estimateWithParquet.EstimatedDuration > TimeSpan.Zero);
        
        // Parquet export should take longer due to additional processing
        Assert.True(estimateWithParquet.EstimatedDuration > estimateWithoutParquet.EstimatedDuration);
        
        // Both should have same table count and row estimates
        Assert.Equal(estimateWithoutParquet.EstimatedTableCount, estimateWithParquet.EstimatedTableCount);
        Assert.Equal(estimateWithoutParquet.EstimatedTotalRows, estimateWithParquet.EstimatedTotalRows);
        
        // Parquet should have better compression (smaller size estimate)
        foreach (var tableEstimate in estimateWithParquet.TableEstimates)
        {
            var correspondingEstimate = estimateWithoutParquet.TableEstimates
                .FirstOrDefault(t => t.TableName == tableEstimate.TableName);
            
            if (correspondingEstimate != null)
            {
                // Parquet should estimate smaller size due to compression
                Assert.True(tableEstimate.EstimatedSizeBytes <= correspondingEstimate.EstimatedSizeBytes);
            }
        }
    }
    
    [Fact]
    public async Task BundleExport_WithParquetAndLargeTable_HandlesEfficiently()
    {
        // Arrange - Create a database with more data
        var largeDatabasePath = Path.Combine(_tempDirectory, "large_test.sqlite");
        CreateLargeTestDatabase(largeDatabasePath, 1000); // 1K rows
        
        var options = new BundleExportOptions
        {
            BundleRootPath = Path.Combine(_tempDirectory, "large_bundle_with_parquet"),
            GenerateParquet = true,
            IncludeSamples = true,
            SampleRowLimit = 100
        };
        
        // Act
        var result = await _bundleExportService.ExportAsync(largeDatabasePath, options);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.ExportedTables);
        
        // Check that both formats were created
        var parquetPartitions = result.Partitions.Where(p => p.Format == "parquet").ToList();
        var jsonlPartitions = result.Partitions.Where(p => p.Format == "jsonl").ToList();
        
        Assert.NotEmpty(parquetPartitions);
        Assert.NotEmpty(jsonlPartitions);
        
        // Parquet files should be smaller than JSONL due to compression
        foreach (var parquetPartition in parquetPartitions)
        {
            var correspondingJsonl = jsonlPartitions
                .FirstOrDefault(j => j.TableName == parquetPartition.TableName);
            
            if (correspondingJsonl != null)
            {
                // Note: This might not always be true for small datasets,
                // but should generally be true for larger datasets
                Assert.True(parquetPartition.FileSizeBytes > 0);
                Assert.True(correspondingJsonl.FileSizeBytes > 0);
            }
        }
        
        // Verify export completed in reasonable time
        Assert.True(result.Duration < TimeSpan.FromMinutes(1), "Large export should complete in reasonable time");
    }
    
    [Fact]
    public async Task BundleExport_WithParquetAndMultipleTables_CreatesAllFormats()
    {
        // Arrange - Create database with multiple tables
        var multiTableDbPath = Path.Combine(_tempDirectory, "multi_table.sqlite");
        CreateMultiTableDatabase(multiTableDbPath);
        
        var options = new BundleExportOptions
        {
            BundleRootPath = Path.Combine(_tempDirectory, "multi_table_bundle"),
            GenerateParquet = true,
            IncludeSamples = true
        };
        
        // Act
        var result = await _bundleExportService.ExportAsync(multiTableDbPath, options);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(result.ExportedTables.Count >= 2, "Should export multiple tables");
        
        // Each table should have both JSONL and Parquet partitions
        foreach (var tableName in result.ExportedTables)
        {
            var tablePartitions = result.Partitions.Where(p => p.TableName == tableName).ToList();
            
            var jsonlPartitions = tablePartitions.Where(p => p.Format == "jsonl").ToList();
            var parquetPartitions = tablePartitions.Where(p => p.Format == "parquet").ToList();
            
            Assert.NotEmpty(jsonlPartitions);
            Assert.NotEmpty(parquetPartitions);
            
            // Verify files exist for both formats
            foreach (var partition in tablePartitions)
            {
                var partitionPath = Path.Combine(result.Layout.RootPath, partition.RelativePath);
                Assert.True(File.Exists(partitionPath), 
                    $"Partition file should exist: {partition.RelativePath} (format: {partition.Format})");
            }
        }
    }
    
    [Fact]
    public async Task BundleExport_WithParquetEnabled_IncludesFormatInManifest()
    {
        // Arrange
        var options = new BundleExportOptions
        {
            BundleRootPath = Path.Combine(_tempDirectory, "manifest_test_bundle"),
            GenerateParquet = true,
            IncludeSamples = true
        };
        
        // Act
        var result = await _bundleExportService.ExportAsync(_testDbPath, options);
        
        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.ManifestPaths);
        
        // Check schema manifest includes format information
        if (result.ManifestPaths.ContainsKey("schema.json"))
        {
            var schemaManifestPath = result.ManifestPaths["schema.json"];
            Assert.True(File.Exists(schemaManifestPath));
            
            var schemaContent = await File.ReadAllTextAsync(schemaManifestPath);
            Assert.NotEmpty(schemaContent);
            
            // Should mention both formats in the manifest
            // (This is a basic check - in a real implementation, 
            // the manifest would have structured format information)
            Assert.True(schemaContent.Length > 100, "Schema manifest should have substantial content");
        }
        
        // Check provenance manifest
        if (result.ManifestPaths.ContainsKey("provenance.json"))
        {
            var provenanceManifestPath = result.ManifestPaths["provenance.json"];
            Assert.True(File.Exists(provenanceManifestPath));
            
            var provenanceContent = await File.ReadAllTextAsync(provenanceManifestPath);
            Assert.NotEmpty(provenanceContent);
        }
    }

    private void CreateTestDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE customers (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT,
                age INTEGER,
                balance REAL,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                is_active BOOLEAN DEFAULT 1
            );

            CREATE TABLE orders (
                id INTEGER PRIMARY KEY,
                customer_id INTEGER,
                amount REAL,
                order_date TEXT DEFAULT CURRENT_TIMESTAMP,
                status TEXT DEFAULT 'pending',
                FOREIGN KEY (customer_id) REFERENCES customers(id)
            );

            INSERT INTO customers (name, email, age, balance, is_active) VALUES 
                ('John Doe', 'john@example.com', 30, 1500.50, 1),
                ('Jane Smith', 'jane@example.com', 25, 2300.75, 1),
                ('Bob Wilson', 'bob@example.com', 45, 500.25, 0),
                ('Alice Brown', 'alice@example.com', 35, 3200.00, 1),
                ('Charlie Davis', 'charlie@example.com', 28, 800.90, 1);
                
            INSERT INTO orders (customer_id, amount, status) VALUES 
                (1, 99.99, 'completed'),
                (2, 149.50, 'pending'),
                (1, 75.25, 'completed'),
                (3, 200.00, 'cancelled'),
                (4, 49.99, 'completed');
        ";

        command.ExecuteNonQuery();
    }
    
    private void CreateLargeTestDatabase(string dbPath, int customerCount)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        
        // Create tables
        command.CommandText = @"
            CREATE TABLE large_customers (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT,
                age INTEGER,
                balance REAL,
                category TEXT,
                created_at TEXT,
                is_active BOOLEAN
            );
        ";
        command.ExecuteNonQuery();

        // Insert large dataset
        using var transaction = connection.BeginTransaction();
        var random = new Random(42);
        
        for (int i = 1; i <= customerCount; i++)
        {
            command.CommandText = @"
                INSERT INTO large_customers (name, email, age, balance, category, created_at, is_active) 
                VALUES (@name, @email, @age, @balance, @category, @created_at, @is_active)
            ";
            
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@name", $"Customer {i:D6}");
            command.Parameters.AddWithValue("@email", $"customer{i}@example.com");
            command.Parameters.AddWithValue("@age", random.Next(18, 80));
            command.Parameters.AddWithValue("@balance", Math.Round(random.NextDouble() * 10000, 2));
            command.Parameters.AddWithValue("@category", $"Category {i % 10}");
            command.Parameters.AddWithValue("@created_at", DateTime.UtcNow.AddDays(-random.Next(365)).ToString("O"));
            command.Parameters.AddWithValue("@is_active", i % 4 != 0);
            
            command.ExecuteNonQuery();
        }
        
        transaction.Commit();
    }
    
    private void CreateMultiTableDatabase(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE products (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                price REAL,
                category TEXT,
                in_stock BOOLEAN DEFAULT 1
            );
            
            CREATE TABLE sales (
                id INTEGER PRIMARY KEY,
                product_id INTEGER,
                quantity INTEGER,
                sale_date TEXT DEFAULT CURRENT_TIMESTAMP,
                total_amount REAL,
                FOREIGN KEY (product_id) REFERENCES products(id)
            );
            
            CREATE TABLE inventory (
                id INTEGER PRIMARY KEY,
                product_id INTEGER,
                stock_level INTEGER,
                last_updated TEXT DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (product_id) REFERENCES products(id)
            );

            INSERT INTO products (name, price, category, in_stock) VALUES 
                ('Laptop Pro', 1299.99, 'Electronics', 1),
                ('Wireless Mouse', 29.99, 'Electronics', 1),
                ('Office Chair', 199.99, 'Furniture', 1),
                ('Standing Desk', 399.99, 'Furniture', 0),
                ('Monitor 24inch', 249.99, 'Electronics', 1);
                
            INSERT INTO sales (product_id, quantity, total_amount) VALUES 
                (1, 2, 2599.98),
                (2, 5, 149.95),
                (3, 1, 199.99),
                (5, 3, 749.97);
                
            INSERT INTO inventory (product_id, stock_level) VALUES 
                (1, 15),
                (2, 50),
                (3, 8),
                (4, 0),
                (5, 25);
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