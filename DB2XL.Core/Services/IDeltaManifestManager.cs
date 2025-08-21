using DB2XL.Core.Models;

namespace DB2XL.Core.Services;

/// <summary>
/// Service for managing delta export manifests and checkpoints.
/// Handles persistence and retrieval of delta.json and updates to partitions.json.
/// </summary>
public interface IDeltaManifestManager
{
    /// <summary>
    /// Loads existing delta manifest from the bundle directory.
    /// </summary>
    /// <param name="bundleDirectory">Bundle root directory</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Existing delta manifest or empty manifest if not found</returns>
    Task<DeltaManifest> LoadDeltaManifestAsync(
        string bundleDirectory,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Saves delta manifest to the bundle directory.
    /// </summary>
    /// <param name="bundleDirectory">Bundle root directory</param>
    /// <param name="deltaManifest">Delta manifest to save</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveDeltaManifestAsync(
        string bundleDirectory,
        DeltaManifest deltaManifest,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Updates delta manifest with new checkpoint information after successful export.
    /// </summary>
    /// <param name="bundleDirectory">Bundle root directory</param>
    /// <param name="tableName">Table that was exported</param>
    /// <param name="exportResult">Results from the delta export</param>
    /// <param name="deltaMode">Type of delta export used</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateDeltaManifestAsync(
        string bundleDirectory,
        string tableName,
        DeltaExportResult exportResult,
        DeltaExportMode deltaMode,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Appends new partition information to partitions.json after delta export.
    /// </summary>
    /// <param name="bundleDirectory">Bundle root directory</param>
    /// <param name="tableName">Table name</param>
    /// <param name="exportResult">Export result with file information</param>
    /// <param name="partitionLabel">Label for the delta partition</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AppendPartitionInfoAsync(
        string bundleDirectory,
        string tableName,
        DeltaExportResult exportResult,
        string partitionLabel,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the latest checkpoint for a table and selection hash.
    /// </summary>
    /// <param name="bundleDirectory">Bundle root directory</param>
    /// <param name="tableName">Table name</param>
    /// <param name="selectionHash">Selection criteria hash</param>
    /// <param name="deltaMode">Delta export mode</param>
    /// <returns>Latest checkpoint or null if not found</returns>
    Task<DeltaCheckpoint?> GetLatestCheckpointAsync(
        string bundleDirectory,
        string tableName,
        string selectionHash,
        DeltaExportMode deltaMode);
    
    /// <summary>
    /// Creates a backup of the current delta manifest before making changes.
    /// </summary>
    /// <param name="bundleDirectory">Bundle root directory</param>
    /// <param name="backupSuffix">Suffix for backup file (e.g., timestamp)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task BackupDeltaManifestAsync(
        string bundleDirectory,
        string backupSuffix,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validates delta manifest consistency and integrity.
    /// </summary>
    /// <param name="bundleDirectory">Bundle root directory</param>
    /// <returns>Validation result</returns>
    Task<DeltaManifestValidationResult> ValidateDeltaManifestAsync(string bundleDirectory);
}

/// <summary>
/// Represents the delta.json manifest structure.
/// </summary>
public sealed record DeltaManifest
{
    /// <summary>Format version for compatibility.</summary>
    public string Version { get; init; } = "1.0";
    
    /// <summary>When this manifest was last updated.</summary>
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
    
    /// <summary>Checkpoints by table name and selection hash.</summary>
    public Dictionary<string, Dictionary<string, TableDeltaInfo>> Tables { get; init; } = new();
    
    /// <summary>Global export metadata.</summary>
    public DeltaGlobalInfo GlobalInfo { get; init; } = new();
}

/// <summary>
/// Delta information for a specific table and selection criteria.
/// </summary>
public sealed record TableDeltaInfo
{
    /// <summary>Last watermark checkpoint for this selection.</summary>
    public DeltaCheckpoint? WatermarkCheckpoint { get; init; }
    
    /// <summary>Last change log checkpoint for this selection.</summary>
    public DeltaCheckpoint? ChangeLogCheckpoint { get; init; }
    
    /// <summary>Selection criteria hash for validation.</summary>
    public required string SelectionHash { get; init; }
    
    /// <summary>Export statistics.</summary>
    public DeltaExportStats Stats { get; init; } = new();
    
    /// <summary>Last export timestamp.</summary>
    public DateTime LastExportTime { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Global delta export information.
/// </summary>
public sealed record DeltaGlobalInfo
{
    /// <summary>Total number of delta exports performed.</summary>
    public long TotalExports { get; init; }
    
    /// <summary>Total rows exported across all deltas.</summary>
    public long TotalRowsExported { get; init; }
    
    /// <summary>First export timestamp.</summary>
    public DateTime? FirstExportTime { get; init; }
    
    /// <summary>Export tool version.</summary>
    public string ToolVersion { get; init; } = "1.0.0";
    
    /// <summary>Supported delta modes.</summary>
    public IReadOnlyList<DeltaExportMode> SupportedModes { get; init; } = 
        new[] { DeltaExportMode.Watermark, DeltaExportMode.ChangeLog };
}

/// <summary>
/// Statistics for delta exports of a table.
/// </summary>
public sealed record DeltaExportStats
{
    /// <summary>Number of exports performed.</summary>
    public int ExportCount { get; init; }
    
    /// <summary>Total rows exported.</summary>
    public long TotalRowsExported { get; init; }
    
    /// <summary>Average rows per export.</summary>
    public double AverageRowsPerExport => ExportCount > 0 ? (double)TotalRowsExported / ExportCount : 0;
    
    /// <summary>Last export duration.</summary>
    public TimeSpan? LastExportDuration { get; init; }
    
    /// <summary>Average export duration.</summary>
    public TimeSpan? AverageExportDuration { get; init; }
}

/// <summary>
/// Types of delta export modes.
/// </summary>
public enum DeltaExportMode
{
    /// <summary>Watermark-based delta using timestamp/version columns.</summary>
    Watermark,
    
    /// <summary>Change log-based delta using trigger tracking.</summary>
    ChangeLog
}

/// <summary>
/// Result of delta manifest validation.
/// </summary>
public sealed record DeltaManifestValidationResult
{
    /// <summary>Whether the manifest is valid.</summary>
    public bool IsValid { get; init; }
    
    /// <summary>Number of tables with checkpoints.</summary>
    public int TableCount { get; init; }
    
    /// <summary>Total checkpoints across all tables.</summary>
    public int TotalCheckpoints { get; init; }
    
    /// <summary>Validation errors found.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    
    /// <summary>Warnings about potential issues.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    
    /// <summary>Informational suggestions.</summary>
    public IReadOnlyList<string> Suggestions { get; init; } = Array.Empty<string>();
}