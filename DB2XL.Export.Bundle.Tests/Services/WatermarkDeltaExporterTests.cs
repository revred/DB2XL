using DB2XL.Core.Models;
using DB2XL.Core.Services;
using DB2XL.Export.Bundle.Services;
using Microsoft.Data.Sqlite;
using Moq;
using System.Text.Json;

namespace DB2XL.Export.Bundle.Tests.Services;

/// <summary>
/// Tests for WatermarkDeltaExporter implementation.
/// Validates watermark-based incremental export functionality.
/// </summary>
public class WatermarkDeltaExporterTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _testDbPath;
    private readonly string _connectionString;
    private readonly WatermarkDeltaExporter _exporter;
    private readonly Mock<IJsonlExportEngine> _mockJsonlExporter;
    private readonly Mock<IParquetExportEngine> _mockParquetExporter;
    
    public WatermarkDeltaExporterTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"delta_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        
        _testDbPath = Path.Combine(_tempDirectory, "test.db");
        _connectionString = $"Data Source={_testDbPath};";
        
        _mockJsonlExporter = new Mock<IJsonlExportEngine>();
        _mockParquetExporter = new Mock<IParquetExportEngine>();
        
        _exporter = new WatermarkDeltaExporter(
            _mockJsonlExporter.Object,
            _mockParquetExporter.Object);
        
        SetupTestDatabase();
    }

    [Fact]
    public async Task ValidateWatermarkSetupAsync_ValidTable_ReturnsValid()
    {
        // Act
        var result = await _exporter.ValidateWatermarkSetupAsync(
            _connectionString, 
            "orders", 
            "updated_at");

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("DATETIME", result.WatermarkColumnType);
        Assert.Contains("id", result.PrimaryKeyColumns);
        Assert.False(result.WatermarkColumnIndexed);
        Assert.Contains(result.Suggestions, s => s.Contains("index"));
    }

    [Fact]
    public async Task ValidateWatermarkSetupAsync_NonexistentTable_ReturnsInvalid()
    {
        // Act
        var result = await _exporter.ValidateWatermarkSetupAsync(
            _connectionString, 
            "nonexistent", 
            "updated_at");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("does not exist"));
    }

    [Fact]
    public async Task ValidateWatermarkSetupAsync_NonexistentColumn_ReturnsInvalid()
    {
        // Act
        var result = await _exporter.ValidateWatermarkSetupAsync(
            _connectionString, 
            "orders", 
            "nonexistent_column");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("does not exist"));
    }

    [Fact]
    public async Task ValidateWatermarkSetupAsync_InvalidColumnType_ReturnsInvalid()
    {
        // Arrange - create table with invalid watermark type
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var command = new SqliteCommand(@"
            CREATE TABLE invalid_watermark (
                id INTEGER PRIMARY KEY,
                data BLOB
            )", connection);
        await command.ExecuteNonQueryAsync();

        // Act
        var result = await _exporter.ValidateWatermarkSetupAsync(
            _connectionString, 
            "invalid_watermark", 
            "data");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("not suitable for watermark"));
    }

    [Fact]
    public async Task GetCurrentCheckpointAsync_WithData_ReturnsCorrectCheckpoint()
    {
        // Act
        var checkpoint = await _exporter.GetCurrentCheckpointAsync(
            _connectionString,
            "orders",
            "updated_at");

        // Assert
        Assert.Equal("orders", checkpoint.TableName);
        Assert.Equal("updated_at", checkpoint.WatermarkColumn);
        Assert.NotNull(checkpoint.LastWatermarkValue);
        Assert.NotNull(checkpoint.LastPrimaryKeyValue);
        Assert.Equal(3, checkpoint.RowsProcessed); // 3 test rows
    }

    [Fact]
    public async Task GetCurrentCheckpointAsync_EmptyTable_ReturnsEmptyCheckpoint()
    {
        // Arrange - create empty table
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var command = new SqliteCommand(@"
            CREATE TABLE empty_orders (
                id INTEGER PRIMARY KEY,
                updated_at DATETIME
            )", connection);
        await command.ExecuteNonQueryAsync();

        // Act
        var checkpoint = await _exporter.GetCurrentCheckpointAsync(
            _connectionString,
            "empty_orders",
            "updated_at");

        // Assert
        Assert.Equal("empty_orders", checkpoint.TableName);
        Assert.Equal("updated_at", checkpoint.WatermarkColumn);
        Assert.Null(checkpoint.LastWatermarkValue);
        Assert.Null(checkpoint.LastPrimaryKeyValue);
        Assert.Equal(0, checkpoint.RowsProcessed);
    }

    [Fact]
    public async Task ExportDeltaAsync_InitialExport_ExportsAllRows()
    {
        // Arrange
        var options = new DeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "delta"),
            Format = ExportFormat.Jsonl,
            BatchSize = 2
        };

        // Act
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "orders",
            "updated_at",
            lastCheckpoint: null,
            options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.RowsExported); // All 3 test rows
        Assert.NotNull(result.NewCheckpoint);
        Assert.Equal("orders", result.NewCheckpoint.TableName);
        Assert.Equal("updated_at", result.NewCheckpoint.WatermarkColumn);
        Assert.Equal(3, result.NewCheckpoint.RowsProcessed);
        Assert.Single(result.ExportedFiles);
    }

    [Fact]
    public async Task ExportDeltaAsync_IncrementalExport_ExportsOnlyNewRows()
    {
        // Arrange - create checkpoint from previous export
        var previousCheckpoint = new DeltaCheckpoint
        {
            TableName = "orders",
            WatermarkColumn = "updated_at",
            LastWatermarkValue = "2023-01-02T10:00:00Z",
            LastPrimaryKeyValue = 2L,
            RowsProcessed = 2,
            CheckpointTimestamp = DateTime.UtcNow.AddHours(-1),
            SelectionHash = string.Empty
        };

        var options = new DeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "incremental"),
            Format = ExportFormat.Jsonl,
            ValidateCheckpoint = false // Skip validation for test
        };

        // Act
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "orders",
            "updated_at",
            previousCheckpoint,
            options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.RowsExported); // Only the newest row
        Assert.NotNull(result.NewCheckpoint);
        Assert.Equal(3, result.NewCheckpoint.RowsProcessed); // 2 + 1 new
        Assert.Single(result.ExportedFiles);
    }

    [Fact]
    public async Task ExportDeltaAsync_NoNewData_ReturnsZeroRows()
    {
        // Arrange - checkpoint at the latest data
        var currentCheckpoint = await _exporter.GetCurrentCheckpointAsync(
            _connectionString,
            "orders", 
            "updated_at");

        var options = new DeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "no_new_data"),
            Format = ExportFormat.Jsonl,
            ValidateCheckpoint = false
        };

        // Act
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "orders",
            "updated_at",
            currentCheckpoint,
            options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.RowsExported);
        Assert.NotNull(result.NewCheckpoint);
        // When no new data, should keep existing checkpoint but update timestamp
        Assert.Equal(currentCheckpoint.RowsProcessed, result.NewCheckpoint.RowsProcessed);
    }

    [Fact]
    public async Task ExportDeltaAsync_WithMaxRows_LimitsOutput()
    {
        // Arrange
        var options = new DeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "limited"),
            Format = ExportFormat.Jsonl,
            MaxRows = 2
        };

        // Act
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "orders",
            "updated_at",
            lastCheckpoint: null,
            options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.RowsExported); // Limited to 2 rows
        Assert.NotNull(result.NewCheckpoint);
    }

    [Fact]
    public async Task ExportDeltaAsync_InvalidWatermarkColumn_ReturnsFailure()
    {
        // Arrange
        var options = new DeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "invalid"),
            Format = ExportFormat.Jsonl
        };

        // Act
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "orders",
            "nonexistent_column",
            lastCheckpoint: null,
            options);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Contains("does not exist"));
        Assert.Equal(0, result.RowsExported);
        Assert.Null(result.NewCheckpoint);
    }

    [Fact]
    public async Task ExportDeltaAsync_TableWithoutPrimaryKey_UsesRowid()
    {
        // Arrange - create table without explicit primary key
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var createCommand = new SqliteCommand(@"
            CREATE TABLE logs (
                message TEXT,
                created_at DATETIME
            )", connection);
        await createCommand.ExecuteNonQueryAsync();

        await using var insertCommand = new SqliteCommand(@"
            INSERT INTO logs (message, created_at) VALUES 
            ('Log 1', '2023-01-01T10:00:00Z'),
            ('Log 2', '2023-01-02T10:00:00Z')", connection);
        await insertCommand.ExecuteNonQueryAsync();

        var options = new DeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "no_pk"),
            Format = ExportFormat.Jsonl
        };

        // Act
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "logs",
            "created_at",
            lastCheckpoint: null,
            options);

        // Assert
        Assert.True(result.IsSuccess, $"Export failed with errors: {string.Join("; ", result.Errors)}");
        Assert.Equal(2, result.RowsExported);
        Assert.NotNull(result.NewCheckpoint);
        Assert.Contains(result.Warnings, w => w.Contains("rowid"));
    }

    [Fact]
    public async Task ExportDeltaAsync_CheckpointValidationFailure_ReturnsFailure()
    {
        // Arrange - invalid checkpoint (wrong table)
        var invalidCheckpoint = new DeltaCheckpoint
        {
            TableName = "wrong_table",
            WatermarkColumn = "updated_at",
            LastWatermarkValue = "2023-01-01T10:00:00Z",
            SelectionHash = "invalid_hash"
        };

        var options = new DeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "validation_fail"),
            Format = ExportFormat.Jsonl,
            ValidateCheckpoint = true
        };

        // Act
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "orders",
            "updated_at",
            invalidCheckpoint,
            options);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Contains("Checkpoint validation failed"));
    }

    [Fact]
    public async Task ExportDeltaAsync_TieBreaking_HandlesEqualWatermarks()
    {
        // Arrange - add more data with same timestamp
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        await using var command = new SqliteCommand(@"
            INSERT INTO orders (customer_id, amount, updated_at) VALUES 
            ('customer4', 400.00, '2023-01-03T10:00:00Z'),
            ('customer5', 500.00, '2023-01-03T10:00:00Z')", connection);
        await command.ExecuteNonQueryAsync();

        // Create checkpoint at first row with that timestamp
        var checkpoint = new DeltaCheckpoint
        {
            TableName = "orders",
            WatermarkColumn = "updated_at",
            LastWatermarkValue = "2023-01-03T10:00:00Z",
            LastPrimaryKeyValue = 3L, // First row with this timestamp
            RowsProcessed = 3,
            SelectionHash = string.Empty
        };

        var options = new DeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "tie_breaking"),
            Format = ExportFormat.Jsonl,
            ValidateCheckpoint = false
        };

        // Act
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "orders",
            "updated_at",
            checkpoint,
            options);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.RowsExported); // Should get rows 4 and 5
        Assert.NotNull(result.NewCheckpoint);
        Assert.Equal(5L, result.NewCheckpoint.LastPrimaryKeyValue); // Last PK processed
    }

    [Fact]
    public async Task ExportDeltaAsync_UnsupportedFormat_ThrowsException()
    {
        // Arrange
        var options = new DeltaExportOptions
        {
            OutputDirectory = Path.Combine(_tempDirectory, "unsupported"),
            Format = ExportFormat.Parquet // Not yet implemented
        };

        // Act & Assert
        var result = await _exporter.ExportDeltaAsync(
            _connectionString,
            "orders",
            "updated_at",
            lastCheckpoint: null,
            options);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Contains("failed"));
    }

    [Theory]
    [InlineData("INTEGER")]
    [InlineData("REAL")]
    [InlineData("TEXT")]
    [InlineData("DATETIME")]
    [InlineData("TIMESTAMP")]
    public async Task ValidateWatermarkSetupAsync_ValidTypes_AcceptsType(string columnType)
    {
        // Arrange
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        var tableName = $"test_{columnType.ToLowerInvariant()}";
        await using var command = new SqliteCommand($@"
            CREATE TABLE {tableName} (
                id INTEGER PRIMARY KEY,
                watermark {columnType}
            )", connection);
        await command.ExecuteNonQueryAsync();

        // Act
        var result = await _exporter.ValidateWatermarkSetupAsync(
            _connectionString,
            tableName,
            "watermark");

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(columnType, result.WatermarkColumnType);
    }

    [Fact]
    public void Constructor_NullJsonlExporter_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new WatermarkDeltaExporter(null!, _mockParquetExporter.Object));
    }

    [Fact]
    public void Constructor_NullParquetExporter_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new WatermarkDeltaExporter(_mockJsonlExporter.Object, null!));
    }

    private void SetupTestDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // Create orders table with test data
        using var command = new SqliteCommand(@"
            CREATE TABLE orders (
                id INTEGER PRIMARY KEY,
                customer_id TEXT NOT NULL,
                amount DECIMAL(10,2) NOT NULL,
                updated_at DATETIME NOT NULL
            );

            INSERT INTO orders (customer_id, amount, updated_at) VALUES 
            ('customer1', 100.50, '2023-01-01T10:00:00Z'),
            ('customer2', 200.75, '2023-01-02T10:00:00Z'),
            ('customer3', 300.25, '2023-01-03T10:00:00Z');
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