using DB2XL.Core.Models;
using DB2XL.Core.Services;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DB2XL.Export.Bundle.Services;

/// <summary>
/// Implementation of watermark-based incremental export for efficient change detection.
/// Uses timestamp or version columns to track and export only new/modified rows.
/// </summary>
public sealed class WatermarkDeltaExporter : IWatermarkDeltaExporter
{
    private readonly IJsonlExportEngine _jsonlExporter;
    private readonly IParquetExportEngine _parquetExporter;
    
    public WatermarkDeltaExporter(
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
        string watermarkColumn,
        DeltaCheckpoint? lastCheckpoint,
        DeltaExportOptions options,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var warnings = new List<string>();
        var exportedFiles = new List<string>();
        
        try
        {
            // Validate setup
            var validation = await ValidateWatermarkSetupAsync(connectionString, tableName, watermarkColumn);
            if (!validation.IsValid)
            {
                errors.AddRange(validation.Errors);
                return CreateFailedResult(errors, warnings, stopwatch.Elapsed);
            }
            
            warnings.AddRange(validation.Warnings);
            
            // Validate checkpoint if provided
            if (options.ValidateCheckpoint && lastCheckpoint != null)
            {
                if (!ValidateCheckpointConsistency(lastCheckpoint, tableName, watermarkColumn, options))
                {
                    errors.Add("Checkpoint validation failed - selection criteria may have changed");
                    return CreateFailedResult(errors, warnings, stopwatch.Elapsed);
                }
            }
            
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            
            // Build delta query
            var primaryKeys = validation.PrimaryKeyColumns.Count > 0 
                ? validation.PrimaryKeyColumns 
                : options.PrimaryKeyColumns;
                
            if (primaryKeys.Count == 0)
            {
                // Try to detect primary key
                primaryKeys = await DetectPrimaryKeyColumnsAsync(connection, tableName);
                if (primaryKeys.Count == 0)
                {
                    // Use rowid as fallback for tables without explicit primary key
                    primaryKeys = new List<string> { "rowid" }.AsReadOnly();
                    warnings.Add($"Table '{tableName}' has no explicit primary key - using rowid for tie-breaking");
                }
            }
            
            // Create delta query with proper tie-breaking
            var (sql, parameters) = BuildDeltaQuery(
                tableName, 
                watermarkColumn, 
                primaryKeys, 
                lastCheckpoint, 
                options);
            
            // Execute query and export data
            var exportResult = await ExecuteDeltaExportAsync(
                connection,
                sql,
                parameters,
                tableName,
                watermarkColumn,
                primaryKeys,
                options,
                cancellationToken);
            
            exportedFiles.AddRange(exportResult.Files);
            
            // Create new checkpoint
            DeltaCheckpoint? newCheckpoint = null;
            if (exportResult.RowCount > 0)
            {
                newCheckpoint = new DeltaCheckpoint
                {
                    TableName = tableName,
                    WatermarkColumn = watermarkColumn,
                    LastWatermarkValue = exportResult.LastWatermark,
                    LastPrimaryKeyValue = exportResult.LastPrimaryKey,
                    CheckpointTimestamp = DateTime.UtcNow,
                    RowsProcessed = (lastCheckpoint?.RowsProcessed ?? 0) + exportResult.RowCount,
                    SelectionHash = ComputeSelectionHash(options)
                };
            }
            else if (lastCheckpoint != null)
            {
                // No new data, keep existing checkpoint
                newCheckpoint = lastCheckpoint with 
                { 
                    CheckpointTimestamp = DateTime.UtcNow 
                };
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
            errors.Add($"Delta export failed: {ex.Message}");
            return CreateFailedResult(errors, warnings, stopwatch.Elapsed);
        }
    }

    /// <inheritdoc />
    public async Task<DeltaCheckpoint> GetCurrentCheckpointAsync(
        string connectionString,
        string tableName,
        string watermarkColumn,
        CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        
        var primaryKeys = await DetectPrimaryKeyColumnsAsync(connection, tableName);
        
        // Get maximum watermark value
        var quotedTable = QuoteIdentifier(tableName);
        var quotedWatermark = QuoteIdentifier(watermarkColumn);
        var quotedPk = primaryKeys.Count > 0 ? QuoteIdentifier(primaryKeys[0]) : "rowid";
        
        var sql = $@"
            SELECT 
                MAX({quotedWatermark}) as max_watermark,
                COUNT(*) as row_count
            FROM {quotedTable}";
        
        using var command = new SqliteCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        
        object? maxWatermark = null;
        long rowCount = 0;
        
        if (await reader.ReadAsync(cancellationToken))
        {
            maxWatermark = reader.IsDBNull(0) ? null : reader.GetValue(0);
            rowCount = reader.GetInt64(1);
        }
        
        object? maxPrimaryKey = null;
        if (maxWatermark != null && primaryKeys.Count > 0)
        {
            // Get the max primary key for rows with the max watermark
            var pkSql = $@"
                SELECT MAX({quotedPk}) 
                FROM {quotedTable}
                WHERE {quotedWatermark} = @watermark";
            
            using var pkCommand = new SqliteCommand(pkSql, connection);
            pkCommand.Parameters.AddWithValue("@watermark", maxWatermark);
            maxPrimaryKey = await pkCommand.ExecuteScalarAsync(cancellationToken);
        }
        
        return new DeltaCheckpoint
        {
            TableName = tableName,
            WatermarkColumn = watermarkColumn,
            LastWatermarkValue = maxWatermark,
            LastPrimaryKeyValue = maxPrimaryKey,
            CheckpointTimestamp = DateTime.UtcNow,
            RowsProcessed = rowCount,
            SelectionHash = string.Empty
        };
    }

    /// <inheritdoc />
    public async Task<DeltaValidationResult> ValidateWatermarkSetupAsync(
        string connectionString,
        string tableName,
        string watermarkColumn)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var suggestions = new List<string>();
        
        try
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            
            // Check if table exists
            if (!await TableExistsAsync(connection, tableName))
            {
                errors.Add($"Table '{tableName}' does not exist");
                return new DeltaValidationResult { IsValid = false, Errors = errors.AsReadOnly() };
            }
            
            // Get column info
            var columnInfo = await GetColumnInfoAsync(connection, tableName, watermarkColumn);
            if (columnInfo == null)
            {
                errors.Add($"Watermark column '{watermarkColumn}' does not exist in table '{tableName}'");
                return new DeltaValidationResult { IsValid = false, Errors = errors.AsReadOnly() };
            }
            
            // Validate column type
            var columnType = columnInfo.Type.ToUpperInvariant();
            if (!IsValidWatermarkType(columnType))
            {
                errors.Add($"Column '{watermarkColumn}' has type '{columnType}' which is not suitable for watermark comparison");
            }
            
            // Check for primary key
            var primaryKeys = await DetectPrimaryKeyColumnsAsync(connection, tableName);
            if (primaryKeys.Count == 0)
            {
                warnings.Add($"Table '{tableName}' has no primary key - using rowid for tie-breaking");
            }
            
            // Check if watermark column is indexed
            var isIndexed = await IsColumnIndexedAsync(connection, tableName, watermarkColumn);
            if (!isIndexed)
            {
                suggestions.Add($"Consider adding an index on '{watermarkColumn}' for better delta query performance");
            }
            
            // Check for NULL values in watermark column
            var nullCount = await GetNullCountAsync(connection, tableName, watermarkColumn);
            if (nullCount > 0)
            {
                warnings.Add($"Watermark column '{watermarkColumn}' contains {nullCount} NULL values which will be excluded from delta exports");
            }
            
            return new DeltaValidationResult
            {
                IsValid = errors.Count == 0,
                PrimaryKeyColumns = primaryKeys,
                WatermarkColumnType = columnType,
                WatermarkColumnIndexed = isIndexed,
                Errors = errors.AsReadOnly(),
                Warnings = warnings.AsReadOnly(),
                Suggestions = suggestions.AsReadOnly()
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Validation failed: {ex.Message}");
            return new DeltaValidationResult 
            { 
                IsValid = false, 
                Errors = errors.AsReadOnly() 
            };
        }
    }
    
    private static (string sql, Dictionary<string, object?> parameters) BuildDeltaQuery(
        string tableName,
        string watermarkColumn,
        IReadOnlyList<string> primaryKeys,
        DeltaCheckpoint? lastCheckpoint,
        DeltaExportOptions options)
    {
        var quotedTable = QuoteIdentifier(tableName);
        var quotedWatermark = QuoteIdentifier(watermarkColumn);
        var quotedPk = primaryKeys.Count > 0 ? QuoteIdentifier(primaryKeys[0]) : "rowid";
        
        var parameters = new Dictionary<string, object?>();
        var whereClause = new StringBuilder();
        
        if (lastCheckpoint?.LastWatermarkValue != null)
        {
            // Build incremental query with tie-breaking
            whereClause.Append($"({quotedWatermark} > @lastWatermark");
            parameters["@lastWatermark"] = lastCheckpoint.LastWatermarkValue;
            
            if (lastCheckpoint.LastPrimaryKeyValue != null)
            {
                whereClause.Append($" OR ({quotedWatermark} = @lastWatermark AND {quotedPk} > @lastPk)");
                parameters["@lastPk"] = lastCheckpoint.LastPrimaryKeyValue;
            }
            
            whereClause.Append(")");
        }
        else
        {
            // Initial export - get all non-null watermark rows
            whereClause.Append($"{quotedWatermark} IS NOT NULL");
        }
        
        var sql = $@"
            SELECT * 
            FROM {quotedTable}
            WHERE {whereClause}
            ORDER BY {quotedWatermark}, {quotedPk}";
        
        if (options.MaxRows > 0)
        {
            sql += $" LIMIT {options.MaxRows}";
        }
        
        return (sql, parameters);
    }
    
    private async Task<DeltaExportData> ExecuteDeltaExportAsync(
        SqliteConnection connection,
        string sql,
        Dictionary<string, object?> parameters,
        string tableName,
        string watermarkColumn,
        IReadOnlyList<string> primaryKeys,
        DeltaExportOptions options,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var command = new SqliteCommand(sql, connection);
        
        foreach (var param in parameters)
        {
            command.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
        }
        
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        
        // Get column names
        var columnNames = new List<string>();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columnNames.Add(reader.GetName(i));
        }
        
        var watermarkIndex = columnNames.FindIndex(c => c.Equals(watermarkColumn, StringComparison.OrdinalIgnoreCase));
        var pkIndex = primaryKeys.Count > 0 
            ? columnNames.FindIndex(c => c.Equals(primaryKeys[0], StringComparison.OrdinalIgnoreCase))
            : -1;
        
        // Stream data to file
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
        
        var rows = new List<Dictionary<string, object?>>();
        object? lastWatermark = null;
        object? lastPk = null;
        DateTime? minTime = null;
        DateTime? maxTime = null;
        long totalRowCount = 0;
        
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[columnNames[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            
            rows.Add(row);
            totalRowCount++;
            
            // Track watermark values
            if (watermarkIndex >= 0 && !reader.IsDBNull(watermarkIndex))
            {
                lastWatermark = reader.GetValue(watermarkIndex);
                
                if (lastWatermark is string dateStr && DateTime.TryParse(dateStr, out var date))
                {
                    if (minTime == null || date < minTime) minTime = date;
                    if (maxTime == null || date > maxTime) maxTime = date;
                }
            }
            
            // Track primary key
            if (pkIndex >= 0 && !reader.IsDBNull(pkIndex))
            {
                lastPk = reader.GetValue(pkIndex);
            }
            
            // Write batch
            if (rows.Count >= options.BatchSize)
            {
                await WriteDataBatchAsync(rows, outputPath, options.Format, cancellationToken);
                rows.Clear();
            }
        }
        
        // Write remaining rows
        if (rows.Count > 0)
        {
            await WriteDataBatchAsync(rows, outputPath, options.Format, cancellationToken);
        }
        
        if (File.Exists(outputPath))
        {
            files.Add(outputPath);
        }
        
        var timeRange = (minTime.HasValue && maxTime.HasValue) 
            ? new DateTimeRange(minTime.Value, maxTime.Value)
            : null;
        
        return new DeltaExportData
        {
            Files = files,
            RowCount = totalRowCount,
            LastWatermark = lastWatermark,
            LastPrimaryKey = lastPk,
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
            // For other formats, would integrate with appropriate exporters
            throw new NotSupportedException($"Format {format} not yet implemented for delta export");
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
    
    private static async Task<ColumnInfo?> GetColumnInfoAsync(SqliteConnection connection, string tableName, string columnName)
    {
        var sql = $"PRAGMA table_info({QuoteIdentifier(tableName)})";
        using var command = new SqliteCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(reader.GetOrdinal("name"));
            if (name.Equals(columnName, StringComparison.OrdinalIgnoreCase))
            {
                return new ColumnInfo(
                    name,
                    reader.GetString(reader.GetOrdinal("type")),
                    reader.GetInt32(reader.GetOrdinal("notnull")) == 1,
                    reader.IsDBNull(reader.GetOrdinal("dflt_value")) ? null : reader.GetValue(reader.GetOrdinal("dflt_value")),
                    reader.GetInt32(reader.GetOrdinal("pk")) > 0
                );
            }
        }
        
        return null;
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
    
    private static async Task<bool> IsColumnIndexedAsync(SqliteConnection connection, string tableName, string columnName)
    {
        var sql = $"PRAGMA index_list({QuoteIdentifier(tableName)})";
        using var command = new SqliteCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            var indexName = reader.GetString(reader.GetOrdinal("name"));
            
            // Check if this index includes our column
            var indexSql = $"PRAGMA index_info({QuoteIdentifier(indexName)})";
            using var indexCommand = new SqliteCommand(indexSql, connection);
            using var indexReader = await indexCommand.ExecuteReaderAsync();
            
            while (await indexReader.ReadAsync())
            {
                var indexedColumn = indexReader.GetString(indexReader.GetOrdinal("name"));
                if (indexedColumn.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        
        return false;
    }
    
    private static async Task<long> GetNullCountAsync(SqliteConnection connection, string tableName, string columnName)
    {
        var sql = $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)} WHERE {QuoteIdentifier(columnName)} IS NULL";
        using var command = new SqliteCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }
    
    private static bool IsValidWatermarkType(string columnType)
    {
        // Valid types for watermark comparison
        var validTypes = new[] { "INTEGER", "REAL", "TEXT", "DATE", "DATETIME", "TIMESTAMP" };
        return validTypes.Any(t => columnType.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
    
    private static bool ValidateCheckpointConsistency(
        DeltaCheckpoint checkpoint,
        string tableName,
        string watermarkColumn,
        DeltaExportOptions options)
    {
        if (!checkpoint.TableName.Equals(tableName, StringComparison.OrdinalIgnoreCase))
            return false;
            
        if (!checkpoint.WatermarkColumn.Equals(watermarkColumn, StringComparison.OrdinalIgnoreCase))
            return false;
            
        var currentHash = ComputeSelectionHash(options);
        if (!string.IsNullOrEmpty(checkpoint.SelectionHash) && checkpoint.SelectionHash != currentHash)
            return false;
            
        return true;
    }
    
    private static string ComputeSelectionHash(DeltaExportOptions options)
    {
        var json = JsonSerializer.Serialize(new
        {
            options.PrimaryKeyColumns,
            options.IncludeDeleted,
            options.MaxRows
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
    
    private sealed record DeltaExportData
    {
        public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();
        public long RowCount { get; init; }
        public object? LastWatermark { get; init; }
        public object? LastPrimaryKey { get; init; }
        public DateTimeRange? TimeRange { get; init; }
    }
}