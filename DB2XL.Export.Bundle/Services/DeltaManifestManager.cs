using DB2XL.Core.Models;
using DB2XL.Core.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DB2XL.Export.Bundle.Services;

/// <summary>
/// Implementation of delta manifest management for tracking export checkpoints and metadata.
/// Manages delta.json and updates to partitions.json for incremental exports.
/// </summary>
public sealed class DeltaManifestManager : IDeltaManifestManager
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <inheritdoc />
    public async Task<DeltaManifest> LoadDeltaManifestAsync(
        string bundleDirectory,
        CancellationToken cancellationToken = default)
    {
        var manifestPath = GetDeltaManifestPath(bundleDirectory);
        
        if (!File.Exists(manifestPath))
        {
            return new DeltaManifest();
        }
        
        try
        {
            var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<DeltaManifest>(json, _jsonOptions);
            return manifest ?? new DeltaManifest();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load delta manifest from {manifestPath}: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task SaveDeltaManifestAsync(
        string bundleDirectory,
        DeltaManifest deltaManifest,
        CancellationToken cancellationToken = default)
    {
        var manifestPath = GetDeltaManifestPath(bundleDirectory);
        var manifestDir = Path.GetDirectoryName(manifestPath);
        
        if (!string.IsNullOrEmpty(manifestDir))
        {
            Directory.CreateDirectory(manifestDir);
        }
        
        try
        {
            var updatedManifest = deltaManifest with { LastUpdated = DateTime.UtcNow };
            var json = JsonSerializer.Serialize(updatedManifest, _jsonOptions);
            await File.WriteAllTextAsync(manifestPath, json, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save delta manifest to {manifestPath}: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task UpdateDeltaManifestAsync(
        string bundleDirectory,
        string tableName,
        DeltaExportResult exportResult,
        DeltaExportMode deltaMode,
        CancellationToken cancellationToken = default)
    {
        if (!exportResult.IsSuccess || exportResult.NewCheckpoint == null)
        {
            return; // Don't update manifest for failed exports
        }
        
        var manifest = await LoadDeltaManifestAsync(bundleDirectory, cancellationToken);
        
        // Ensure table entry exists
        if (!manifest.Tables.ContainsKey(tableName))
        {
            manifest.Tables[tableName] = new Dictionary<string, TableDeltaInfo>();
        }
        
        var selectionHash = exportResult.NewCheckpoint.SelectionHash;
        var tableDeltas = manifest.Tables[tableName];
        
        // Get existing delta info or create new
        var existingInfo = tableDeltas.TryGetValue(selectionHash, out var existing) ? existing : null;
        
        var newStats = CalculateUpdatedStats(existingInfo?.Stats, exportResult);
        
        var newDeltaInfo = new TableDeltaInfo
        {
            SelectionHash = selectionHash,
            Stats = newStats,
            LastExportTime = DateTime.UtcNow,
            WatermarkCheckpoint = deltaMode == DeltaExportMode.Watermark ? exportResult.NewCheckpoint : existingInfo?.WatermarkCheckpoint,
            ChangeLogCheckpoint = deltaMode == DeltaExportMode.ChangeLog ? exportResult.NewCheckpoint : existingInfo?.ChangeLogCheckpoint
        };
        
        tableDeltas[selectionHash] = newDeltaInfo;
        
        // Update global stats
        var updatedGlobalInfo = UpdateGlobalInfo(manifest.GlobalInfo, exportResult);
        var updatedManifest = manifest with 
        { 
            Tables = manifest.Tables,
            GlobalInfo = updatedGlobalInfo
        };
        
        await SaveDeltaManifestAsync(bundleDirectory, updatedManifest, cancellationToken);
    }

    /// <inheritdoc />
    public async Task AppendPartitionInfoAsync(
        string bundleDirectory,
        string tableName,
        DeltaExportResult exportResult,
        string partitionLabel,
        CancellationToken cancellationToken = default)
    {
        if (!exportResult.IsSuccess || exportResult.ExportedFiles.Count == 0)
        {
            return;
        }
        
        var partitionsPath = GetPartitionsManifestPath(bundleDirectory);
        
        // Load existing partitions manifest
        var partitionsManifest = await LoadPartitionsManifestAsync(partitionsPath, cancellationToken);
        
        // Ensure table entry exists
        if (!partitionsManifest.ContainsKey(tableName))
        {
            partitionsManifest[tableName] = new DeltaPartitionTableInfo
            {
                Strategy = "delta",
                Parts = new List<DeltaPartitionInfo>()
            };
        }
        
        var tableInfo = partitionsManifest[tableName];
        var parts = tableInfo.Parts.ToList();
        
        // Add new partition info for each exported file
        foreach (var filePath in exportResult.ExportedFiles)
        {
            var relativePath = Path.GetRelativePath(bundleDirectory, filePath);
            var fileSize = File.Exists(filePath) ? new FileInfo(filePath).Length : 0;
            var checksum = await ComputeFileChecksumAsync(filePath, cancellationToken);
            
            var partitionInfo = new DeltaPartitionInfo
            {
                Path = relativePath.Replace('\\', '/'), // Use forward slashes for portability
                Rows = exportResult.RowsExported,
                Sha256 = checksum,
                Label = partitionLabel,
                FileSizeBytes = fileSize,
                ExportTimestamp = DateTime.UtcNow,
                FirstPk = GetFirstPrimaryKey(exportResult),
                LastPk = GetLastPrimaryKey(exportResult)
            };
            
            parts.Add(partitionInfo);
        }
        
        // Update table info
        partitionsManifest[tableName] = tableInfo with { Parts = parts.AsReadOnly() };
        
        // Save updated partitions manifest
        await SavePartitionsManifestAsync(partitionsPath, partitionsManifest, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DeltaCheckpoint?> GetLatestCheckpointAsync(
        string bundleDirectory,
        string tableName,
        string selectionHash,
        DeltaExportMode deltaMode)
    {
        var manifest = await LoadDeltaManifestAsync(bundleDirectory);
        
        if (!manifest.Tables.TryGetValue(tableName, out var tableDeltas) ||
            !tableDeltas.TryGetValue(selectionHash, out var deltaInfo))
        {
            return null;
        }
        
        return deltaMode switch
        {
            DeltaExportMode.Watermark => deltaInfo.WatermarkCheckpoint,
            DeltaExportMode.ChangeLog => deltaInfo.ChangeLogCheckpoint,
            _ => null
        };
    }

    /// <inheritdoc />
    public async Task BackupDeltaManifestAsync(
        string bundleDirectory,
        string backupSuffix,
        CancellationToken cancellationToken = default)
    {
        var manifestPath = GetDeltaManifestPath(bundleDirectory);
        
        if (!File.Exists(manifestPath))
        {
            return; // No manifest to backup
        }
        
        var backupPath = Path.ChangeExtension(manifestPath, $".{backupSuffix}.json");
        using var sourceStream = File.OpenRead(manifestPath);
        using var destinationStream = File.Create(backupPath);
        await sourceStream.CopyToAsync(destinationStream, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<DeltaManifestValidationResult> ValidateDeltaManifestAsync(string bundleDirectory)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var suggestions = new List<string>();
        
        try
        {
            var manifest = await LoadDeltaManifestAsync(bundleDirectory);
            
            var tableCount = manifest.Tables.Count;
            var totalCheckpoints = manifest.Tables.Values
                .SelectMany(dict => dict.Values)
                .Count(info => info.WatermarkCheckpoint != null || info.ChangeLogCheckpoint != null);
            
            // Validate manifest structure
            if (string.IsNullOrEmpty(manifest.Version))
            {
                warnings.Add("Delta manifest missing version information");
            }
            
            // Validate table entries
            foreach (var (tableName, tableDeltas) in manifest.Tables)
            {
                if (string.IsNullOrEmpty(tableName))
                {
                    errors.Add("Found table with empty name");
                    continue;
                }
                
                foreach (var (selectionHash, deltaInfo) in tableDeltas)
                {
                    if (string.IsNullOrEmpty(selectionHash))
                    {
                        warnings.Add($"Table {tableName} has entry with empty selection hash");
                    }
                    
                    if (deltaInfo.WatermarkCheckpoint == null && deltaInfo.ChangeLogCheckpoint == null)
                    {
                        warnings.Add($"Table {tableName} has no checkpoints for selection {selectionHash}");
                    }
                    
                    // Validate checkpoint consistency
                    var watermarkCheckpoint = deltaInfo.WatermarkCheckpoint;
                    if (watermarkCheckpoint != null)
                    {
                        if (!watermarkCheckpoint.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add($"Watermark checkpoint table name mismatch: expected {tableName}, got {watermarkCheckpoint.TableName}");
                        }
                    }
                    
                    var changeLogCheckpoint = deltaInfo.ChangeLogCheckpoint;
                    if (changeLogCheckpoint != null)
                    {
                        if (!changeLogCheckpoint.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add($"Change log checkpoint table name mismatch: expected {tableName}, got {changeLogCheckpoint.TableName}");
                        }
                    }
                }
            }
            
            // Performance suggestions
            if (totalCheckpoints > 100)
            {
                suggestions.Add("Consider periodic cleanup of old checkpoints for better performance");
            }
            
            if (manifest.GlobalInfo.TotalExports > 1000)
            {
                suggestions.Add("High export count detected - consider archiving old delta manifests");
            }
            
            return new DeltaManifestValidationResult
            {
                IsValid = errors.Count == 0,
                TableCount = tableCount,
                TotalCheckpoints = totalCheckpoints,
                Errors = errors.AsReadOnly(),
                Warnings = warnings.AsReadOnly(),
                Suggestions = suggestions.AsReadOnly()
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to validate delta manifest: {ex.Message}");
            return new DeltaManifestValidationResult
            {
                IsValid = false,
                TableCount = 0,
                TotalCheckpoints = 0,
                Errors = errors.AsReadOnly(),
                Warnings = warnings.AsReadOnly(),
                Suggestions = suggestions.AsReadOnly()
            };
        }
    }
    
    private static DeltaExportStats CalculateUpdatedStats(DeltaExportStats? existingStats, DeltaExportResult exportResult)
    {
        var existingCount = existingStats?.ExportCount ?? 0;
        var existingTotal = existingStats?.TotalRowsExported ?? 0;
        var existingAvgDuration = existingStats?.AverageExportDuration;
        
        var newCount = existingCount + 1;
        var newTotal = existingTotal + exportResult.RowsExported;
        
        // Calculate new average duration
        TimeSpan? newAvgDuration = null;
        if (existingAvgDuration.HasValue && existingCount > 0)
        {
            var totalMs = existingAvgDuration.Value.TotalMilliseconds * existingCount + exportResult.Duration.TotalMilliseconds;
            newAvgDuration = TimeSpan.FromMilliseconds(totalMs / newCount);
        }
        else
        {
            newAvgDuration = exportResult.Duration;
        }
        
        return new DeltaExportStats
        {
            ExportCount = newCount,
            TotalRowsExported = newTotal,
            LastExportDuration = exportResult.Duration,
            AverageExportDuration = newAvgDuration
        };
    }
    
    private static DeltaGlobalInfo UpdateGlobalInfo(DeltaGlobalInfo existing, DeltaExportResult exportResult)
    {
        return existing with
        {
            TotalExports = existing.TotalExports + 1,
            TotalRowsExported = existing.TotalRowsExported + exportResult.RowsExported,
            FirstExportTime = existing.FirstExportTime ?? DateTime.UtcNow
        };
    }
    
    private async Task<Dictionary<string, DeltaPartitionTableInfo>> LoadPartitionsManifestAsync(
        string partitionsPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(partitionsPath))
        {
            return new Dictionary<string, DeltaPartitionTableInfo>();
        }
        
        try
        {
            var json = await File.ReadAllTextAsync(partitionsPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<Dictionary<string, DeltaPartitionTableInfo>>(json, _jsonOptions);
            return manifest ?? new Dictionary<string, DeltaPartitionTableInfo>();
        }
        catch
        {
            // Return empty manifest if deserialization fails
            return new Dictionary<string, DeltaPartitionTableInfo>();
        }
    }
    
    private async Task SavePartitionsManifestAsync(
        string partitionsPath,
        Dictionary<string, DeltaPartitionTableInfo> partitionsManifest,
        CancellationToken cancellationToken)
    {
        var manifestDir = Path.GetDirectoryName(partitionsPath);
        if (!string.IsNullOrEmpty(manifestDir))
        {
            Directory.CreateDirectory(manifestDir);
        }
        
        var json = JsonSerializer.Serialize(partitionsManifest, _jsonOptions);
        await File.WriteAllTextAsync(partitionsPath, json, cancellationToken);
    }
    
    private static async Task<string> ComputeFileChecksumAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return string.Empty;
        }
        
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToBase64String(hash);
    }
    
    private static string? GetFirstPrimaryKey(DeltaExportResult exportResult)
    {
        // This could be enhanced to extract actual first PK from export metadata
        return exportResult.NewCheckpoint?.LastPrimaryKeyValue?.ToString();
    }
    
    private static string? GetLastPrimaryKey(DeltaExportResult exportResult)
    {
        return exportResult.NewCheckpoint?.LastPrimaryKeyValue?.ToString();
    }
    
    private static string GetDeltaManifestPath(string bundleDirectory)
    {
        return Path.Combine(bundleDirectory, "manifest", "delta.json");
    }
    
    private static string GetPartitionsManifestPath(string bundleDirectory)
    {
        return Path.Combine(bundleDirectory, "manifest", "partitions.json");
    }
}

/// <summary>
/// Partition table information for partitions.json.
/// </summary>
internal sealed record DeltaPartitionTableInfo
{
    public required string Strategy { get; init; }
    public IReadOnlyList<DeltaPartitionInfo> Parts { get; init; } = Array.Empty<DeltaPartitionInfo>();
}

/// <summary>
/// Information about a single partition.
/// </summary>
internal sealed record DeltaPartitionInfo
{
    public required string Path { get; init; }
    public long Rows { get; init; }
    public required string Sha256 { get; init; }
    public string? Label { get; init; }
    public long FileSizeBytes { get; init; }
    public DateTime ExportTimestamp { get; init; }
    public string? FirstPk { get; init; }
    public string? LastPk { get; init; }
}