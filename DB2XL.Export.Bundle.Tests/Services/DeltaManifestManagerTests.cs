using DB2XL.Core.Models;
using DB2XL.Core.Services;
using DB2XL.Export.Bundle.Services;
using System.Text.Json;

namespace DB2XL.Export.Bundle.Tests.Services;

/// <summary>
/// Tests for DeltaManifestManager implementation.
/// Validates manifest persistence, checkpoint management, and partition tracking.
/// </summary>
public class DeltaManifestManagerTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly DeltaManifestManager _manager;
    
    public DeltaManifestManagerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"delta_manifest_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        _manager = new DeltaManifestManager();
    }

    [Fact]
    public async Task LoadDeltaManifestAsync_NoManifest_ReturnsEmptyManifest()
    {
        // Act
        var manifest = await _manager.LoadDeltaManifestAsync(_tempDirectory);

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal("1.0", manifest.Version);
        Assert.Empty(manifest.Tables);
        Assert.Equal(0, manifest.GlobalInfo.TotalExports);
    }

    [Fact]
    public async Task SaveAndLoadDeltaManifestAsync_RoundTrip_PreservesData()
    {
        // Arrange
        var originalManifest = new DeltaManifest
        {
            Version = "1.0",
            Tables = new Dictionary<string, Dictionary<string, TableDeltaInfo>>
            {
                ["users"] = new Dictionary<string, TableDeltaInfo>
                {
                    ["hash123"] = new TableDeltaInfo
                    {
                        SelectionHash = "hash123",
                        WatermarkCheckpoint = new DeltaCheckpoint
                        {
                            TableName = "users",
                            WatermarkColumn = "updated_at",
                            LastWatermarkValue = "2023-01-01T00:00:00Z",
                            RowsProcessed = 100,
                            SelectionHash = "hash123"
                        },
                        Stats = new DeltaExportStats
                        {
                            ExportCount = 1,
                            TotalRowsExported = 100,
                            LastExportDuration = TimeSpan.FromSeconds(5)
                        }
                    }
                }
            },
            GlobalInfo = new DeltaGlobalInfo
            {
                TotalExports = 1,
                TotalRowsExported = 100,
                FirstExportTime = DateTime.UtcNow
            }
        };

        // Act
        await _manager.SaveDeltaManifestAsync(_tempDirectory, originalManifest);
        var loadedManifest = await _manager.LoadDeltaManifestAsync(_tempDirectory);

        // Assert
        Assert.Equal(originalManifest.Version, loadedManifest.Version);
        Assert.Single(loadedManifest.Tables);
        Assert.Contains("users", loadedManifest.Tables.Keys);
        
        var usersTable = loadedManifest.Tables["users"];
        Assert.Single(usersTable);
        Assert.Contains("hash123", usersTable.Keys);
        
        var deltaInfo = usersTable["hash123"];
        Assert.Equal("hash123", deltaInfo.SelectionHash);
        Assert.NotNull(deltaInfo.WatermarkCheckpoint);
        Assert.Equal("users", deltaInfo.WatermarkCheckpoint.TableName);
        Assert.Equal(100, deltaInfo.WatermarkCheckpoint.RowsProcessed);
    }

    [Fact]
    public async Task UpdateDeltaManifestAsync_NewTable_CreatesTableEntry()
    {
        // Arrange
        var exportResult = CreateSuccessfulExportResult(
            rowsExported: 50,
            checkpoint: new DeltaCheckpoint
            {
                TableName = "products",
                WatermarkColumn = "modified_at",
                LastWatermarkValue = "2023-05-01T10:00:00Z",
                RowsProcessed = 50,
                SelectionHash = "productHash"
            }
        );

        // Act
        await _manager.UpdateDeltaManifestAsync(
            _tempDirectory,
            "products",
            exportResult,
            DeltaExportMode.Watermark);

        // Assert
        var manifest = await _manager.LoadDeltaManifestAsync(_tempDirectory);
        Assert.Single(manifest.Tables);
        Assert.Contains("products", manifest.Tables.Keys);
        
        var productTable = manifest.Tables["products"];
        Assert.Single(productTable);
        Assert.Contains("productHash", productTable.Keys);
        
        var deltaInfo = productTable["productHash"];
        Assert.NotNull(deltaInfo.WatermarkCheckpoint);
        Assert.Equal("products", deltaInfo.WatermarkCheckpoint.TableName);
        Assert.Equal(1, deltaInfo.Stats.ExportCount);
        Assert.Equal(50, deltaInfo.Stats.TotalRowsExported);
    }

    [Fact]
    public async Task UpdateDeltaManifestAsync_ExistingTable_UpdatesStats()
    {
        // Arrange - first export
        var firstExportResult = CreateSuccessfulExportResult(
            rowsExported: 100,
            checkpoint: new DeltaCheckpoint
            {
                TableName = "orders",
                WatermarkColumn = "created_at",
                LastWatermarkValue = "2023-04-01T00:00:00Z",
                RowsProcessed = 100,
                SelectionHash = "orderHash"
            }
        );

        await _manager.UpdateDeltaManifestAsync(
            _tempDirectory,
            "orders",
            firstExportResult,
            DeltaExportMode.Watermark);

        // Act - second export
        var secondExportResult = CreateSuccessfulExportResult(
            rowsExported: 25,
            checkpoint: new DeltaCheckpoint
            {
                TableName = "orders",
                WatermarkColumn = "created_at",
                LastWatermarkValue = "2023-04-02T00:00:00Z",
                RowsProcessed = 125,
                SelectionHash = "orderHash"
            }
        );

        await _manager.UpdateDeltaManifestAsync(
            _tempDirectory,
            "orders",
            secondExportResult,
            DeltaExportMode.Watermark);

        // Assert
        var manifest = await _manager.LoadDeltaManifestAsync(_tempDirectory);
        var deltaInfo = manifest.Tables["orders"]["orderHash"];
        
        Assert.Equal(2, deltaInfo.Stats.ExportCount);
        Assert.Equal(125, deltaInfo.Stats.TotalRowsExported);
        Assert.Equal(62.5, deltaInfo.Stats.AverageRowsPerExport);
        Assert.Equal(125, deltaInfo.WatermarkCheckpoint!.RowsProcessed);
    }

    [Fact]
    public async Task UpdateDeltaManifestAsync_ChangeLogMode_UpdatesChangeLogCheckpoint()
    {
        // Arrange
        var exportResult = CreateSuccessfulExportResult(
            rowsExported: 75,
            checkpoint: new DeltaCheckpoint
            {
                TableName = "logs",
                WatermarkColumn = "change_id",
                LastWatermarkValue = 1500L,
                RowsProcessed = 75,
                SelectionHash = "logHash"
            }
        );

        // Act
        await _manager.UpdateDeltaManifestAsync(
            _tempDirectory,
            "logs",
            exportResult,
            DeltaExportMode.ChangeLog);

        // Assert
        var manifest = await _manager.LoadDeltaManifestAsync(_tempDirectory);
        var deltaInfo = manifest.Tables["logs"]["logHash"];
        
        Assert.NotNull(deltaInfo.ChangeLogCheckpoint);
        Assert.Null(deltaInfo.WatermarkCheckpoint);
        Assert.Equal("logs", deltaInfo.ChangeLogCheckpoint.TableName);
        Assert.Equal(1500L, deltaInfo.ChangeLogCheckpoint.LastWatermarkValue);
    }

    [Fact]
    public async Task UpdateDeltaManifestAsync_FailedExport_DoesNotUpdateManifest()
    {
        // Arrange
        var failedResult = new DeltaExportResult
        {
            IsSuccess = false,
            RowsExported = 0,
            NewCheckpoint = null,
            Errors = new[] { "Export failed" }.AsReadOnly()
        };

        var originalManifest = await _manager.LoadDeltaManifestAsync(_tempDirectory);
        var originalTableCount = originalManifest.Tables.Count;

        // Act
        await _manager.UpdateDeltaManifestAsync(
            _tempDirectory,
            "failed_table",
            failedResult,
            DeltaExportMode.Watermark);

        // Assert
        var updatedManifest = await _manager.LoadDeltaManifestAsync(_tempDirectory);
        Assert.Equal(originalTableCount, updatedManifest.Tables.Count);
    }

    [Fact]
    public async Task GetLatestCheckpointAsync_ExistingCheckpoint_ReturnsCheckpoint()
    {
        // Arrange
        var exportResult = CreateSuccessfulExportResult(
            rowsExported: 30,
            checkpoint: new DeltaCheckpoint
            {
                TableName = "customers",
                WatermarkColumn = "last_modified",
                LastWatermarkValue = "2023-03-15T14:30:00Z",
                RowsProcessed = 30,
                SelectionHash = "customerHash"
            }
        );

        await _manager.UpdateDeltaManifestAsync(
            _tempDirectory,
            "customers",
            exportResult,
            DeltaExportMode.Watermark);

        // Act
        var checkpoint = await _manager.GetLatestCheckpointAsync(
            _tempDirectory,
            "customers",
            "customerHash",
            DeltaExportMode.Watermark);

        // Assert
        Assert.NotNull(checkpoint);
        Assert.Equal("customers", checkpoint.TableName);
        Assert.Equal("last_modified", checkpoint.WatermarkColumn);
        Assert.Equal("2023-03-15T14:30:00Z", checkpoint.LastWatermarkValue);
        Assert.Equal(30, checkpoint.RowsProcessed);
    }

    [Fact]
    public async Task GetLatestCheckpointAsync_NonexistentTable_ReturnsNull()
    {
        // Act
        var checkpoint = await _manager.GetLatestCheckpointAsync(
            _tempDirectory,
            "nonexistent",
            "hash",
            DeltaExportMode.Watermark);

        // Assert
        Assert.Null(checkpoint);
    }

    [Fact]
    public async Task AppendPartitionInfoAsync_NewPartition_AddsToManifest()
    {
        // Arrange
        var testFile = Path.Combine(_tempDirectory, "test_export.jsonl");
        await File.WriteAllTextAsync(testFile, "{\"id\": 1}\n{\"id\": 2}");

        var exportResult = CreateSuccessfulExportResult(
            rowsExported: 2,
            exportedFiles: new[] { testFile }
        );

        // Act
        await _manager.AppendPartitionInfoAsync(
            _tempDirectory,
            "test_table",
            exportResult,
            "delta_001");

        // Assert
        var partitionsPath = Path.Combine(_tempDirectory, "manifest", "partitions.json");
        Assert.True(File.Exists(partitionsPath));

        var partitionsJson = await File.ReadAllTextAsync(partitionsPath);
        var partitionsManifest = JsonSerializer.Deserialize<Dictionary<string, object>>(partitionsJson);
        
        Assert.NotNull(partitionsManifest);
        Assert.Contains("test_table", partitionsManifest.Keys);
    }

    [Fact]
    public async Task BackupDeltaManifestAsync_ExistingManifest_CreatesBackup()
    {
        // Arrange
        var manifest = new DeltaManifest();
        await _manager.SaveDeltaManifestAsync(_tempDirectory, manifest);

        var backupSuffix = "backup_20230501";

        // Act
        await _manager.BackupDeltaManifestAsync(_tempDirectory, backupSuffix);

        // Assert
        var backupPath = Path.Combine(_tempDirectory, "manifest", $"delta.{backupSuffix}.json");
        Assert.True(File.Exists(backupPath));
        
        // Verify backup content
        var backupManifest = await _manager.LoadDeltaManifestAsync(backupPath.Replace("delta.json", ""));
        Assert.NotNull(backupManifest);
    }

    [Fact]
    public async Task BackupDeltaManifestAsync_NoManifest_DoesNotThrow()
    {
        // Act & Assert (should not throw)
        await _manager.BackupDeltaManifestAsync(_tempDirectory, "test");
    }

    [Fact]
    public async Task ValidateDeltaManifestAsync_ValidManifest_ReturnsValid()
    {
        // Arrange
        var exportResult = CreateSuccessfulExportResult(
            rowsExported: 10,
            checkpoint: new DeltaCheckpoint
            {
                TableName = "validation_test",
                WatermarkColumn = "timestamp",
                LastWatermarkValue = "2023-01-01T00:00:00Z",
                RowsProcessed = 10,
                SelectionHash = "validHash"
            }
        );

        await _manager.UpdateDeltaManifestAsync(
            _tempDirectory,
            "validation_test",
            exportResult,
            DeltaExportMode.Watermark);

        // Act
        var result = await _manager.ValidateDeltaManifestAsync(_tempDirectory);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(1, result.TableCount);
        Assert.Equal(1, result.TotalCheckpoints);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ValidateDeltaManifestAsync_InvalidCheckpoint_ReturnsErrors()
    {
        // Arrange - create invalid manifest with mismatched table names
        var invalidManifest = new DeltaManifest
        {
            Tables = new Dictionary<string, Dictionary<string, TableDeltaInfo>>
            {
                ["users"] = new Dictionary<string, TableDeltaInfo>
                {
                    ["hash"] = new TableDeltaInfo
                    {
                        SelectionHash = "hash",
                        WatermarkCheckpoint = new DeltaCheckpoint
                        {
                            TableName = "orders", // Mismatch!
                            WatermarkColumn = "updated_at",
                            RowsProcessed = 50,
                            SelectionHash = "hash"
                        }
                    }
                }
            }
        };

        await _manager.SaveDeltaManifestAsync(_tempDirectory, invalidManifest);

        // Act
        var result = await _manager.ValidateDeltaManifestAsync(_tempDirectory);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("table name mismatch"));
    }

    [Fact]
    public async Task ValidateDeltaManifestAsync_NoCheckpoints_ReturnsWarnings()
    {
        // Arrange
        var manifestWithNoCheckpoints = new DeltaManifest
        {
            Tables = new Dictionary<string, Dictionary<string, TableDeltaInfo>>
            {
                ["empty_table"] = new Dictionary<string, TableDeltaInfo>
                {
                    ["hash"] = new TableDeltaInfo
                    {
                        SelectionHash = "hash",
                        WatermarkCheckpoint = null,
                        ChangeLogCheckpoint = null
                    }
                }
            }
        };

        await _manager.SaveDeltaManifestAsync(_tempDirectory, manifestWithNoCheckpoints);

        // Act
        var result = await _manager.ValidateDeltaManifestAsync(_tempDirectory);

        // Assert
        Assert.True(result.IsValid); // Valid but with warnings
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("no checkpoints"));
    }

    [Fact]
    public async Task ValidateDeltaManifestAsync_HighVolumeData_ReturnsSuggestions()
    {
        // Arrange
        var highVolumeManifest = new DeltaManifest
        {
            GlobalInfo = new DeltaGlobalInfo
            {
                TotalExports = 1500, // High volume
                TotalRowsExported = 1_000_000
            }
        };

        await _manager.SaveDeltaManifestAsync(_tempDirectory, highVolumeManifest);

        // Act
        var result = await _manager.ValidateDeltaManifestAsync(_tempDirectory);

        // Assert
        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Suggestions);
        Assert.Contains(result.Suggestions, s => s.Contains("archiving"));
    }

    private static DeltaExportResult CreateSuccessfulExportResult(
        long rowsExported,
        DeltaCheckpoint? checkpoint = null,
        IReadOnlyList<string>? exportedFiles = null)
    {
        return new DeltaExportResult
        {
            IsSuccess = true,
            RowsExported = rowsExported,
            NewCheckpoint = checkpoint,
            ExportedFiles = exportedFiles ?? Array.Empty<string>(),
            Duration = TimeSpan.FromSeconds(2),
            Errors = Array.Empty<string>(),
            Warnings = Array.Empty<string>()
        };
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