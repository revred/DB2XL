using Microsoft.Data.Sqlite;
using DB2XL.Query;

namespace DB2XL.DeltaExport;

/// <summary>
/// Main service for delta exports - coordinates different strategies
/// </summary>
public sealed class DeltaExportService : IDeltaExportService
{
    private readonly IDeltaCheckpointService _checkpointService;
    private readonly IWatermarkDeltaService _watermarkService;
    private readonly IChangeLogDeltaService _changeLogService;
    private readonly IPrimaryKeyDiscoveryService _primaryKeyService;
    
    public DeltaExportService(
        IDeltaCheckpointService? checkpointService = null,
        IWatermarkDeltaService? watermarkService = null,
        IChangeLogDeltaService? changeLogService = null,
        IPrimaryKeyDiscoveryService? primaryKeyService = null)
    {
        _checkpointService = checkpointService ?? new FileDeltaCheckpointService();
        _watermarkService = watermarkService ?? new WatermarkDeltaService();
        _changeLogService = changeLogService ?? new ChangeLogDeltaService();
        _primaryKeyService = primaryKeyService ?? new PrimaryKeyDiscoveryService();
    }
    
    public async Task<DeltaExportResult> ExecuteDeltaExportAsync(
        SqliteConnection connection, 
        string tableName, 
        DeltaExportConfig config)
    {
        // Get the latest checkpoint for this table and strategy
        var checkpoint = await _checkpointService.GetLatestCheckpointAsync(tableName, config.Strategy);
        
        // Execute delta export based on strategy
        var result = config.Strategy switch
        {
            DeltaStrategy.Watermark => await _watermarkService.ExecuteDeltaExportAsync(
                connection, tableName, config, checkpoint),
            DeltaStrategy.ChangeLog => await _changeLogService.ExecuteDeltaExportAsync(
                connection, tableName, config, checkpoint),
            DeltaStrategy.Full => await ExecuteFullExportAsync(connection, tableName, config),
            _ => throw new ArgumentException($"Unsupported delta strategy: {config.Strategy}")
        };
        
        // Save the new checkpoint
        await _checkpointService.SaveCheckpointAsync(result.Checkpoint);
        
        return result;
    }
    
    public async Task<(DeltaStrategy strategy, DeltaExportConfig config)> RecommendDeltaStrategyAsync(
        SqliteConnection connection, 
        string tableName)
    {
        // Analyze table structure to recommend the best strategy
        var columns = _primaryKeyService.GetColumns(connection, tableName);
        var watermarkCandidates = _watermarkService.DiscoverWatermarkColumns(connection, tableName);
        
        // Check if change tracking is already installed
        var defaultChangeLogConfig = new ChangeLogConfig();
        var hasChangeTracking = _changeLogService.IsChangeTrackingInstalled(
            connection, tableName, defaultChangeLogConfig);
        
        // Recommendation logic:
        // 1. If change tracking is already installed, prefer ChangeLog
        // 2. If good watermark columns exist, prefer Watermark
        // 3. Otherwise, recommend ChangeLog for future tracking
        
        if (hasChangeTracking)
        {
            return (DeltaStrategy.ChangeLog, new DeltaExportConfig
            {
                Strategy = DeltaStrategy.ChangeLog,
                ChangeLogConfig = defaultChangeLogConfig,
                IncludeDeletes = true
            });
        }
        
        if (watermarkCandidates.Count > 0)
        {
            // Validate the top watermark candidate
            var topCandidate = watermarkCandidates.Take(1).ToArray();
            var validation = _watermarkService.ValidateWatermarkColumns(connection, tableName, topCandidate);
            
            if (validation.IsValid)
            {
                return (DeltaStrategy.Watermark, new DeltaExportConfig
                {
                    Strategy = DeltaStrategy.Watermark,
                    WatermarkColumns = topCandidate
                });
            }
        }
        
        // Default recommendation: ChangeLog with auto-install
        return (DeltaStrategy.ChangeLog, new DeltaExportConfig
        {
            Strategy = DeltaStrategy.ChangeLog,
            ChangeLogConfig = new ChangeLogConfig
            {
                AutoInstallTriggers = true,
                CaptureFullRowData = false, // More efficient
                RetentionDays = 30
            },
            IncludeDeletes = true
        });
    }
    
    public async Task<IReadOnlyList<string>> GetTrackedTablesAsync()
    {
        // This would require scanning all checkpoint files
        // For now, we'll implement a simpler version that works with the file-based service
        
        if (_checkpointService is FileDeltaCheckpointService fileService)
        {
            // Extract table names from checkpoint files
            var checkpointDir = GetCheckpointDirectory(fileService);
            if (!Directory.Exists(checkpointDir))
            {
                return Array.Empty<string>();
            }
            
            var files = Directory.GetFiles(checkpointDir, "*.json");
            var tableNames = new HashSet<string>();
            
            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var parts = fileName.Split('_');
                if (parts.Length >= 2)
                {
                    // First part should be the table name
                    tableNames.Add(parts[0]);
                }
            }
            
            return tableNames.ToArray();
        }
        
        // For in-memory service, we'd need to add a method to get all unique table names
        return Array.Empty<string>();
    }
    
    public async Task<bool> ResetDeltaTrackingAsync(string tableName)
    {
        try
        {
            var checkpoints = await _checkpointService.GetCheckpointsAsync(tableName);
            
            // For file-based service, we need to delete the files
            if (_checkpointService is FileDeltaCheckpointService fileService)
            {
                var checkpointDir = GetCheckpointDirectory(fileService);
                if (Directory.Exists(checkpointDir))
                {
                    var pattern = $"{SanitizeFileName(tableName)}_*.json";
                    var files = Directory.GetFiles(checkpointDir, pattern);
                    
                    foreach (var file in files)
                    {
                        File.Delete(file);
                    }
                }
            }
            
            // For in-memory service, we'd need to add a method to remove checkpoints
            if (_checkpointService is InMemoryDeltaCheckpointService memoryService)
            {
                // This would require adding a method to the in-memory service
                // For now, we'll return false to indicate it's not supported
                return false;
            }
            
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    private async Task<DeltaExportResult> ExecuteFullExportAsync(
        SqliteConnection connection, 
        string tableName, 
        DeltaExportConfig config)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Build a full table query
        var selectionGrammar = new SelectionGrammar
        {
            Table = tableName,
            Select = config.AdditionalFilter?.Select ?? new[] { "*" },
            Where = config.AdditionalFilter?.Where,
            OrderBy = config.CustomOrdering ?? Array.Empty<IOrderByClause>(),
            Limit = config.MaxRows
        };
        
        var sqlBuilder = new SqlBuilder();
        var query = sqlBuilder.BuildQuery(selectionGrammar);
        
        var queryExecutor = new DeltaQueryExecutor();
        var (rows, totalCount, hasMore) = await queryExecutor.ExecuteDeltaQueryAsync(
            connection, query, config.MaxRows);
        
        var rowsExported = rows.Count();
        
        var checkpoint = new DeltaCheckpoint
        {
            CheckpointId = Guid.NewGuid().ToString("N")[..8],
            TableName = tableName,
            Strategy = DeltaStrategy.Full,
            CreatedAt = DateTime.UtcNow,
            RowsProcessed = rowsExported,
            Metadata = new Dictionary<string, object>
            {
                ["fullExport"] = true,
                ["totalRowsInQuery"] = totalCount,
                ["executionTimeMs"] = stopwatch.ElapsedMilliseconds
            }
        };
        
        stopwatch.Stop();
        
        return new DeltaExportResult
        {
            Checkpoint = checkpoint,
            RowsExported = rowsExported,
            HasMoreData = hasMore,
            ElapsedTime = stopwatch.Elapsed,
            ExecutedQuery = query.Sql,
            QueryParameters = query.Parameters
        };
    }
    
    private static string GetCheckpointDirectory(FileDeltaCheckpointService fileService)
    {
        // Use reflection to access the private field (not ideal, but works for this case)
        var field = typeof(FileDeltaCheckpointService).GetField("_checkpointDirectory", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(fileService)?.ToString() ?? string.Empty;
    }
    
    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>
/// Builder for creating delta export configurations
/// </summary>
public sealed class DeltaExportConfigBuilder
{
    private DeltaStrategy _strategy = DeltaStrategy.Watermark;
    private readonly List<string> _watermarkColumns = new();
    private ISelectionGrammar? _additionalFilter;
    private int? _maxRows;
    private bool _includeDeletes = true;
    private readonly List<IOrderByClause> _customOrdering = new();
    private ChangeLogConfig? _changeLogConfig;
    
    public static DeltaExportConfigBuilder Create() => new();
    
    public DeltaExportConfigBuilder WithStrategy(DeltaStrategy strategy)
    {
        _strategy = strategy;
        return this;
    }
    
    public DeltaExportConfigBuilder WithWatermarkColumns(params string[] columns)
    {
        _watermarkColumns.Clear();
        _watermarkColumns.AddRange(columns);
        return this;
    }
    
    public DeltaExportConfigBuilder WithAdditionalFilter(ISelectionGrammar filter)
    {
        _additionalFilter = filter;
        return this;
    }
    
    public DeltaExportConfigBuilder WithMaxRows(int maxRows)
    {
        _maxRows = maxRows;
        return this;
    }
    
    public DeltaExportConfigBuilder IncludeDeletes(bool include = true)
    {
        _includeDeletes = include;
        return this;
    }
    
    public DeltaExportConfigBuilder WithCustomOrdering(params IOrderByClause[] ordering)
    {
        _customOrdering.Clear();
        _customOrdering.AddRange(ordering);
        return this;
    }
    
    public DeltaExportConfigBuilder WithChangeLogConfig(ChangeLogConfig config)
    {
        _changeLogConfig = config;
        return this;
    }
    
    public DeltaExportConfig Build()
    {
        return new DeltaExportConfig
        {
            Strategy = _strategy,
            WatermarkColumns = _watermarkColumns.ToArray(),
            AdditionalFilter = _additionalFilter,
            MaxRows = _maxRows,
            IncludeDeletes = _includeDeletes,
            CustomOrdering = _customOrdering.ToArray(),
            ChangeLogConfig = _changeLogConfig
        };
    }
}

/// <summary>
/// Statistics and information about delta export operations
/// </summary>
public sealed record DeltaExportStats
{
    public string TableName { get; init; } = string.Empty;
    public DeltaStrategy Strategy { get; init; }
    public int CheckpointCount { get; init; }
    public DateTime? FirstExport { get; init; }
    public DateTime? LastExport { get; init; }
    public long TotalRowsProcessed { get; init; }
    public TimeSpan TotalProcessingTime { get; init; }
    public double AverageRowsPerSecond { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Service for analyzing delta export performance and statistics
/// </summary>
public static class DeltaExportAnalyzer
{
    /// <summary>
    /// Analyzes delta export performance for a table
    /// </summary>
    public static async Task<DeltaExportStats> AnalyzeTableAsync(
        IDeltaCheckpointService checkpointService, 
        string tableName)
    {
        var checkpoints = await checkpointService.GetCheckpointsAsync(tableName);
        
        if (checkpoints.Count == 0)
        {
            return new DeltaExportStats
            {
                TableName = tableName,
                Strategy = DeltaStrategy.Full
            };
        }
        
        var firstCheckpoint = checkpoints.First();
        var lastCheckpoint = checkpoints.Last();
        var totalRows = checkpoints.Sum(c => c.RowsProcessed);
        
        var executionTimes = checkpoints
            .Where(c => c.Metadata.ContainsKey("executionTimeMs"))
            .Select(c => TimeSpan.FromMilliseconds(Convert.ToDouble(c.Metadata["executionTimeMs"])))
            .ToList();
        
        var totalTime = executionTimes.Count > 0 
            ? TimeSpan.FromTicks(executionTimes.Sum(t => t.Ticks))
            : TimeSpan.Zero;
        
        var averageRowsPerSecond = totalTime.TotalSeconds > 0 
            ? totalRows / totalTime.TotalSeconds 
            : 0;
        
        return new DeltaExportStats
        {
            TableName = tableName,
            Strategy = firstCheckpoint.Strategy,
            CheckpointCount = checkpoints.Count,
            FirstExport = firstCheckpoint.CreatedAt,
            LastExport = lastCheckpoint.CreatedAt,
            TotalRowsProcessed = totalRows,
            TotalProcessingTime = totalTime,
            AverageRowsPerSecond = averageRowsPerSecond,
            Metadata = new Dictionary<string, object>
            {
                ["latestCheckpointId"] = lastCheckpoint.CheckpointId,
                ["strategySwitches"] = checkpoints.GroupBy(c => c.Strategy).Count() - 1
            }
        };
    }
    
    /// <summary>
    /// Recommends optimizations for delta export configuration
    /// </summary>
    public static IReadOnlyList<string> RecommendOptimizations(DeltaExportStats stats)
    {
        var recommendations = new List<string>();
        
        if (stats.AverageRowsPerSecond < 1000)
        {
            recommendations.Add("Consider adding indexes to watermark columns to improve query performance");
        }
        
        if (stats.CheckpointCount > 100)
        {
            recommendations.Add("Consider implementing checkpoint cleanup to remove old entries");
        }
        
        if (stats.Strategy == DeltaStrategy.Full)
        {
            recommendations.Add("Consider switching to watermark or change log strategy for better performance");
        }
        
        return recommendations;
    }
}