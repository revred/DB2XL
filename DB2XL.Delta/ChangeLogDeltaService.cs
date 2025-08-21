using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Text;
using DB2XL.Query;
using DB2XL.Core.Models;

namespace DB2XL.DeltaExport;

/// <summary>
/// Implementation of change log-based delta exports using SQLite triggers
/// Tracks INSERT, UPDATE, and DELETE operations in a dedicated change log table
/// </summary>
public sealed class ChangeLogDeltaService : IChangeLogDeltaService
{
    private readonly IDeltaQueryExecutor _queryExecutor;
    private readonly IPrimaryKeyDiscoveryService _primaryKeyService;
    
    public ChangeLogDeltaService(
        IDeltaQueryExecutor? queryExecutor = null,
        IPrimaryKeyDiscoveryService? primaryKeyService = null)
    {
        _queryExecutor = queryExecutor ?? new DeltaQueryExecutor();
        _primaryKeyService = primaryKeyService ?? new PrimaryKeyDiscoveryService();
    }
    
    public async Task<bool> InstallChangeTrackingAsync(
        SqliteConnection connection, 
        string tableName, 
        ChangeLogConfig config)
    {
        try
        {
            // Ensure change log table exists
            await EnsureChangeLogTableAsync(connection, config);
            
            // Get primary key information for the table
            var primaryKey = _primaryKeyService.DiscoverPrimaryKey(connection, tableName);
            
            // Install triggers for INSERT, UPDATE, DELETE
            await InstallInsertTriggerAsync(connection, tableName, config, primaryKey);
            await InstallUpdateTriggerAsync(connection, tableName, config, primaryKey);
            await InstallDeleteTriggerAsync(connection, tableName, config, primaryKey);
            
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }
    
    public async Task<bool> RemoveChangeTrackingAsync(
        SqliteConnection connection, 
        string tableName, 
        ChangeLogConfig config)
    {
        try
        {
            var triggerNames = new[]
            {
                GetTriggerName(tableName, "insert"),
                GetTriggerName(tableName, "update"),
                GetTriggerName(tableName, "delete")
            };
            
            foreach (var triggerName in triggerNames)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"DROP TRIGGER IF EXISTS \"{triggerName.Replace("\"", "\"\"")}\"";
                await cmd.ExecuteNonQueryAsync();
            }
            
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }
    
    public async Task<DeltaExportResult> ExecuteDeltaExportAsync(
        SqliteConnection connection, 
        string tableName, 
        DeltaExportConfig config, 
        DeltaCheckpoint? checkpoint = null)
    {
        var stopwatch = Stopwatch.StartNew();
        
        if (config.ChangeLogConfig == null)
        {
            throw new ArgumentException("ChangeLogConfig is required for change log delta exports", nameof(config));
        }
        
        // Ensure change tracking is installed
        if (config.ChangeLogConfig.AutoInstallTriggers && 
            !IsChangeTrackingInstalled(connection, tableName, config.ChangeLogConfig))
        {
            await InstallChangeTrackingAsync(connection, tableName, config.ChangeLogConfig);
        }
        
        var lastChangeLogId = checkpoint?.LastChangeLogId ?? 0;
        
        // Build and execute change log query
        var query = DeltaQueryBuilder.BuildChangeLogQuery(
            tableName,
            config.ChangeLogConfig.ChangeLogTableName,
            lastChangeLogId,
            config.IncludeDeletes,
            config.AdditionalFilter,
            config.MaxRows);
        
        var (rows, totalCount, hasMore) = await _queryExecutor.ExecuteDeltaQueryAsync(
            connection, query, config.MaxRows);
        
        var rowList = rows.ToList();
        var rowsExported = rowList.Count;
        
        // Find the highest change_id from exported rows
        var newLastChangeLogId = lastChangeLogId;
        if (rowsExported > 0)
        {
            var maxChangeId = rowList
                .Where(row => row.ContainsKey("change_id"))
                .Select(row => Convert.ToInt64(row["change_id"]))
                .DefaultIfEmpty(lastChangeLogId)
                .Max();
            
            newLastChangeLogId = maxChangeId;
        }
        
        // Create new checkpoint
        var newCheckpoint = new DeltaCheckpoint
        {
            CheckpointId = Guid.NewGuid().ToString("N")[..8],
            TableName = tableName,
            Strategy = DeltaStrategy.ChangeLog,
            CreatedAt = DateTime.UtcNow,
            LastChangeLogId = newLastChangeLogId,
            RowsProcessed = (checkpoint?.RowsProcessed ?? 0) + rowsExported,
            Metadata = new Dictionary<string, object>
            {
                ["changeLogTable"] = config.ChangeLogConfig.ChangeLogTableName,
                ["includeDeletes"] = config.IncludeDeletes,
                ["captureFullRowData"] = config.ChangeLogConfig.CaptureFullRowData,
                ["lastChangeLogId"] = newLastChangeLogId,
                ["totalRowsInQuery"] = totalCount,
                ["executionTimeMs"] = stopwatch.ElapsedMilliseconds
            }
        };
        
        stopwatch.Stop();
        
        return new DeltaExportResult
        {
            Checkpoint = newCheckpoint,
            RowsExported = rowsExported,
            HasMoreData = hasMore,
            ElapsedTime = stopwatch.Elapsed,
            ExecutedQuery = query.Sql,
            QueryParameters = query.Parameters
        };
    }
    
    public async Task<int> CleanupChangeLogAsync(
        SqliteConnection connection, 
        ChangeLogConfig config, 
        int retentionDays)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
        var quotedTableName = $"\"{config.ChangeLogTableName.Replace("\"", "\"\"")}\"";
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            DELETE FROM {quotedTableName} 
            WHERE changed_at < @cutoffDate";
        cmd.Parameters.AddWithValue("@cutoffDate", cutoffDate.ToString("yyyy-MM-dd HH:mm:ss"));
        
        return await cmd.ExecuteNonQueryAsync();
    }
    
    public bool IsChangeTrackingInstalled(
        SqliteConnection connection, 
        string tableName, 
        ChangeLogConfig config)
    {
        var triggerNames = new[]
        {
            GetTriggerName(tableName, "insert"),
            GetTriggerName(tableName, "update"),
            GetTriggerName(tableName, "delete")
        };
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) 
            FROM sqlite_master 
            WHERE type = 'trigger' 
              AND name IN (@trigger1, @trigger2, @trigger3)";
        
        cmd.Parameters.AddWithValue("@trigger1", triggerNames[0]);
        cmd.Parameters.AddWithValue("@trigger2", triggerNames[1]);
        cmd.Parameters.AddWithValue("@trigger3", triggerNames[2]);
        
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        return count == 3; // All three triggers must be present
    }
    
    private async Task EnsureChangeLogTableAsync(SqliteConnection connection, ChangeLogConfig config)
    {
        var quotedTableName = $"\"{config.ChangeLogTableName.Replace("\"", "\"\"")}\"";
        
        var createTableSql = $@"
            CREATE TABLE IF NOT EXISTS {quotedTableName} (
                change_id INTEGER PRIMARY KEY AUTOINCREMENT,
                table_name TEXT NOT NULL,
                operation TEXT NOT NULL CHECK (operation IN ('INSERT', 'UPDATE', 'DELETE')),
                row_data TEXT NULL,
                changed_at TEXT NOT NULL DEFAULT (datetime('now', 'utc')),
                primary_key_values TEXT NOT NULL
            )";
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = createTableSql;
        await cmd.ExecuteNonQueryAsync();
        
        // Create indexes for performance
        var indexSqls = new[]
        {
            $"CREATE INDEX IF NOT EXISTS idx_{config.ChangeLogTableName}_table_operation ON {quotedTableName}(table_name, operation)",
            $"CREATE INDEX IF NOT EXISTS idx_{config.ChangeLogTableName}_changed_at ON {quotedTableName}(changed_at)",
            $"CREATE INDEX IF NOT EXISTS idx_{config.ChangeLogTableName}_table_changeid ON {quotedTableName}(table_name, change_id)"
        };
        
        foreach (var indexSql in indexSqls)
        {
            cmd.CommandText = indexSql;
            await cmd.ExecuteNonQueryAsync();
        }
    }
    
    private async Task InstallInsertTriggerAsync(
        SqliteConnection connection, 
        string tableName, 
        ChangeLogConfig config, 
        PrimaryKeyInfo primaryKey)
    {
        var triggerName = GetTriggerName(tableName, "insert");
        var quotedTableName = $"\"{tableName.Replace("\"", "\"\"")}\"";
        var quotedTriggerName = $"\"{triggerName.Replace("\"", "\"\"")}\"";
        var quotedChangeLogTable = $"\"{config.ChangeLogTableName.Replace("\"", "\"\"")}\"";
        
        var primaryKeyJson = BuildPrimaryKeyJson(primaryKey);
        var rowDataClause = config.CaptureFullRowData ? BuildRowDataJson("NEW", connection, tableName) : "NULL";
        
        var triggerSql = $@"
            CREATE TRIGGER IF NOT EXISTS {quotedTriggerName}
            AFTER INSERT ON {quotedTableName}
            BEGIN
                INSERT INTO {quotedChangeLogTable} (table_name, operation, row_data, primary_key_values)
                VALUES ('{tableName}', 'INSERT', {rowDataClause}, {primaryKeyJson});
            END";
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = triggerSql;
        await cmd.ExecuteNonQueryAsync();
    }
    
    private async Task InstallUpdateTriggerAsync(
        SqliteConnection connection, 
        string tableName, 
        ChangeLogConfig config, 
        PrimaryKeyInfo primaryKey)
    {
        var triggerName = GetTriggerName(tableName, "update");
        var quotedTableName = $"\"{tableName.Replace("\"", "\"\"")}\"";
        var quotedTriggerName = $"\"{triggerName.Replace("\"", "\"\"")}\"";
        var quotedChangeLogTable = $"\"{config.ChangeLogTableName.Replace("\"", "\"\"")}\"";
        
        var primaryKeyJson = BuildPrimaryKeyJson(primaryKey);
        var rowDataClause = config.CaptureFullRowData ? BuildRowDataJson("NEW", connection, tableName) : "NULL";
        
        var triggerSql = $@"
            CREATE TRIGGER IF NOT EXISTS {quotedTriggerName}
            AFTER UPDATE ON {quotedTableName}
            BEGIN
                INSERT INTO {quotedChangeLogTable} (table_name, operation, row_data, primary_key_values)
                VALUES ('{tableName}', 'UPDATE', {rowDataClause}, {primaryKeyJson});
            END";
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = triggerSql;
        await cmd.ExecuteNonQueryAsync();
    }
    
    private async Task InstallDeleteTriggerAsync(
        SqliteConnection connection, 
        string tableName, 
        ChangeLogConfig config, 
        PrimaryKeyInfo primaryKey)
    {
        var triggerName = GetTriggerName(tableName, "delete");
        var quotedTableName = $"\"{tableName.Replace("\"", "\"\"")}\"";
        var quotedTriggerName = $"\"{triggerName.Replace("\"", "\"\"")}\"";
        var quotedChangeLogTable = $"\"{config.ChangeLogTableName.Replace("\"", "\"\"")}\"";
        
        var primaryKeyJson = BuildPrimaryKeyJson(primaryKey, "OLD");
        var rowDataClause = config.CaptureFullRowData ? BuildRowDataJson("OLD", connection, tableName) : "NULL";
        
        var triggerSql = $@"
            CREATE TRIGGER IF NOT EXISTS {quotedTriggerName}
            BEFORE DELETE ON {quotedTableName}
            BEGIN
                INSERT INTO {quotedChangeLogTable} (table_name, operation, row_data, primary_key_values)
                VALUES ('{tableName}', 'DELETE', {rowDataClause}, {primaryKeyJson});
            END";
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = triggerSql;
        await cmd.ExecuteNonQueryAsync();
    }
    
    private string BuildPrimaryKeyJson(PrimaryKeyInfo primaryKey, string rowAlias = "NEW")
    {
        if (primaryKey.Columns.Count == 0)
        {
            return "'null'";
        }
        
        if (primaryKey.Columns.Count == 1)
        {
            var column = primaryKey.Columns[0];
            var quotedColumn = $"\"{column.Replace("\"", "\"\"")}\"";
            return $"json_object('{column}', {rowAlias}.{quotedColumn})";
        }
        
        // Multiple columns - build JSON object
        var jsonPairs = new List<string>();
        foreach (var column in primaryKey.Columns)
        {
            var quotedColumn = $"\"{column.Replace("\"", "\"\"")}\"";
            jsonPairs.Add($"'{column}', {rowAlias}.{quotedColumn}");
        }
        
        return $"json_object({string.Join(", ", jsonPairs)})";
    }
    
    private string BuildRowDataJson(string rowAlias, SqliteConnection connection, string tableName)
    {
        var columns = _primaryKeyService.GetColumns(connection, tableName);
        
        if (columns.Count == 0)
        {
            return "'null'";
        }
        
        var jsonPairs = new List<string>();
        foreach (var column in columns)
        {
            var quotedColumn = $"\"{column.Name.Replace("\"", "\"\"")}\"";
            jsonPairs.Add($"'{column.Name}', {rowAlias}.{quotedColumn}");
        }
        
        return $"json_object({string.Join(", ", jsonPairs)})";
    }
    
    private static string GetTriggerName(string tableName, string operation)
    {
        var safeTableName = tableName.Replace("\"", "").Replace("'", "");
        return $"changelog_{safeTableName}_{operation}";
    }
}

/// <summary>
/// Utilities for working with change log entries
/// </summary>
public static class ChangeLogUtils
{
    /// <summary>
    /// Parses primary key values from JSON stored in change log
    /// </summary>
    public static Dictionary<string, object?> ParsePrimaryKeyValues(string? primaryKeyJson)
    {
        if (string.IsNullOrEmpty(primaryKeyJson))
        {
            return new Dictionary<string, object?>();
        }
        
        try
        {
            var values = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(primaryKeyJson);
            return values ?? new Dictionary<string, object?>();
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }
    
    /// <summary>
    /// Parses row data from JSON stored in change log
    /// </summary>
    public static Dictionary<string, object?> ParseRowData(string? rowDataJson)
    {
        if (string.IsNullOrEmpty(rowDataJson))
        {
            return new Dictionary<string, object?>();
        }
        
        try
        {
            var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(rowDataJson);
            return data ?? new Dictionary<string, object?>();
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }
    
    /// <summary>
    /// Builds a WHERE clause to find the original row based on primary key values
    /// </summary>
    public static string BuildPrimaryKeyWhereClause(
        Dictionary<string, object?> primaryKeyValues, 
        Dictionary<string, object?> parameters)
    {
        if (primaryKeyValues.Count == 0)
        {
            return "1=1"; // No primary key, match all (shouldn't happen in practice)
        }
        
        var conditions = new List<string>();
        
        foreach (var kvp in primaryKeyValues)
        {
            var columnName = kvp.Key;
            var quotedColumn = $"\"{columnName.Replace("\"", "\"\"")}\"";
            var paramName = $"pk_{columnName}_{parameters.Count}";
            
            if (kvp.Value == null)
            {
                conditions.Add($"{quotedColumn} IS NULL");
            }
            else
            {
                conditions.Add($"{quotedColumn} = @{paramName}");
                parameters[paramName] = kvp.Value;
            }
        }
        
        return string.Join(" AND ", conditions);
    }
    
    /// <summary>
    /// Formats a change log operation for display
    /// </summary>
    public static string FormatOperation(string operation, DateTime? changedAt = null)
    {
        var timestamp = changedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "unknown";
        return $"{operation} at {timestamp}";
    }
    
    /// <summary>
    /// Determines if a change log entry represents a meaningful change
    /// (filters out no-op updates where old and new values are the same)
    /// </summary>
    public static bool IsMeaningfulChange(
        string operation, 
        Dictionary<string, object?> oldData, 
        Dictionary<string, object?> newData)
    {
        if (operation == "INSERT" || operation == "DELETE")
        {
            return true; // Inserts and deletes are always meaningful
        }
        
        if (operation != "UPDATE")
        {
            return false; // Unknown operation
        }
        
        // For updates, check if any values actually changed
        foreach (var kvp in newData)
        {
            var oldValue = oldData.GetValueOrDefault(kvp.Key);
            var newValue = kvp.Value;
            
            if (!object.Equals(oldValue, newValue))
            {
                return true; // Found a difference
            }
        }
        
        return false; // No meaningful changes detected
    }
}