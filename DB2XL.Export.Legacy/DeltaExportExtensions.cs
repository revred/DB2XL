using DB2XL.Data.Query;
using DB2XL.Data.Schema;
using Microsoft.Data.Sqlite;
using DB2XL.DeltaExport;
using DB2XL.Query;
using ClosedXML.Excel;

namespace DB2XL;

/// <summary>
/// Extensions for integrating delta exports with SqliteToExcel
/// </summary>
public static class SqliteToExcelDeltaExtensions
{
    /// <summary>
    /// Exports tables using delta export strategy
    /// Only includes data that has changed since the last export
    /// </summary>
    /// <param name="sqlitePath">Path to SQLite database</param>
    /// <param name="xlsxPath">Path for output Excel file</param>
    /// <param name="options">Export options including delta configuration</param>
    public static async Task ExportDeltaAsync(
        string sqlitePath, 
        string xlsxPath, 
        SqliteToExcelOptions options)
    {
        if (options.DeltaExportConfig == null)
        {
            throw new ArgumentException("DeltaExportConfig is required for delta exports", nameof(options));
        }
        
        using var connection = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly;Cache=Shared;Pooling=True;");
        connection.Open();
        
        var deltaService = new DeltaExportService(options.DeltaCheckpointService);
        var tables = SqliteSchemaReader.GetDatabaseObjects(connection, options.TableNameLikeFilter, options.IncludeViews);
        
        using var workbook = new XLWorkbook();
        var exportResults = new List<(string tableName, DeltaExportResult result)>();
        
        foreach (var table in tables)
        {
            try
            {
                var result = await deltaService.ExecuteDeltaExportAsync(
                    connection, table.Name, options.DeltaExportConfig);
                
                exportResults.Add((table.Name, result));
                
                if (result.RowsExported > 0)
                {
                    await AddDeltaDataToWorkbook(workbook, connection, table.Name, result, options);
                }
            }
            catch (Exception ex)
            {
                // Log error but continue with other tables
                Console.WriteLine($"Error exporting delta for table {table.Name}: {ex.Message}");
            }
        }
        
        // Add delta metadata sheet if requested
        if (options.IncludeDeltaMetadata && exportResults.Count > 0)
        {
            AddDeltaMetadataSheet(workbook, exportResults, options);
        }
        
        // Only save if we have data
        if (workbook.Worksheets.Count > 0)
        {
            workbook.SaveAs(xlsxPath);
        }
        else
        {
            // Create an empty workbook with just metadata
            var emptySheet = workbook.Worksheets.Add("No_Changes");
            emptySheet.Cell(1, 1).Value = "No changes detected since last export";
            workbook.SaveAs(xlsxPath);
        }
    }
    
    /// <summary>
    /// Recommends delta export configuration for a database
    /// </summary>
    public static async Task<Dictionary<string, (DeltaStrategy strategy, DeltaExportConfig config)>> 
        RecommendDeltaConfigurationAsync(string sqlitePath, SqliteToExcelOptions? options = null)
    {
        options ??= new SqliteToExcelOptions();
        
        using var connection = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly;Cache=Shared;Pooling=True;");
        connection.Open();
        
        var deltaService = new DeltaExportService();
        var tables = SqliteSchemaReader.GetDatabaseObjects(connection, options.TableNameLikeFilter, options.IncludeViews);
        
        var recommendations = new Dictionary<string, (DeltaStrategy strategy, DeltaExportConfig config)>();
        
        foreach (var table in tables)
        {
            try
            {
                var recommendation = await deltaService.RecommendDeltaStrategyAsync(connection, table.Name);
                recommendations[table.Name] = recommendation;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error analyzing table {table.Name}: {ex.Message}");
                // Provide fallback recommendation
                recommendations[table.Name] = (DeltaStrategy.Full, new DeltaExportConfig { Strategy = DeltaStrategy.Full });
            }
        }
        
        return recommendations;
    }
    
    /// <summary>
    /// Resets delta tracking for all tables or specific table
    /// </summary>
    public static async Task<bool> ResetDeltaTrackingAsync(
        string? tableName = null, 
        IDeltaCheckpointService? checkpointService = null)
    {
        var service = new DeltaExportService(checkpointService ?? new FileDeltaCheckpointService());
        
        if (tableName != null)
        {
            return await service.ResetDeltaTrackingAsync(tableName);
        }
        
        // Reset all tables
        var trackedTables = await service.GetTrackedTablesAsync();
        var allSuccess = true;
        
        foreach (var table in trackedTables)
        {
            var success = await service.ResetDeltaTrackingAsync(table);
            allSuccess = allSuccess && success;
        }
        
        return allSuccess;
    }
    
    private static async Task AddDeltaDataToWorkbook(
        XLWorkbook workbook, 
        SqliteConnection connection, 
        string tableName, 
        DeltaExportResult result,
        SqliteToExcelOptions options)
    {
        // Execute the same query that was used for delta export to get the data
        var queryExecutor = new QueryExecutor();
        var parameterizedQuery = new ParameterizedSql(result.ExecutedQuery, result.QueryParameters);
        var rows = queryExecutor.ExecuteQuery(connection, parameterizedQuery).ToList();
        
        if (rows.Count == 0)
        {
            return; // No data to add
        }
        
        // Determine sheet name
        var baseSheetName = SanitizeSheetName(tableName);
        var deltaSheetName = baseSheetName;
        
        // Add delta strategy suffix
        deltaSheetName = result.Checkpoint.Strategy switch
        {
            DeltaStrategy.Watermark => $"{baseSheetName}_Delta",
            DeltaStrategy.ChangeLog => $"{baseSheetName}_Changes",
            DeltaStrategy.Full => baseSheetName,
            _ => $"{baseSheetName}_Delta"
        };
        
        // Ensure unique sheet name
        deltaSheetName = EnsureUniqueSheetName(workbook, deltaSheetName);
        
        var worksheet = workbook.Worksheets.Add(deltaSheetName);
        
        // Add headers
        var columnNames = rows[0].Keys.ToList();
        for (int i = 0; i < columnNames.Count; i++)
        {
            worksheet.Cell(1, i + 1).Value = columnNames[i];
            worksheet.Cell(1, i + 1).Style.Font.Bold = true;
        }
        
        // Add data rows
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            for (int colIndex = 0; colIndex < columnNames.Count; colIndex++)
            {
                var columnName = columnNames[colIndex];
                var value = row[columnName];
                
                if (value != null)
                {
                    if (options.WriteAllAsText)
                    {
                        worksheet.Cell(rowIndex + 2, colIndex + 1).Value = value.ToString();
                    }
                    else
                    {
                        var cell = worksheet.Cell(rowIndex + 2, colIndex + 1);
                        if (value is string str)
                            cell.Value = str;
                        else if (value is int intVal)
                            cell.Value = intVal;
                        else if (value is long longVal)
                            cell.Value = longVal;
                        else if (value is double doubleVal)
                            cell.Value = doubleVal;
                        else if (value is decimal decimalVal)
                            cell.Value = decimalVal;
                        else if (value is DateTime dateTimeVal)
                            cell.Value = dateTimeVal;
                        else if (value is bool boolVal)
                            cell.Value = boolVal;
                        else
                            cell.Value = value?.ToString() ?? "";
                    }
                }
            }
        }
        
        // Auto-fit columns
        worksheet.Columns().AdjustToContents();
    }
    
    private static void AddDeltaMetadataSheet(
        XLWorkbook workbook, 
        IReadOnlyList<(string tableName, DeltaExportResult result)> exportResults,
        SqliteToExcelOptions options)
    {
        var metadataSheet = workbook.Worksheets.Add(options.MetadataSheetName + "_Delta");
        
        // Headers
        var headers = new[]
        {
            "Table_Name", "Strategy", "Rows_Exported", "Has_More_Data", 
            "Checkpoint_ID", "Execution_Time_Ms", "Last_Watermark_Values", 
            "Last_ChangeLog_ID", "Total_Rows_Processed"
        };
        
        for (int i = 0; i < headers.Length; i++)
        {
            metadataSheet.Cell(1, i + 1).Value = headers[i];
            metadataSheet.Cell(1, i + 1).Style.Font.Bold = true;
        }
        
        // Data rows
        int rowIndex = 2;
        foreach (var (tableName, result) in exportResults)
        {
            metadataSheet.Cell(rowIndex, 1).Value = tableName;
            metadataSheet.Cell(rowIndex, 2).Value = result.Checkpoint.Strategy.ToString();
            metadataSheet.Cell(rowIndex, 3).Value = result.RowsExported;
            metadataSheet.Cell(rowIndex, 4).Value = result.HasMoreData;
            metadataSheet.Cell(rowIndex, 5).Value = result.Checkpoint.CheckpointId;
            metadataSheet.Cell(rowIndex, 6).Value = result.ElapsedTime.TotalMilliseconds;
            
            // Watermark values (JSON)
            if (result.Checkpoint.WatermarkValues.Count > 0)
            {
                var watermarkJson = System.Text.Json.JsonSerializer.Serialize(result.Checkpoint.WatermarkValues);
                metadataSheet.Cell(rowIndex, 7).Value = watermarkJson;
            }
            
            // Change log ID
            if (result.Checkpoint.LastChangeLogId.HasValue)
            {
                metadataSheet.Cell(rowIndex, 8).Value = result.Checkpoint.LastChangeLogId.Value;
            }
            
            metadataSheet.Cell(rowIndex, 9).Value = result.Checkpoint.RowsProcessed;
            
            rowIndex++;
        }
        
        // Auto-fit columns
        metadataSheet.Columns().AdjustToContents();
    }
    
    private static string SanitizeSheetName(string sheetName)
    {
        // Excel sheet name limitations: max 31 chars, no \ / ? * [ ] :
        var invalidChars = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var sanitized = sheetName;
        
        foreach (var invalidChar in invalidChars)
        {
            sanitized = sanitized.Replace(invalidChar, '_');
        }
        
        return sanitized.Length > 31 ? sanitized[..31] : sanitized;
    }
    
    private static string EnsureUniqueSheetName(XLWorkbook workbook, string baseName)
    {
        var name = baseName;
        var counter = 1;
        
        while (workbook.Worksheets.Any(ws => ws.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{baseName}_{counter}";
            counter++;
            
            // Ensure we don't exceed Excel's 31 character limit
            if (name.Length > 31)
            {
                var baseLength = 31 - $"_{counter}".Length;
                name = $"{baseName[..baseLength]}_{counter}";
            }
        }
        
        return name;
    }
}

/// <summary>
/// Builder for creating SqliteToExcelOptions with delta export configuration
/// </summary>
public static class DeltaExportOptionsBuilder
{
    /// <summary>
    /// Creates SqliteToExcelOptions configured for watermark-based delta exports
    /// </summary>
    public static SqliteToExcelOptions WithWatermarkDelta(
        string[] watermarkColumns,
        int? maxRows = null,
        bool includeMetadata = true)
    {
        return new SqliteToExcelOptions
        {
            DeltaExportConfig = new DeltaExportConfig
            {
                Strategy = DeltaStrategy.Watermark,
                WatermarkColumns = watermarkColumns,
                MaxRows = maxRows
            },
            IncludeDeltaMetadata = includeMetadata,
            IncludeMetadataSheet = includeMetadata
        };
    }
    
    /// <summary>
    /// Creates SqliteToExcelOptions configured for change log-based delta exports
    /// </summary>
    public static SqliteToExcelOptions WithChangeLogDelta(
        bool autoInstallTriggers = true,
        bool includeDeletes = true,
        bool includeMetadata = true,
        int? maxRows = null)
    {
        return new SqliteToExcelOptions
        {
            DeltaExportConfig = new DeltaExportConfig
            {
                Strategy = DeltaStrategy.ChangeLog,
                IncludeDeletes = includeDeletes,
                MaxRows = maxRows,
                ChangeLogConfig = new ChangeLogConfig
                {
                    AutoInstallTriggers = autoInstallTriggers,
                    CaptureFullRowData = false, // More efficient
                    RetentionDays = 30
                }
            },
            IncludeDeltaMetadata = includeMetadata,
            IncludeMetadataSheet = includeMetadata
        };
    }
    
    /// <summary>
    /// Creates SqliteToExcelOptions configured for full exports with delta tracking
    /// </summary>
    public static SqliteToExcelOptions WithFullExportAndTracking(
        bool includeMetadata = true)
    {
        return new SqliteToExcelOptions
        {
            DeltaExportConfig = new DeltaExportConfig
            {
                Strategy = DeltaStrategy.Full
            },
            IncludeDeltaMetadata = includeMetadata,
            IncludeMetadataSheet = includeMetadata
        };
    }
}