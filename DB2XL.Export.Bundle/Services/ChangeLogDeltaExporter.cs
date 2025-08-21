using DB2XL.Core.Models;
using DB2XL.Core.Services;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DB2XL.Export.Bundle.Services;

/// <summary>
/// Implementation of trigger-based change log incremental export.
/// Uses __changes table to track all data modifications and export only changed rows.
/// </summary>
public sealed class ChangeLogDeltaExporter : IChangeLogDeltaExporter
{
    private readonly IJsonlExportEngine _jsonlExporter;
    private readonly IParquetExportEngine _parquetExporter;
    
    public ChangeLogDeltaExporter(
        IJsonlExportEngine jsonlExporter,
        IParquetExportEngine parquetExporter)
    {
        _jsonlExporter = jsonlExporter ?? throw new ArgumentNullException(nameof(jsonlExporter));
        _parquetExporter = parquetExporter ?? throw new ArgumentNullException(nameof(parquetExporter));
    }

    /// <inheritdoc />
    public async Task<DeltaExportResult> ExportDeltaAsync(
        string connectionString,
        string tableName,
        DeltaCheckpoint? lastCheckpoint,
        ChangeLogDeltaExportOptions options,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var warnings = new List<string>();
        var exportedFiles = new List<string>();
        
        try
        {
            // Validate change log setup
            var validation = await ValidateChangeLogSetupAsync(connectionString, tableName);
            if (!validation.IsValid)
            {
                errors.AddRange(validation.Errors);
                return CreateFailedResult(errors, warnings, stopwatch.Elapsed);
            }
            
            warnings.AddRange(validation.Warnings);
            
            // Validate checkpoint if provided
            if (options.ValidateCheckpoint && lastCheckpoint != null)
            {
                if (!ValidateCheckpointConsistency(lastCheckpoint, tableName, options))
                {
                    errors.Add("Checkpoint validation failed - selection criteria may have changed");
                    return CreateFailedResult(errors, warnings, stopwatch.Elapsed);
                }
            }
            
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            
            // Get change log entries since last checkpoint
            var changes = await GetChangeLogEntriesAsync(
                connection, 
                tableName, 
                lastCheckpoint, 
                options);
            
            if (changes.Count == 0)
            {
                // No changes found
                var currentCheckpoint = lastCheckpoint != null 
                    ? lastCheckpoint with { CheckpointTimestamp = DateTime.UtcNow }
                    : await GetCurrentChangeLogCheckpointAsync(connectionString, tableName, cancellationToken);
                    
                stopwatch.Stop();
                return new DeltaExportResult
                {
                    IsSuccess = true,
                    RowsExported = 0,
                    NewCheckpoint = currentCheckpoint,
                    ExportedFiles = Array.Empty<string>(),
                    DataTimeRange = null,
                    Duration = stopwatch.Elapsed,
                    Errors = errors.AsReadOnly(),
                    Warnings = warnings.AsReadOnly()
                };
            }
            
            // Export changed rows
            var exportResult = await ExportChangedRowsAsync(
                connection,
                tableName,
                changes,
                validation.PrimaryKeyColumns,
                options,
                cancellationToken);
            
            exportedFiles.AddRange(exportResult.Files);
            
            // Create new checkpoint from latest change
            var latestChange = changes.MaxBy(c => c.ChangeId)!;
            var newCheckpoint = new DeltaCheckpoint
            {
                TableName = tableName,
                WatermarkColumn = "change_id", // Using change_id as watermark
                LastWatermarkValue = latestChange.ChangeId,
                LastPrimaryKeyValue = latestChange.PrimaryKeyValue,
                CheckpointTimestamp = DateTime.UtcNow,
                RowsProcessed = (lastCheckpoint?.RowsProcessed ?? 0) + exportResult.RowCount,
                SelectionHash = ComputeSelectionHash(options)
            };
            
            // Cleanup processed entries if requested
            if (options.CleanupProcessedEntries)
            {
                await CleanupProcessedEntriesAsync(connection, options.ChangeLogTableName, latestChange.ChangeId);
            }
            
            stopwatch.Stop();
            
            return new DeltaExportResult
            {
                IsSuccess = true,
                RowsExported = exportResult.RowCount,
                NewCheckpoint = newCheckpoint,
                ExportedFiles = exportedFiles.AsReadOnly(),
                DataTimeRange = exportResult.TimeRange,
                Duration = stopwatch.Elapsed,
                Errors = errors.AsReadOnly(),
                Warnings = warnings.AsReadOnly()
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            errors.Add($"Change log delta export failed: {ex.Message}");
            return CreateFailedResult(errors, warnings, stopwatch.Elapsed);
        }
    }

    /// <inheritdoc />
    public async Task<DeltaCheckpoint> GetCurrentChangeLogCheckpointAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var changeLogTable = "__changes";
        
        // Check if __changes table exists
        if (!await TableExistsAsync(connection, changeLogTable))
        {
            return new DeltaCheckpoint
            {
                TableName = tableName,
                WatermarkColumn = "change_id",
                LastWatermarkValue = null,
                LastPrimaryKeyValue = null,
                CheckpointTimestamp = DateTime.UtcNow,
                RowsProcessed = 0,
                SelectionHash = string.Empty
            };
        }
        
        var sql = $@"
            SELECT 
                MAX(change_id) as max_change_id,
                COUNT(*) as total_changes
            FROM {QuoteIdentifier(changeLogTable)}
            WHERE table_name = @tableName";
        
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@tableName", tableName);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        
        object? maxChangeId = null;
        long totalChanges = 0;
        
        if (await reader.ReadAsync(cancellationToken))
        {
            maxChangeId = reader.IsDBNull(0) ? null : reader.GetValue(0);
            totalChanges = reader.GetInt64(1);
        }
        
        return new DeltaCheckpoint
        {
            TableName = tableName,
            WatermarkColumn = "change_id",
            LastWatermarkValue = maxChangeId,
            LastPrimaryKeyValue = null,
            CheckpointTimestamp = DateTime.UtcNow,
            RowsProcessed = totalChanges,
            SelectionHash = string.Empty
        };
    }

    /// <inheritdoc />
    public async Task<ChangeLogValidationResult> ValidateChangeLogSetupAsync(
        string connectionString,
        string tableName)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var suggestions = new List<string>();
        
        try
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            
            // Check if target table exists
            if (!await TableExistsAsync(connection, tableName))
            {
                errors.Add($"Table '{tableName}' does not exist");
                return new ChangeLogValidationResult { IsValid = false, Errors = errors.AsReadOnly() };
            }
            
            // Check if __changes table exists
            var changeLogExists = await TableExistsAsync(connection, "__changes");
            if (!changeLogExists)
            {
                errors.Add("Change log table '__changes' does not exist. Run SetupChangeLogAsync first.");
                return new ChangeLogValidationResult 
                { 
                    IsValid = false, 
                    ChangeLogTableExists = false,
                    Errors = errors.AsReadOnly() 
                };
            }
            
            // Get primary key columns
            var primaryKeys = await DetectPrimaryKeyColumnsAsync(connection, tableName);
            if (primaryKeys.Count == 0)
            {
                warnings.Add($"Table '{tableName}' has no primary key - change tracking may be unreliable");
            }
            
            // Check for existing triggers
            var triggers = await GetTriggersForTableAsync(connection, tableName);
            var trackedOps = new List<ChangeOperation>();
            
            if (triggers.Any(t => t.Contains("insert", StringComparison.OrdinalIgnoreCase)))
                trackedOps.Add(ChangeOperation.Insert);
            if (triggers.Any(t => t.Contains("update", StringComparison.OrdinalIgnoreCase)))
                trackedOps.Add(ChangeOperation.Update);
            if (triggers.Any(t => t.Contains("delete", StringComparison.OrdinalIgnoreCase)))
                trackedOps.Add(ChangeOperation.Delete);
            
            if (trackedOps.Count == 0)
            {
                errors.Add($"No change tracking triggers found for table '{tableName}'. Run SetupChangeLogAsync first.");
            }
            else if (trackedOps.Count < 3)
            {
                warnings.Add($"Only {trackedOps.Count} of 3 change tracking triggers configured for '{tableName}'");
            }
            
            // Check change log table structure
            await ValidateChangeLogTableStructureAsync(connection, errors, warnings);
            
            return new ChangeLogValidationResult
            {
                IsValid = errors.Count == 0,
                ChangeLogTableExists = changeLogExists,
                PrimaryKeyColumns = primaryKeys,
                ExistingTriggers = triggers,
                TrackedOperations = trackedOps.AsReadOnly(),
                Errors = errors.AsReadOnly(),
                Warnings = warnings.AsReadOnly(),
                Suggestions = suggestions.AsReadOnly()
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Validation failed: {ex.Message}");
            return new ChangeLogValidationResult 
            { 
                IsValid = false, 
                Errors = errors.AsReadOnly() 
            };
        }
    }

    /// <inheritdoc />
    public async Task<ChangeLogSetupResult> SetupChangeLogAsync(
        string connectionString,
        string tableName,
        ChangeLogSetupOptions? options = null)
    {
        options ??= new ChangeLogSetupOptions();
        var errors = new List<string>();
        var warnings = new List<string>();
        var createdComponents = new List<string>();
        
        try
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            
            using var transaction = connection.BeginTransaction();
            
            try
            {
                // Ensure target table exists
                if (!await TableExistsAsync(connection, tableName))
                {
                    errors.Add($"Table '{tableName}' does not exist");
                    return new ChangeLogSetupResult { IsSuccess = false, Errors = errors.AsReadOnly() };
                }
                
                // Create __changes table if it doesn't exist
                if (!await TableExistsAsync(connection, options.ChangeLogTableName))
                {
                    await CreateChangeLogTableAsync(connection, options.ChangeLogTableName);
                    createdComponents.Add($"Table '{options.ChangeLogTableName}'");
                }
                
                // Get primary key columns
                var primaryKeys = await DetectPrimaryKeyColumnsAsync(connection, tableName);
                if (primaryKeys.Count == 0)
                {
                    warnings.Add($"Table '{tableName}' has no primary key - using rowid");
                    primaryKeys = new List<string> { "rowid" }.AsReadOnly();
                }
                
                // Create triggers
                if (options.TrackInserts)
                {
                    await CreateInsertTriggerAsync(connection, tableName, primaryKeys, options);
                    createdComponents.Add($"INSERT trigger for '{tableName}'");
                }
                
                if (options.TrackUpdates)
                {
                    await CreateUpdateTriggerAsync(connection, tableName, primaryKeys, options);
                    createdComponents.Add($"UPDATE trigger for '{tableName}'");
                }
                
                if (options.TrackDeletes)
                {
                    await CreateDeleteTriggerAsync(connection, tableName, primaryKeys, options);
                    createdComponents.Add($"DELETE trigger for '{tableName}'");
                }
                
                transaction.Commit();
                
                return new ChangeLogSetupResult
                {
                    IsSuccess = true,
                    CreatedComponents = createdComponents.AsReadOnly(),
                    Errors = errors.AsReadOnly(),
                    Warnings = warnings.AsReadOnly()
                };
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Setup failed: {ex.Message}");
            return new ChangeLogSetupResult
            {
                IsSuccess = false,
                CreatedComponents = createdComponents.AsReadOnly(),
                Errors = errors.AsReadOnly(),
                Warnings = warnings.AsReadOnly()
            };
        }
    }

    /// <inheritdoc />
    public async Task<bool> RemoveChangeLogAsync(string connectionString, string tableName)
    {
        try
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            
            using var transaction = connection.BeginTransaction();
            
            try
            {
                // Remove triggers
                var triggerNames = new[]
                {
                    $"tr_{tableName}_insert_changlog",
                    $"tr_{tableName}_update_changlog", 
                    $"tr_{tableName}_delete_changlog"
                };
                
                foreach (var triggerName in triggerNames)
                {
                    var sql = $"DROP TRIGGER IF EXISTS {QuoteIdentifier(triggerName)}";
                    using var command = new SqliteCommand(sql, connection, transaction);
                    await command.ExecuteNonQueryAsync();
                }
                
                // Clean up entries for this table from __changes
                var cleanupSql = "DELETE FROM __changes WHERE table_name = @tableName";
                using var cleanupCommand = new SqliteCommand(cleanupSql, connection, transaction);
                cleanupCommand.Parameters.AddWithValue("@tableName", tableName);
                await cleanupCommand.ExecuteNonQueryAsync();
                
                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch
        {
            return false;
        }
    }
    
    private async Task<IReadOnlyList<ChangeLogEntry>> GetChangeLogEntriesAsync(
        SqliteConnection connection,
        string tableName,
        DeltaCheckpoint? lastCheckpoint,
        ChangeLogDeltaExportOptions options)
    {
        var whereClause = new StringBuilder();
        whereClause.Append("table_name = @tableName");
        
        var parameters = new Dictionary<string, object?>
        {
            ["@tableName"] = tableName
        };
        
        if (lastCheckpoint?.LastWatermarkValue != null)
        {
            whereClause.Append(" AND change_id > @lastChangeId");
            parameters["@lastChangeId"] = lastCheckpoint.LastWatermarkValue;
        }
        
        // Filter by operation types
        if (options.IncludeOperations.Count > 0 && options.IncludeOperations.Count < 3)
        {
            var opConditions = options.IncludeOperations.Select((op, i) => $"@op{i}").ToList();
            whereClause.Append($" AND operation IN ({string.Join(",", opConditions)})");
            
            for (int i = 0; i < options.IncludeOperations.Count; i++)
            {
                parameters[$"@op{i}"] = options.IncludeOperations[i].ToString().ToUpperInvariant();
            }
        }
        
        var sql = $@"
            SELECT change_id, table_name, operation, primary_key_value, timestamp, transaction_id, row_data
            FROM {QuoteIdentifier(options.ChangeLogTableName)}
            WHERE {whereClause}
            ORDER BY change_id ASC";
        
        if (options.MaxRows > 0)
        {
            sql += $" LIMIT {options.MaxRows}";
        }
        
        using var command = new SqliteCommand(sql, connection);
        foreach (var param in parameters)
        {
            command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
        }
        
        var changes = new List<ChangeLogEntry>();
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            var operationOrdinal = reader.GetOrdinal("operation");
            var operation = Enum.Parse<ChangeOperation>(reader.GetString(operationOrdinal), ignoreCase: true);
            
            var changeIdOrdinal = reader.GetOrdinal("change_id");
            var tableNameOrdinal = reader.GetOrdinal("table_name");
            var primaryKeyValueOrdinal = reader.GetOrdinal("primary_key_value");
            var timestampOrdinal = reader.GetOrdinal("timestamp");
            var transactionIdOrdinal = reader.GetOrdinal("transaction_id");
            var rowDataOrdinal = reader.GetOrdinal("row_data");
            
            changes.Add(new ChangeLogEntry
            {
                ChangeId = reader.GetInt64(changeIdOrdinal),
                TableName = reader.GetString(tableNameOrdinal),
                Operation = operation,
                PrimaryKeyValue = reader.GetValue(primaryKeyValueOrdinal),
                Timestamp = DateTime.Parse(reader.GetString(timestampOrdinal)),
                TransactionId = reader.IsDBNull(transactionIdOrdinal) ? null : reader.GetString(transactionIdOrdinal),
                RowData = reader.IsDBNull(rowDataOrdinal) ? null : reader.GetString(rowDataOrdinal)
            });
        }
        
        return changes.AsReadOnly();
    }
    
    private async Task<ChangeLogExportData> ExportChangedRowsAsync(
        SqliteConnection connection,
        string tableName,
        IReadOnlyList<ChangeLogEntry> changes,
        IReadOnlyList<string> primaryKeyColumns,
        ChangeLogDeltaExportOptions options,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var quotedTable = QuoteIdentifier(tableName);
        var primaryKeyCol = primaryKeyColumns.Count > 0 ? primaryKeyColumns[0] : "rowid";
        var quotedPk = QuoteIdentifier(primaryKeyCol);
        
        // Create output file
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var fileName = options.FileNamePattern
            .Replace("{table}", tableName)
            .Replace("{timestamp}", timestamp)
            .Replace("{sequence}", "001");
            
        var outputPath = Path.Combine(options.OutputDirectory, fileName);
        
        if (options.Format == ExportFormat.Jsonl)
        {
            outputPath += ".jsonl";
        }
        else if (options.Format == ExportFormat.Parquet)
        {
            outputPath += ".parquet";
        }
        
        Directory.CreateDirectory(options.OutputDirectory);
        
        // Group changes by primary key (latest change wins)
        var latestChanges = changes
            .GroupBy(c => c.PrimaryKeyValue?.ToString() ?? "")
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.ChangeId).First());
        
        var exportedRows = new List<Dictionary<string, object?>>();
        var totalRowCount = 0L;
        DateTime? minTime = null;
        DateTime? maxTime = null;
        
        foreach (var change in latestChanges.Values)
        {
            // Skip deleted rows unless explicitly requested
            if (change.Operation == ChangeOperation.Delete && !options.IncludeDeleted)
                continue;
            
            Dictionary<string, object?> rowData;
            
            if (change.Operation == ChangeOperation.Delete)
            {
                // For deleted rows, create minimal row with PK and metadata
                rowData = new Dictionary<string, object?>
                {
                    [primaryKeyCol] = change.PrimaryKeyValue,
                    ["__change_operation"] = "DELETE",
                    ["__change_timestamp"] = change.Timestamp.ToString("O"),
                    ["__change_id"] = change.ChangeId
                };
            }
            else
            {
                // For insert/update, fetch current row data
                var fetchSql = $"SELECT * FROM {quotedTable} WHERE {quotedPk} = @pk";
                using var fetchCommand = new SqliteCommand(fetchSql, connection);
                fetchCommand.Parameters.AddWithValue("@pk", change.PrimaryKeyValue);
                using var reader = await fetchCommand.ExecuteReaderAsync(cancellationToken);
                
                if (await reader.ReadAsync(cancellationToken))
                {
                    rowData = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        var columnName = reader.GetName(i);
                        rowData[columnName] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    
                    // Add change metadata
                    rowData["__change_operation"] = change.Operation.ToString().ToUpperInvariant();
                    rowData["__change_timestamp"] = change.Timestamp.ToString("O");
                    rowData["__change_id"] = change.ChangeId;
                }
                else
                {
                    // Row no longer exists (was deleted after the change log entry)
                    continue;
                }
            }
            
            exportedRows.Add(rowData);
            totalRowCount++;
            
            // Track time range
            if (minTime == null || change.Timestamp < minTime) minTime = change.Timestamp;
            if (maxTime == null || change.Timestamp > maxTime) maxTime = change.Timestamp;
            
            // Write in batches
            if (exportedRows.Count >= options.BatchSize)
            {
                await WriteDataBatchAsync(exportedRows, outputPath, options.Format, cancellationToken);
                exportedRows.Clear();
            }
        }
        
        // Write remaining rows
        if (exportedRows.Count > 0)
        {
            await WriteDataBatchAsync(exportedRows, outputPath, options.Format, cancellationToken);
        }
        
        if (File.Exists(outputPath))
        {
            files.Add(outputPath);
        }
        
        var timeRange = (minTime.HasValue && maxTime.HasValue) 
            ? new DateTimeRange(minTime.Value, maxTime.Value)
            : null;
        
        return new ChangeLogExportData
        {
            Files = files,
            RowCount = totalRowCount,
            TimeRange = timeRange
        };
    }
    
    private async Task WriteDataBatchAsync(
        List<Dictionary<string, object?>> rows,
        string outputPath,
        ExportFormat format,
        CancellationToken cancellationToken)
    {
        if (format == ExportFormat.Jsonl)
        {
            using var writer = new StreamWriter(outputPath, append: true);
            foreach (var row in rows)
            {
                var json = JsonSerializer.Serialize(row);
                await writer.WriteLineAsync(json);
            }
        }
        else
        {
            throw new NotSupportedException($"Format {format} not yet implemented for change log delta export");
        }
    }
    
    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        var sql = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = @name";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@name", tableName);
        var result = await command.ExecuteScalarAsync();
        return result != null;
    }
    
    private static async Task<IReadOnlyList<string>> DetectPrimaryKeyColumnsAsync(SqliteConnection connection, string tableName)
    {
        var primaryKeys = new List<string>();
        var sql = $"PRAGMA table_info({QuoteIdentifier(tableName)})";
        
        using var command = new SqliteCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();
        
        var pkColumns = new List<(string name, int pk)>();
        
        while (await reader.ReadAsync())
        {
            var pk = reader.GetInt32(reader.GetOrdinal("pk"));
            if (pk > 0)
            {
                var name = reader.GetString(reader.GetOrdinal("name"));
                pkColumns.Add((name, pk));
            }
        }
        
        return pkColumns.OrderBy(c => c.pk).Select(c => c.name).ToList().AsReadOnly();
    }
    
    private static async Task<IReadOnlyList<string>> GetTriggersForTableAsync(SqliteConnection connection, string tableName)
    {
        var triggers = new List<string>();
        var sql = "SELECT name FROM sqlite_master WHERE type = 'trigger' AND tbl_name = @tableName";
        
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@tableName", tableName);
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            triggers.Add(reader.GetString(0));
        }
        
        return triggers.AsReadOnly();
    }
    
    private static async Task ValidateChangeLogTableStructureAsync(SqliteConnection connection, List<string> errors, List<string> warnings)
    {
        var sql = "PRAGMA table_info(__changes)";
        using var command = new SqliteCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();
        
        var columns = new HashSet<string>();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(reader.GetOrdinal("name")));
        }
        
        var requiredColumns = new[] { "change_id", "table_name", "operation", "primary_key_value", "timestamp" };
        foreach (var required in requiredColumns)
        {
            if (!columns.Contains(required))
            {
                errors.Add($"Change log table missing required column: {required}");
            }
        }
    }
    
    private static async Task CreateChangeLogTableAsync(SqliteConnection connection, string changeLogTableName)
    {
        var sql = $@"
            CREATE TABLE {QuoteIdentifier(changeLogTableName)} (
                change_id INTEGER PRIMARY KEY AUTOINCREMENT,
                table_name TEXT NOT NULL,
                operation TEXT NOT NULL CHECK(operation IN ('INSERT', 'UPDATE', 'DELETE')),
                primary_key_value TEXT NOT NULL,
                timestamp TEXT NOT NULL DEFAULT (datetime('now', 'utc')),
                transaction_id TEXT,
                row_data TEXT
            );
            
            CREATE INDEX IF NOT EXISTS idx_changes_table_name ON {QuoteIdentifier(changeLogTableName)}(table_name);
            CREATE INDEX IF NOT EXISTS idx_changes_timestamp ON {QuoteIdentifier(changeLogTableName)}(timestamp);
            CREATE INDEX IF NOT EXISTS idx_changes_table_changeid ON {QuoteIdentifier(changeLogTableName)}(table_name, change_id);";
        
        using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
    
    private static async Task CreateInsertTriggerAsync(
        SqliteConnection connection,
        string tableName,
        IReadOnlyList<string> primaryKeys,
        ChangeLogSetupOptions options)
    {
        var pkValue = primaryKeys.Count == 1 && primaryKeys[0] != "rowid"
            ? $"NEW.{QuoteIdentifier(primaryKeys[0])}"
            : primaryKeys[0] == "rowid" 
                ? "NEW.rowid"
                : $"'{string.Join(",", primaryKeys.Select(pk => "' || NEW." + QuoteIdentifier(pk) + " || '"))}'";
        
        var rowData = options.StoreFul­lRowData 
            ? ", json_object('dummy', 'value')" // Simplified for now
            : ", NULL";
        
        var sql = $@"
            CREATE TRIGGER IF NOT EXISTS tr_{tableName}_insert_changlog
            AFTER INSERT ON {QuoteIdentifier(tableName)}
            BEGIN
                INSERT INTO {QuoteIdentifier(options.ChangeLogTableName)}
                (table_name, operation, primary_key_value, row_data)
                VALUES ('{tableName}', 'INSERT', {pkValue}{rowData});
            END;";
        
        using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
    
    private static async Task CreateUpdateTriggerAsync(
        SqliteConnection connection,
        string tableName,
        IReadOnlyList<string> primaryKeys,
        ChangeLogSetupOptions options)
    {
        var pkValue = primaryKeys.Count == 1 && primaryKeys[0] != "rowid"
            ? $"NEW.{QuoteIdentifier(primaryKeys[0])}"
            : primaryKeys[0] == "rowid" 
                ? "NEW.rowid"
                : $"'{string.Join(",", primaryKeys.Select(pk => "' || NEW." + QuoteIdentifier(pk) + " || '"))}'";
        
        var rowData = options.StoreFul­lRowData 
            ? ", json_object('dummy', 'value')" // Simplified for now
            : ", NULL";
        
        var sql = $@"
            CREATE TRIGGER IF NOT EXISTS tr_{tableName}_update_changlog
            AFTER UPDATE ON {QuoteIdentifier(tableName)}
            BEGIN
                INSERT INTO {QuoteIdentifier(options.ChangeLogTableName)}
                (table_name, operation, primary_key_value, row_data)
                VALUES ('{tableName}', 'UPDATE', {pkValue}{rowData});
            END;";
        
        using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
    
    private static async Task CreateDeleteTriggerAsync(
        SqliteConnection connection,
        string tableName,
        IReadOnlyList<string> primaryKeys,
        ChangeLogSetupOptions options)
    {
        var pkValue = primaryKeys.Count == 1 && primaryKeys[0] != "rowid"
            ? $"OLD.{QuoteIdentifier(primaryKeys[0])}"
            : primaryKeys[0] == "rowid" 
                ? "OLD.rowid"
                : $"'{string.Join(",", primaryKeys.Select(pk => "' || OLD." + QuoteIdentifier(pk) + " || '"))}'";
        
        var rowData = options.StoreFul­lRowData 
            ? ", json_object('dummy', 'value')" // Simplified for now
            : ", NULL";
        
        var sql = $@"
            CREATE TRIGGER IF NOT EXISTS tr_{tableName}_delete_changlog
            AFTER DELETE ON {QuoteIdentifier(tableName)}
            BEGIN
                INSERT INTO {QuoteIdentifier(options.ChangeLogTableName)}
                (table_name, operation, primary_key_value, row_data)
                VALUES ('{tableName}', 'DELETE', {pkValue}{rowData});
            END;";
        
        using var command = new SqliteCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
    
    private static async Task CleanupProcessedEntriesAsync(SqliteConnection connection, string changeLogTableName, long lastProcessedChangeId)
    {
        var sql = $"DELETE FROM {QuoteIdentifier(changeLogTableName)} WHERE change_id <= @lastChangeId";
        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@lastChangeId", lastProcessedChangeId);
        await command.ExecuteNonQueryAsync();
    }
    
    private static bool ValidateCheckpointConsistency(
        DeltaCheckpoint checkpoint,
        string tableName,
        ChangeLogDeltaExportOptions options)
    {
        if (!checkpoint.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase))
            return false;
            
        if (!checkpoint.WatermarkColumn.Equals("change_id", StringComparison.OrdinalIgnoreCase))
            return false;
            
        var currentHash = ComputeSelectionHash(options);
        if (!string.IsNullOrEmpty(checkpoint.SelectionHash) && checkpoint.SelectionHash != currentHash)
            return false;
            
        return true;
    }
    
    private static string ComputeSelectionHash(ChangeLogDeltaExportOptions options)
    {
        var json = JsonSerializer.Serialize(new
        {
            options.IncludeDeleted,
            options.MaxRows,
            Operations = options.IncludeOperations.Select(op => op.ToString()).OrderBy(x => x).ToList()
        });
        
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
    
    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
    
    private static DeltaExportResult CreateFailedResult(List<string> errors, List<string> warnings, TimeSpan duration)
    {
        return new DeltaExportResult
        {
            IsSuccess = false,
            RowsExported = 0,
            NewCheckpoint = null,
            ExportedFiles = Array.Empty<string>(),
            DataTimeRange = null,
            Duration = duration,
            Errors = errors.AsReadOnly(),
            Warnings = warnings.AsReadOnly()
        };
    }
    
    private sealed record ChangeLogExportData
    {
        public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();
        public long RowCount { get; init; }
        public DateTimeRange? TimeRange { get; init; }
    }
}