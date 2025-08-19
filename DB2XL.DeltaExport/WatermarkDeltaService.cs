using Microsoft.Data.Sqlite;
using System.Diagnostics;
using DB2XL.Query;

namespace DB2XL.DeltaExport;

/// <summary>
/// Implementation of watermark-based delta exports
/// Uses timestamp or monotonically increasing ID columns to track changes
/// </summary>
public sealed class WatermarkDeltaService : IWatermarkDeltaService
{
    private readonly IDeltaQueryExecutor _queryExecutor;
    private readonly IPrimaryKeyDiscoveryService _primaryKeyService;
    
    public WatermarkDeltaService(
        IDeltaQueryExecutor? queryExecutor = null,
        IPrimaryKeyDiscoveryService? primaryKeyService = null)
    {
        _queryExecutor = queryExecutor ?? new DeltaQueryExecutor();
        _primaryKeyService = primaryKeyService ?? new PrimaryKeyDiscoveryService();
    }
    
    public IReadOnlyList<string> DiscoverWatermarkColumns(SqliteConnection connection, string tableName)
    {
        var columns = _primaryKeyService.GetColumns(connection, tableName);
        var candidates = new List<(string name, int priority)>();
        
        foreach (var column in columns)
        {
            var priority = GetWatermarkPriority(column);
            if (priority > 0)
            {
                candidates.Add((column.Name, priority));
            }
        }
        
        // Return columns ordered by priority (highest first)
        return candidates
            .OrderByDescending(c => c.priority)
            .Select(c => c.name)
            .ToArray();
    }
    
    public async Task<DeltaExportResult> ExecuteDeltaExportAsync(
        SqliteConnection connection, 
        string tableName, 
        DeltaExportConfig config, 
        DeltaCheckpoint? checkpoint = null)
    {
        var stopwatch = Stopwatch.StartNew();
        
        // Validate configuration
        var validation = ValidateWatermarkColumns(connection, tableName, config.WatermarkColumns);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Invalid watermark configuration: {string.Join(", ", validation.Errors)}");
        }
        
        // Get current watermark values if this is the first export
        var watermarkValues = checkpoint?.WatermarkValues ?? new Dictionary<string, object?>();
        
        // Build and execute delta query
        var query = DeltaQueryBuilder.BuildWatermarkQuery(
            tableName,
            config.WatermarkColumns,
            watermarkValues,
            config.AdditionalFilter,
            config.CustomOrdering,
            config.MaxRows);
        
        var (rows, totalCount, hasMore) = await _queryExecutor.ExecuteDeltaQueryAsync(
            connection, query, config.MaxRows);
        
        var rowList = rows.ToList();
        var rowsExported = rowList.Count;
        
        // Calculate new watermark values from the last exported row
        var newWatermarkValues = watermarkValues;
        if (rowsExported > 0)
        {
            var lastRow = rowList.Last();
            newWatermarkValues = ExtractWatermarkValues(lastRow, config.WatermarkColumns);
        }
        
        // Create new checkpoint
        var newCheckpoint = new DeltaCheckpoint
        {
            CheckpointId = Guid.NewGuid().ToString("N")[..8], // Short ID
            TableName = tableName,
            Strategy = DeltaStrategy.Watermark,
            CreatedAt = DateTime.UtcNow,
            WatermarkValues = newWatermarkValues,
            RowsProcessed = (checkpoint?.RowsProcessed ?? 0) + rowsExported,
            Metadata = new Dictionary<string, object>
            {
                ["watermarkColumns"] = config.WatermarkColumns,
                ["totalRowsInQuery"] = totalCount,
                ["hasAdditionalFilter"] = config.AdditionalFilter != null,
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
    
    public ValidationResult ValidateWatermarkColumns(
        SqliteConnection connection, 
        string tableName, 
        IReadOnlyList<string> watermarkColumns)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        
        if (watermarkColumns.Count == 0)
        {
            errors.Add("At least one watermark column is required");
            return ValidationResult.Failure(errors.ToArray());
        }
        
        var tableColumns = _primaryKeyService.GetColumns(connection, tableName);
        var columnsByName = tableColumns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        
        foreach (var watermarkColumn in watermarkColumns)
        {
            if (!columnsByName.TryGetValue(watermarkColumn, out var column))
            {
                errors.Add($"Watermark column '{watermarkColumn}' does not exist in table '{tableName}'");
                continue;
            }
            
            // Check if column type is suitable for watermarking
            var suitability = AssessColumnSuitability(column);
            if (suitability.level == SuitabilityLevel.Unsuitable)
            {
                errors.Add($"Column '{watermarkColumn}' is unsuitable for watermarking: {suitability.reason}");
            }
            else if (suitability.level == SuitabilityLevel.Warning)
            {
                warnings.Add($"Column '{watermarkColumn}' may have issues: {suitability.reason}");
            }
        }
        
        // Check for indexes on watermark columns
        var indexes = _primaryKeyService.GetIndexes(connection, tableName);
        foreach (var watermarkColumn in watermarkColumns)
        {
            var hasIndex = indexes.Any(idx => idx.Columns.Contains(watermarkColumn, StringComparer.OrdinalIgnoreCase));
            if (!hasIndex)
            {
                warnings.Add($"Watermark column '{watermarkColumn}' is not indexed, which may impact performance");
            }
        }
        
        if (errors.Count > 0)
        {
            return ValidationResult.Failure(errors.ToArray());
        }
        
        return warnings.Count > 0 
            ? ValidationResult.WithWarnings(warnings.ToArray())
            : ValidationResult.Success();
    }
    
    private static int GetWatermarkPriority(ColumnInfo column)
    {
        var columnName = column.Name.ToLowerInvariant();
        var columnType = column.Type.ToUpperInvariant();
        
        // High priority: timestamp columns
        if (columnName.Contains("timestamp") || columnName.Contains("created") || 
            columnName.Contains("updated") || columnName.Contains("modified"))
        {
            return 100;
        }
        
        // High priority: ID columns that are likely auto-incrementing
        if ((columnName.Contains("id") || columnName == "rowid") && 
            (columnType.Contains("INTEGER") || columnType.Contains("BIGINT")))
        {
            return 90;
        }
        
        // Medium priority: datetime/date columns
        if (columnType.Contains("DATETIME") || columnType.Contains("DATE") || 
            columnName.Contains("date") || columnName.Contains("time"))
        {
            return 80;
        }
        
        // Medium priority: sequence/version columns
        if (columnName.Contains("seq") || columnName.Contains("version") || 
            columnName.Contains("rev") || columnName.Contains("num"))
        {
            return 70;
        }
        
        // Low priority: other integer columns
        if (columnType.Contains("INTEGER") || columnType.Contains("BIGINT") || 
            columnType.Contains("NUMERIC"))
        {
            return 40;
        }
        
        // Very low priority: text that might contain timestamps
        if (columnType.Contains("TEXT") && 
            (columnName.Contains("timestamp") || columnName.Contains("date")))
        {
            return 30;
        }
        
        return 0; // Not suitable
    }
    
    private static Dictionary<string, object?> ExtractWatermarkValues(
        Dictionary<string, object?> row, 
        IReadOnlyList<string> watermarkColumns)
    {
        var watermarkValues = new Dictionary<string, object?>();
        
        foreach (var column in watermarkColumns)
        {
            // Try case-insensitive lookup
            var value = row.FirstOrDefault(kvp => 
                string.Equals(kvp.Key, column, StringComparison.OrdinalIgnoreCase)).Value;
            watermarkValues[column] = value;
        }
        
        return watermarkValues;
    }
    
    private enum SuitabilityLevel
    {
        Suitable,
        Warning,
        Unsuitable
    }
    
    private static (SuitabilityLevel level, string reason) AssessColumnSuitability(ColumnInfo column)
    {
        var columnType = column.Type.ToUpperInvariant();
        var columnName = column.Name.ToLowerInvariant();
        
        // Check for nullable columns that might have gaps
        if (!column.NotNull && !columnName.Contains("id"))
        {
            return (SuitabilityLevel.Warning, "Column is nullable, which may cause gaps in delta exports");
        }
        
        // Check for unsuitable types
        if (columnType.Contains("BLOB"))
        {
            return (SuitabilityLevel.Unsuitable, "BLOB columns cannot be used as watermarks");
        }
        
        // Check for text columns that might not be orderable
        if (columnType.Contains("TEXT") && 
            !columnName.Contains("timestamp") && !columnName.Contains("date"))
        {
            return (SuitabilityLevel.Warning, "TEXT columns may not provide reliable ordering for delta exports");
        }
        
        // Check for floating point columns
        if (columnType.Contains("REAL") || columnType.Contains("FLOAT") || columnType.Contains("DOUBLE"))
        {
            return (SuitabilityLevel.Warning, "Floating point columns may have precision issues for watermarking");
        }
        
        return (SuitabilityLevel.Suitable, string.Empty);
    }
}

/// <summary>
/// Utilities for working with watermark values
/// </summary>
public static class WatermarkUtils
{
    /// <summary>
    /// Parses a watermark value from string representation
    /// Handles common SQLite data types and formats
    /// </summary>
    public static object? ParseWatermarkValue(string? value, string columnType)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        
        var normalizedType = columnType.ToUpperInvariant();
        
        try
        {
            if (normalizedType.Contains("INTEGER") || normalizedType.Contains("BIGINT"))
            {
                return long.Parse(value);
            }
            
            if (normalizedType.Contains("REAL") || normalizedType.Contains("FLOAT") || normalizedType.Contains("DOUBLE"))
            {
                return double.Parse(value);
            }
            
            if (normalizedType.Contains("DATETIME") || normalizedType.Contains("TIMESTAMP"))
            {
                if (DateTime.TryParse(value, out var dateTime))
                {
                    return dateTime;
                }
                
                // Try Unix timestamp
                if (long.TryParse(value, out var unixTimestamp))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).DateTime;
                }
            }
            
            // Default to string
            return value;
        }
        catch
        {
            // If parsing fails, return as string
            return value;
        }
    }
    
    /// <summary>
    /// Formats a watermark value for display
    /// </summary>
    public static string FormatWatermarkValue(object? value)
    {
        return value switch
        {
            null => "<null>",
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"),
            double d => d.ToString("F6"),
            float f => f.ToString("F6"),
            _ => value.ToString() ?? "<null>"
        };
    }
    
    /// <summary>
    /// Determines if a watermark value represents a "reset" (start from beginning)
    /// </summary>
    public static bool IsResetValue(object? value)
    {
        return value switch
        {
            null => true,
            string s => string.IsNullOrEmpty(s) || s == "0",
            long l => l == 0,
            int i => i == 0,
            double d => d == 0.0,
            float f => f == 0.0f,
            DateTime dt => dt == DateTime.MinValue || dt == default,
            _ => false
        };
    }
    
    /// <summary>
    /// Compares two watermark values for ordering
    /// Returns: -1 if left < right, 0 if equal, 1 if left > right
    /// </summary>
    public static int CompareWatermarkValues(object? left, object? right)
    {
        if (left == null && right == null) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        
        // Try to compare as IComparable if types match
        if (left.GetType() == right.GetType() && left is IComparable leftComparable)
        {
            return leftComparable.CompareTo(right);
        }
        
        // Fall back to string comparison
        return string.Compare(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }
}