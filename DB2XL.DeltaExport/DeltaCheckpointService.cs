using System.Text.Json;

namespace DB2XL.DeltaExport;

/// <summary>
/// File-based implementation of delta checkpoint persistence
/// Stores checkpoints as JSON files in a configurable directory
/// </summary>
public sealed class FileDeltaCheckpointService : IDeltaCheckpointService
{
    private readonly string _checkpointDirectory;
    private readonly JsonSerializerOptions _jsonOptions;
    
    public FileDeltaCheckpointService(string? checkpointDirectory = null)
    {
        _checkpointDirectory = checkpointDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DB2XL", "Checkpoints");
        
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        
        EnsureDirectoryExists();
    }
    
    public async Task SaveCheckpointAsync(DeltaCheckpoint checkpoint)
    {
        var fileName = GetCheckpointFileName(checkpoint.TableName, checkpoint.Strategy, checkpoint.CheckpointId);
        var filePath = Path.Combine(_checkpointDirectory, fileName);
        
        var json = JsonSerializer.Serialize(checkpoint, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }
    
    public async Task<DeltaCheckpoint?> GetLatestCheckpointAsync(string tableName, DeltaStrategy strategy)
    {
        var checkpoints = await GetCheckpointsAsync(tableName);
        return checkpoints
            .Where(c => c.Strategy == strategy)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault();
    }
    
    public async Task<IReadOnlyList<DeltaCheckpoint>> GetCheckpointsAsync(string tableName)
    {
        if (!Directory.Exists(_checkpointDirectory))
        {
            return Array.Empty<DeltaCheckpoint>();
        }
        
        var pattern = GetCheckpointFilePattern(tableName);
        var files = Directory.GetFiles(_checkpointDirectory, pattern);
        
        var checkpoints = new List<DeltaCheckpoint>();
        
        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var checkpoint = JsonSerializer.Deserialize<DeltaCheckpoint>(json, _jsonOptions);
                if (checkpoint != null)
                {
                    checkpoints.Add(checkpoint);
                }
            }
            catch (JsonException)
            {
                // Skip corrupted checkpoint files
                continue;
            }
        }
        
        return checkpoints.OrderBy(c => c.CreatedAt).ToArray();
    }
    
    public async Task<int> CleanupOldCheckpointsAsync(int retentionDays)
    {
        if (!Directory.Exists(_checkpointDirectory))
        {
            return 0;
        }
        
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
        var files = Directory.GetFiles(_checkpointDirectory, "*.json");
        var deletedCount = 0;
        
        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var checkpoint = JsonSerializer.Deserialize<DeltaCheckpoint>(json, _jsonOptions);
                
                if (checkpoint?.CreatedAt < cutoffDate)
                {
                    File.Delete(file);
                    deletedCount++;
                }
            }
            catch (JsonException)
            {
                // Delete corrupted files as well
                File.Delete(file);
                deletedCount++;
            }
        }
        
        return deletedCount;
    }
    
    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_checkpointDirectory))
        {
            Directory.CreateDirectory(_checkpointDirectory);
        }
    }
    
    private static string GetCheckpointFileName(string tableName, DeltaStrategy strategy, string checkpointId)
    {
        var safeTableName = SanitizeFileName(tableName);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        return $"{safeTableName}_{strategy}_{timestamp}_{checkpointId}.json";
    }
    
    private static string GetCheckpointFilePattern(string tableName)
    {
        var safeTableName = SanitizeFileName(tableName);
        return $"{safeTableName}_*.json";
    }
    
    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }
}

/// <summary>
/// In-memory implementation of delta checkpoint service for testing
/// </summary>
public sealed class InMemoryDeltaCheckpointService : IDeltaCheckpointService
{
    private readonly List<DeltaCheckpoint> _checkpoints = new();
    private readonly object _lock = new();
    
    public Task SaveCheckpointAsync(DeltaCheckpoint checkpoint)
    {
        lock (_lock)
        {
            _checkpoints.Add(checkpoint);
        }
        return Task.CompletedTask;
    }
    
    public Task<DeltaCheckpoint?> GetLatestCheckpointAsync(string tableName, DeltaStrategy strategy)
    {
        lock (_lock)
        {
            var latest = _checkpoints
                .Where(c => c.TableName == tableName && c.Strategy == strategy)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefault();
            
            return Task.FromResult(latest);
        }
    }
    
    public Task<IReadOnlyList<DeltaCheckpoint>> GetCheckpointsAsync(string tableName)
    {
        lock (_lock)
        {
            var checkpoints = _checkpoints
                .Where(c => c.TableName == tableName)
                .OrderBy(c => c.CreatedAt)
                .ToArray();
            
            return Task.FromResult<IReadOnlyList<DeltaCheckpoint>>(checkpoints);
        }
    }
    
    public Task<int> CleanupOldCheckpointsAsync(int retentionDays)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
        
        lock (_lock)
        {
            var toRemove = _checkpoints.Where(c => c.CreatedAt < cutoffDate).ToList();
            foreach (var checkpoint in toRemove)
            {
                _checkpoints.Remove(checkpoint);
            }
            return Task.FromResult(toRemove.Count);
        }
    }
    
    /// <summary>
    /// Test helper: Clear all checkpoints
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _checkpoints.Clear();
        }
    }
    
    /// <summary>
    /// Test helper: Get all checkpoints
    /// </summary>
    public IReadOnlyList<DeltaCheckpoint> GetAllCheckpoints()
    {
        lock (_lock)
        {
            return _checkpoints.ToArray();
        }
    }
}