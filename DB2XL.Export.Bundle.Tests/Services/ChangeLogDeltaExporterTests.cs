using DB2XL.Core.Models;
using DB2XL.Core.Services;
using DB2XL.Export.Bundle.Services;
using Microsoft.Data.Sqlite;
using Moq;
using System.Text.Json;

namespace DB2XL.Export.Bundle.Tests.Services;

/// <summary>
/// Tests for ChangeLogDeltaExporter implementation.
/// Validates trigger-based incremental export functionality.
/// </summary>
public class ChangeLogDeltaExporterTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _testDbPath;
    private readonly string _connectionString;
    private readonly ChangeLogDeltaExporter _exporter;
    private readonly Mock<IJsonlExportEngine> _mockJsonlExporter;
    private readonly Mock<IParquetExportEngine> _mockParquetExporter;
    
    public ChangeLogDeltaExporterTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"changelog_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        
        _testDbPath = Path.Combine(_tempDirectory, "test.db");
        _connectionString = $"Data Source={_testDbPath};";
        
        _mockJsonlExporter = new Mock<IJsonlExportEngine>();
        _mockParquetExporter = new Mock<IParquetExportEngine>();
        
        _exporter = new ChangeLogDeltaExporter(
            _mockJsonlExporter.Object,
            _mockParquetExporter.Object);
        
        SetupTestDatabase();
    }

    [Fact]
    public async Task SetupChangeLogAsync_ValidTable_CreatesInfrastructure()
    {
        // Arrange
        var options = new ChangeLogSetupOptions
        {
            TrackInserts = true,
            TrackUpdates = true,
            TrackDeletes = true
        };

        // Act
        var result = await _exporter.SetupChangeLogAsync(_connectionString, "products", options);

        // Assert
        Assert.True(result.IsSuccess, $"Setup failed with errors: {string.Join("; ", result.Errors)}");
        Assert.Contains(result.CreatedComponents, c => c.Contains("__changes"));
        Assert.Contains(result.CreatedComponents, c => c.Contains("INSERT trigger"));
        Assert.Contains(result.CreatedComponents, c => c.Contains("UPDATE trigger"));
        Assert.Contains(result.CreatedComponents, c => c.Contains("DELETE trigger"));
    }

    [Fact]
    public async Task ValidateChangeLogSetupAsync_NoInfrastructure_ReturnsInvalid()
    {
        // Act
        var result = await _exporter.ValidateChangeLogSetupAsync(_connectionString, "products");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("__changes") && e.Contains("does not exist"));
    }

    [Fact]
    public async Task ValidateChangeLogSetupAsync_WithInfrastructure_ReturnsValid()
    {
        // Arrange
        await _exporter.SetupChangeLogAsync(_connectionString, "products");

        // Act
        var result = await _exporter.ValidateChangeLogSetupAsync(_connectionString, "products");

        // Assert
        Assert.True(result.IsValid);
        Assert.True(result.ChangeLogTableExists);
        Assert.Contains("id", result.PrimaryKeyColumns);
        Assert.Equal(3, result.TrackedOperations.Count); // INSERT, UPDATE, DELETE
        Assert.Contains(result.ExistingTriggers, t => t.Contains("insert"));
        Assert.Contains(result.ExistingTriggers, t => t.Contains("update"));
        Assert.Contains(result.ExistingTriggers, t => t.Contains("delete"));
    }

    [Fact]
    public async Task GetCurrentChangeLogCheckpointAsync_EmptyChangeLog_ReturnsEmptyCheckpoint()
    {
        // Arrange
        await _exporter.SetupChangeLogAsync(_connectionString, "products");

        // Act
        var checkpoint = await _exporter.GetCurrentChangeLogCheckpointAsync(
            _connectionString, 
            "products");

        // Assert
        Assert.Equal("products", checkpoint.TableName);
        Assert.Equal("change_id", checkpoint.WatermarkColumn);
        Assert.Null(checkpoint.LastWatermarkValue);
        Assert.Equal(0, checkpoint.RowsProcessed);
    }

    [Fact]
    public async Task GetCurrentChangeLogCheckpointAsync_WithChanges_ReturnsLatestCheckpoint()
    {
        // Arrange
        await _exporter.SetupChangeLogAsync(_connectionString, "products");
        
        // Add some data to trigger change log entries
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var insertCommand = new SqliteCommand(@"
            INSERT INTO products (name, price, category) VALUES 
            ('Product A', 10.99, 'Category 1'),
            ('Product B', 20.99, 'Category 2')", connection);
        await insertCommand.ExecuteNonQueryAsync();

        // Act
        var checkpoint = await _exporter.GetCurrentChangeLogCheckpointAsync(
            _connectionString, 
            "products");

        // Assert
        Assert.Equal("products", checkpoint.TableName);
        Assert.Equal("change_id", checkpoint.WatermarkColumn);
        Assert.NotNull(checkpoint.LastWatermarkValue);
        Assert.Equal(2, checkpoint.RowsProcessed); // 2 insert operations
    }

    [Fact]
    public async Task ExportDeltaAsync_InitialExport_ExportsAllChanges()
    {
        // Arrange
        await _exporter.SetupChangeLogAsync(_connectionString, "products");
        
        // Add test data
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var insertCommand = new SqliteCommand(@"
            INSERT INTO products (name, price, category) VALUES 
            ('Product A', 10.99, 'Category 1'),
            ('Product B', 20.99, 'Category 2')", connection);
        await insertCommand.ExecuteNonQueryAsync();

        var options = new ChangeLogDeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "delta"),
            Format = ExportFormat.Jsonl
        };

        // Act
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "products",
            lastCheckpoint: null,
            options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.RowsExported); // 2 insert operations
        Assert.NotNull(result.NewCheckpoint);
        Assert.Equal("products", result.NewCheckpoint.TableName);
        Assert.Equal("change_id", result.NewCheckpoint.WatermarkColumn);
        Assert.Single(result.ExportedFiles);
    }

    [Fact]
    public async Task ExportDeltaAsync_IncrementalExport_ExportsOnlyNewChanges()
    {
        // Arrange
        await _exporter.SetupChangeLogAsync(_connectionString, "products");
        
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        // Initial data
        await using var insertCommand = new SqliteCommand(@"
            INSERT INTO products (name, price, category) VALUES 
            ('Product A', 10.99, 'Category 1'),
            ('Product B', 20.99, 'Category 2')", connection);
        await insertCommand.ExecuteNonQueryAsync();

        // Get initial checkpoint
        var initialCheckpoint = await _exporter.GetCurrentChangeLogCheckpointAsync(_connectionString, "products");

        // Add more data
        await using var moreInsertCommand = new SqliteCommand(@"
            INSERT INTO products (name, price, category) VALUES 
            ('Product C', 30.99, 'Category 3')", connection);
        await moreInsertCommand.ExecuteNonQueryAsync();

        var options = new ChangeLogDeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "incremental"),
            Format = ExportFormat.Jsonl,
            ValidateCheckpoint = false
        };

        // Act
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "products",
            initialCheckpoint,
            options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.RowsExported); // Only the new insert
        Assert.NotNull(result.NewCheckpoint);
        Assert.Equal(3, result.NewCheckpoint.RowsProcessed); // 2 initial + 1 new
    }

    [Fact]
    public async Task ExportDeltaAsync_WithUpdates_TracksModifications()
    {
        // Arrange
        await _exporter.SetupChangeLogAsync(_connectionString, "products");
        
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        // Insert initial data
        await using var insertCommand = new SqliteCommand(@"
            INSERT INTO products (name, price, category) VALUES 
            ('Product A', 10.99, 'Category 1')", connection);
        await insertCommand.ExecuteNonQueryAsync();

        var initialCheckpoint = await _exporter.GetCurrentChangeLogCheckpointAsync(_connectionString, "products");

        // Update the record
        await using var updateCommand = new SqliteCommand(@"
            UPDATE products SET price = 15.99 WHERE name = 'Product A'", connection);
        await updateCommand.ExecuteNonQueryAsync();

        var options = new ChangeLogDeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "updates"),
            Format = ExportFormat.Jsonl,
            ValidateCheckpoint = false
        };

        // Act
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "products",
            initialCheckpoint,
            options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.RowsExported); // The updated row
        Assert.NotNull(result.NewCheckpoint);
        
        // Verify the exported file contains the update
        var exportedFile = result.ExportedFiles.First();
        var content = await File.ReadAllTextAsync(exportedFile);
        Assert.Contains("UPDATE", content);
        Assert.Contains("15.99", content); // Updated price
    }

    [Fact]
    public async Task ExportDeltaAsync_WithDeletes_TracksRemovals()
    {
        // Arrange
        await _exporter.SetupChangeLogAsync(_connectionString, "products");
        
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        // Insert initial data
        await using var insertCommand = new SqliteCommand(@"
            INSERT INTO products (name, price, category) VALUES 
            ('Product A', 10.99, 'Category 1')", connection);
        await insertCommand.ExecuteNonQueryAsync();

        var initialCheckpoint = await _exporter.GetCurrentChangeLogCheckpointAsync(_connectionString, "products");

        // Delete the record
        await using var deleteCommand = new SqliteCommand(@"
            DELETE FROM products WHERE name = 'Product A'", connection);
        await deleteCommand.ExecuteNonQueryAsync();

        var options = new ChangeLogDeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "deletes"),
            Format = ExportFormat.Jsonl,
            IncludeDeleted = true,
            ValidateCheckpoint = false
        };

        // Act
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "products",
            initialCheckpoint,
            options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.RowsExported); // The deleted row
        Assert.NotNull(result.NewCheckpoint);
        
        // Verify the exported file contains the delete
        var exportedFile = result.ExportedFiles.First();
        var content = await File.ReadAllTextAsync(exportedFile);
        Assert.Contains("DELETE", content);
    }

    [Fact]
    public async Task ExportDeltaAsync_ExcludeDeletes_SkipsDeletedRows()
    {
        // Arrange
        await _exporter.SetupChangeLogAsync(_connectionString, "products");
        
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        // Insert and then delete
        await using var insertCommand = new SqliteCommand(@"
            INSERT INTO products (name, price, category) VALUES 
            ('Product A', 10.99, 'Category 1')", connection);
        await insertCommand.ExecuteNonQueryAsync();

        await using var deleteCommand = new SqliteCommand(@"
            DELETE FROM products WHERE name = 'Product A'", connection);
        await deleteCommand.ExecuteNonQueryAsync();

        var options = new ChangeLogDeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "no_deletes"),
            Format = ExportFormat.Jsonl,
            IncludeDeleted = false
        };

        // Act
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "products",
            lastCheckpoint: null,
            options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.RowsExported); // Only the insert, delete skipped
        Assert.Single(result.ExportedFiles);
    }

    [Fact]
    public async Task ExportDeltaAsync_NoChanges_ReturnsZeroRows()
    {
        // Arrange
        await _exporter.SetupChangeLogAsync(_connectionString, "products");
        
        var currentCheckpoint = await _exporter.GetCurrentChangeLogCheckpointAsync(_connectionString, "products");

        var options = new ChangeLogDeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "no_changes"),
            Format = ExportFormat.Jsonl,
            ValidateCheckpoint = false
        };

        // Act
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "products",
            currentCheckpoint,
            options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.RowsExported);
        Assert.NotNull(result.NewCheckpoint);
        Assert.Empty(result.ExportedFiles);
    }

    [Fact]
    public async Task ExportDeltaAsync_MultipleUpdatesToSameRow_ExportsLatestVersion()
    {
        // Arrange
        await _exporter.SetupChangeLogAsync(_connectionString, "products");
        
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        // Insert and update multiple times
        await using var insertCommand = new SqliteCommand(@"
            INSERT INTO products (name, price, category) VALUES 
            ('Product A', 10.99, 'Category 1')", connection);
        await insertCommand.ExecuteNonQueryAsync();

        await using var update1Command = new SqliteCommand(@"
            UPDATE products SET price = 15.99 WHERE name = 'Product A'", connection);
        await update1Command.ExecuteNonQueryAsync();

        await using var update2Command = new SqliteCommand(@"
            UPDATE products SET price = 20.99 WHERE name = 'Product A'", connection);
        await update2Command.ExecuteNonQueryAsync();

        var options = new ChangeLogDeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "multiple_updates"),
            Format = ExportFormat.Jsonl
        };

        // Act
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "products",
            lastCheckpoint: null,
            options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.RowsExported); // Only one row (latest version)
        
        // Verify it has the latest price
        var exportedFile = result.ExportedFiles.First();
        var content = await File.ReadAllTextAsync(exportedFile);
        Assert.Contains("20.99", content); // Latest price
    }

    [Fact]
    public async Task RemoveChangeLogAsync_ExistingInfrastructure_RemovesAll()
    {
        // Arrange
        await _exporter.SetupChangeLogAsync(_connectionString, "products");
        
        // Verify infrastructure exists
        var beforeValidation = await _exporter.ValidateChangeLogSetupAsync(_connectionString, "products");
        Assert.True(beforeValidation.IsValid);

        // Act
        var result = await _exporter.RemoveChangeLogAsync(_connectionString, "products");

        // Assert
        Assert.True(result);
        
        // Verify infrastructure is removed
        var afterValidation = await _exporter.ValidateChangeLogSetupAsync(_connectionString, "products");
        Assert.False(afterValidation.IsValid);
        Assert.Empty(afterValidation.ExistingTriggers);
    }

    [Fact]
    public async Task SetupChangeLogAsync_NonexistentTable_ReturnsFailure()
    {
        // Act
        var result = await _exporter.SetupChangeLogAsync(_connectionString, "nonexistent_table");

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Contains("does not exist"));
    }

    [Fact]
    public async Task ExportDeltaAsync_WithMaxRows_LimitsOutput()
    {
        // Arrange
        await _exporter.SetupChangeLogAsync(_connectionString, "products");
        
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        // Insert multiple records
        await using var insertCommand = new SqliteCommand(@"
            INSERT INTO products (name, price, category) VALUES 
            ('Product A', 10.99, 'Category 1'),
            ('Product B', 20.99, 'Category 2'),
            ('Product C', 30.99, 'Category 3')", connection);
        await insertCommand.ExecuteNonQueryAsync();

        var options = new ChangeLogDeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "limited"),
            Format = ExportFormat.Jsonl,
            MaxRows = 2
        };

        // Act
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "products",
            lastCheckpoint: null,
            options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.RowsExported); // Limited to 2 rows
    }

    [Fact]
    public void Constructor_NullJsonlExporter_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ChangeLogDeltaExporter(null!, _mockParquetExporter.Object));
    }

    [Fact]
    public void Constructor_NullParquetExporter_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ChangeLogDeltaExporter(_mockJsonlExporter.Object, null!));
    }

    private void SetupTestDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // Create products table for testing
        using var command = new SqliteCommand(@"
            CREATE TABLE products (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                price DECIMAL(10,2) NOT NULL,
                category TEXT NOT NULL,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP
            );
        ", connection);
        
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try
        {
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