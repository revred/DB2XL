using DB2XL.Core.Models;
using DB2XL.Core.Services;
using DB2XL.Data.Schema;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DB2XL.Export.Bundle.Services;

/// <summary>
/// Implementation of MCP (Model Context Protocol) service for AI-friendly database operations.
/// Provides structured access to database export, preview, and analysis functionality.
/// </summary>
public sealed class McpExportService : IMcpExportService
{
    private readonly IBundleExportService _bundleExportService;
    private readonly IWatermarkDeltaExporter _watermarkExporter;
    private readonly IChangeLogDeltaExporter _changeLogExporter;
    private readonly IDeltaManifestManager _manifestManager;
    private readonly SqliteSchemaReader _schemaReader;

    public McpExportService(
        IBundleExportService bundleExportService,
        IWatermarkDeltaExporter watermarkExporter,
        IChangeLogDeltaExporter changeLogExporter,
        IDeltaManifestManager manifestManager)
    {
        _bundleExportService = bundleExportService ?? throw new ArgumentNullException(nameof(bundleExportService));
        _watermarkExporter = watermarkExporter ?? throw new ArgumentNullException(nameof(watermarkExporter));
        _changeLogExporter = changeLogExporter ?? throw new ArgumentNullException(nameof(changeLogExporter));
        _manifestManager = manifestManager ?? throw new ArgumentNullException(nameof(manifestManager));
        _schemaReader = new SqliteSchemaReader();
    }

    /// <inheritdoc />
    public async Task<McpPreviewResult> PreviewDatabaseAsync(McpPreviewRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();

        try
        {
            if (!File.Exists(request.DatabasePath))
            {
                errors.Add($"Database file not found: {request.DatabasePath}");
                return new McpPreviewResult
                {
                    IsSuccess = false,
                    Errors = errors.AsReadOnly(),
                    Duration = stopwatch.Elapsed
                };
            }

            var connectionString = $"Data Source={request.DatabasePath};Mode=ReadOnly";
            
            // Get database summary
            var summary = await GetDatabaseSummaryAsync(connectionString, request.DatabasePath);
            
            // Get table previews
            var tables = await GetTablePreviewsAsync(connectionString, request);
            
            // Get relationships if requested
            var relationships = request.IncludeRelationships 
                ? await GetRelationshipsAsync(connectionString)
                : Array.Empty<RelationshipInfo>();

            return new McpPreviewResult
            {
                IsSuccess = true,
                Summary = summary,
                Tables = tables,
                Relationships = relationships,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Preview failed: {ex.Message}");
            return new McpPreviewResult
            {
                IsSuccess = false,
                Errors = errors.AsReadOnly(),
                Duration = stopwatch.Elapsed
            };
        }
    }

    /// <inheritdoc />
    public async Task<McpExportResult> ExportDatabaseAsync(McpExportRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();

        try
        {
            if (!File.Exists(request.DatabasePath))
            {
                errors.Add($"Database file not found: {request.DatabasePath}");
                return new McpExportResult
                {
                    IsSuccess = false,
                    Errors = errors.AsReadOnly(),
                    Duration = stopwatch.Elapsed
                };
            }

            Directory.CreateDirectory(request.OutputDirectory);

            var bundleOptions = new BundleExportOptions
            {
                BundleRootPath = request.OutputDirectory,
                IncludeSamples = request.IncludeSamples,
                SampleRowLimit = request.SampleRowLimit,
                GenerateParquet = request.Format.Contains("parquet", StringComparison.OrdinalIgnoreCase)
            };

            var result = await _bundleExportService.ExportAsync(request.DatabasePath, bundleOptions);
            
            var exportedFiles = await CollectExportedFilesAsync(request.OutputDirectory);
            var statistics = CalculateExportStatistics(exportedFiles, result);

            // Generate AI-friendly manifest if requested
            string? manifestPath = null;
            if (request.GenerateManifest)
            {
                manifestPath = await GenerateAiManifestAsync(request.OutputDirectory, exportedFiles, statistics);
            }

            return new McpExportResult
            {
                IsSuccess = result.IsSuccess,
                ExportedFiles = exportedFiles,
                Statistics = statistics,
                ManifestPath = manifestPath,
                Errors = result.IsSuccess ? Array.Empty<string>() : new[] { "Export completed with warnings" },
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Export failed: {ex.Message}");
            return new McpExportResult
            {
                IsSuccess = false,
                Errors = errors.AsReadOnly(),
                Duration = stopwatch.Elapsed
            };
        }
    }

    /// <inheritdoc />
    public async Task<McpDeltaResult> ExportDeltaAsync(McpDeltaRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();

        try
        {
            if (!File.Exists(request.DatabasePath))
            {
                errors.Add($"Database file not found: {request.DatabasePath}");
                return new McpDeltaResult
                {
                    IsSuccess = false,
                    Errors = errors.AsReadOnly(),
                    Duration = stopwatch.Elapsed
                };
            }

            Directory.CreateDirectory(request.OutputDirectory);

            var connectionString = $"Data Source={request.DatabasePath};Mode=ReadOnly";
            var deltaFiles = new List<DeltaFileInfo>();
            var tablesWithChanges = new List<string>();
            long totalDeltaRows = 0;

            // Get tables to process
            var tablesToProcess = request.IncludeTables ?? await GetTableNamesAsync(connectionString);

            foreach (var tableName in tablesToProcess)
            {
                var deltaResult = await ExportTableDeltaAsync(
                    connectionString, 
                    tableName, 
                    request, 
                    request.OutputDirectory);

                if (deltaResult.IsSuccess && deltaResult.RowsExported > 0)
                {
                    tablesWithChanges.Add(tableName);
                    totalDeltaRows += deltaResult.RowsExported;
                    
                    // Create delta file info
                    foreach (var filePath in deltaResult.ExportedFiles)
                    {
                        var relativePath = Path.GetRelativePath(request.OutputDirectory, filePath);
                        var fileInfo = new FileInfo(filePath);
                        
                        deltaFiles.Add(new DeltaFileInfo
                        {
                            RelativePath = relativePath.Replace('\\', '/'),
                            TableName = tableName,
                            ChangedRows = deltaResult.RowsExported,
                            ChangeTypes = DetermineChangeTypes(deltaResult),
                            TimeRange = deltaResult.DataTimeRange,
                            FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0
                        });
                    }
                }
                else if (!deltaResult.IsSuccess)
                {
                    errors.AddRange(deltaResult.Errors);
                }
            }

            // Generate checkpoint information
            var checkpointInfo = await GenerateCheckpointInfoAsync(request);

            return new McpDeltaResult
            {
                IsSuccess = errors.Count == 0,
                DeltaFiles = deltaFiles.AsReadOnly(),
                CheckpointInfo = checkpointInfo,
                TablesWithChanges = tablesWithChanges.AsReadOnly(),
                TotalDeltaRows = totalDeltaRows,
                Errors = errors.AsReadOnly(),
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Delta export failed: {ex.Message}");
            return new McpDeltaResult
            {
                IsSuccess = false,
                Errors = errors.AsReadOnly(),
                Duration = stopwatch.Elapsed
            };
        }
    }

    /// <inheritdoc />
    public async Task<McpSchemaResult> GetSchemaAsync(McpSchemaRequest request)
    {
        try
        {
            if (!File.Exists(request.DatabasePath))
            {
                return new McpSchemaResult
                {
                    IsSuccess = false,
                    Errors = new[] { $"Database file not found: {request.DatabasePath}" }
                };
            }

            var connectionString = $"Data Source={request.DatabasePath};Mode=ReadOnly";
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            var schema = await _schemaReader.ReadSchemaAsync(connectionString);
            var databaseSchema = MapToMcpSchema(schema, request);

            return new McpSchemaResult
            {
                IsSuccess = true,
                Schema = databaseSchema
            };
        }
        catch (Exception ex)
        {
            return new McpSchemaResult
            {
                IsSuccess = false,
                Errors = new[] { $"Schema query failed: {ex.Message}" }
            };
        }
    }

    /// <inheritdoc />
    public async Task<McpQueryResult> ExecuteQueryAsync(McpQueryRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();

        try
        {
            if (!File.Exists(request.DatabasePath))
            {
                errors.Add($"Database file not found: {request.DatabasePath}");
                return new McpQueryResult
                {
                    IsSuccess = false,
                    Errors = errors.AsReadOnly(),
                    Duration = stopwatch.Elapsed
                };
            }

            // Validate query safety
            if (!IsQuerySafe(request.SqlQuery, request.AllowWrites))
            {
                errors.Add("Query contains unsafe operations or write operations are not allowed");
                return new McpQueryResult
                {
                    IsSuccess = false,
                    Errors = errors.AsReadOnly(),
                    Duration = stopwatch.Elapsed
                };
            }

            var connectionString = $"Data Source={request.DatabasePath};Mode={(request.AllowWrites ? "ReadWrite" : "ReadOnly")}";
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            using var command = new SqliteCommand(request.SqlQuery, connection);
            command.CommandTimeout = request.TimeoutSeconds;

            var rows = new List<Dictionary<string, object?>>();
            var columns = new List<QueryColumnInfo>();
            int rowsAffected = 0;
            bool isTruncated = false;

            using var reader = await command.ExecuteReaderAsync();
            
            // Build column information
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(new QueryColumnInfo
                {
                    Name = reader.GetName(i),
                    Type = reader.GetDataTypeName(i),
                    Position = i
                });
            }

            // Read data rows
            int rowCount = 0;
            while (await reader.ReadAsync() && rowCount < request.MaxRows)
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[reader.GetName(i)] = value;
                }
                rows.Add(row);
                rowCount++;
            }

            // Check if there are more rows (truncated)
            if (await reader.ReadAsync())
            {
                isTruncated = true;
            }

            if (!reader.HasRows && request.AllowWrites)
            {
                rowsAffected = reader.RecordsAffected;
            }

            return new McpQueryResult
            {
                IsSuccess = true,
                Rows = rows.AsReadOnly(),
                Columns = columns.AsReadOnly(),
                RowsAffected = rowsAffected,
                IsTruncated = isTruncated,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Query execution failed: {ex.Message}");
            return new McpQueryResult
            {
                IsSuccess = false,
                Errors = errors.AsReadOnly(),
                Duration = stopwatch.Elapsed
            };
        }
    }

    private async Task<DatabaseSummary> GetDatabaseSummaryAsync(string connectionString, string databasePath)
    {
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var fileInfo = new FileInfo(databasePath);
        
        // Get counts
        var tableCount = await GetCountAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'");
        var viewCount = await GetCountAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='view'");
        var indexCount = await GetCountAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%'");

        // Get SQLite version
        var sqliteVersion = await GetScalarAsync(connection, "SELECT sqlite_version()") ?? "Unknown";
        
        // Get schema version
        var schemaVersion = await GetCountAsync(connection, "PRAGMA schema_version");

        // Estimate total rows (approximate)
        long totalEstimatedRows = 0;
        var tables = await GetTableNamesAsync(connectionString);
        foreach (var table in tables)
        {
            try
            {
                var rowCount = await GetCountAsync(connection, $"SELECT COUNT(*) FROM \"{table.Replace("\"", "\"\"")}\"");
                totalEstimatedRows += rowCount;
            }
            catch
            {
                // Skip tables that can't be counted
            }
        }

        return new DatabaseSummary
        {
            FilePath = databasePath,
            FileSizeBytes = fileInfo.Length,
            TableCount = (int)tableCount,
            ViewCount = (int)viewCount,
            IndexCount = (int)indexCount,
            TotalEstimatedRows = totalEstimatedRows,
            SqliteVersion = sqliteVersion,
            SchemaVersion = schemaVersion,
            CreatedAt = fileInfo.CreationTimeUtc,
            LastModified = fileInfo.LastWriteTimeUtc
        };
    }

    private async Task<IReadOnlyList<TablePreview>> GetTablePreviewsAsync(string connectionString, McpPreviewRequest request)
    {
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var tablePreviews = new List<TablePreview>();
        var tablesToProcess = request.IncludeTables ?? await GetTableNamesAsync(connectionString);

        foreach (var tableName in tablesToProcess)
        {
            try
            {
                var preview = await CreateTablePreviewAsync(connection, tableName, request);
                tablePreviews.Add(preview);
            }
            catch
            {
                // Skip tables that can't be processed
            }
        }

        return tablePreviews.AsReadOnly();
    }

    private async Task<TablePreview> CreateTablePreviewAsync(SqliteConnection connection, string tableName, McpPreviewRequest request)
    {
        // Get table info
        var columns = await GetColumnInfoAsync(connection, tableName);
        var primaryKeys = columns.Where(c => c.Column.IsPrimaryKey).Select(c => c.Column.Name).ToList();
        
        // Get row count
        var estimatedRows = await GetCountAsync(connection, $"SELECT COUNT(*) FROM \"{tableName.Replace("\"", "\"\"")}\"");
        
        // Get sample data if requested
        var sampleData = new List<Dictionary<string, object?>>();
        if (request.IncludeSampleData && request.MaxPreviewRows > 0)
        {
            sampleData = await GetSampleDataAsync(connection, tableName, request.MaxPreviewRows);
        }

        // Analyze data patterns
        var dataPatterns = await AnalyzeTableDataPatternsAsync(connection, tableName, columns, sampleData);

        // Get CREATE SQL
        string? createSql = null;
        try
        {
            createSql = await GetScalarAsync(connection, $"SELECT sql FROM sqlite_master WHERE type='table' AND name='{tableName}'");
        }
        catch { }

        return new TablePreview
        {
            Name = tableName,
            Type = "table",
            Columns = columns,
            PrimaryKeys = primaryKeys.AsReadOnly(),
            EstimatedRows = estimatedRows,
            SampleData = sampleData.AsReadOnly(),
            CreateSql = createSql,
            DataPatterns = dataPatterns
        };
    }

    private async Task<IReadOnlyList<McpColumnPreview>> GetColumnInfoAsync(SqliteConnection connection, string tableName)
    {
        var columns = new List<McpColumnPreview>();
        
        using var command = new SqliteCommand($"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\");", connection);
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            var nameOrdinal = reader.GetOrdinal("name");
            var typeOrdinal = reader.GetOrdinal("type");
            var notNullOrdinal = reader.GetOrdinal("notnull");
            var pkOrdinal = reader.GetOrdinal("pk");
            var defaultOrdinal = reader.GetOrdinal("dflt_value");
            
            var columnName = reader.GetString(nameOrdinal);
            var dataType = reader.GetString(typeOrdinal);
            var notNull = reader.GetInt32(notNullOrdinal) == 1;
            var isPrimaryKey = reader.GetInt32(pkOrdinal) > 0;
            var defaultValue = reader.IsDBNull(defaultOrdinal) ? null : reader.GetString(defaultOrdinal);

            var columnInfo = new ColumnInfo(
                Name: columnName,
                Type: dataType,
                NotNull: notNull,
                DefaultValue: defaultValue,
                IsPrimaryKey: isPrimaryKey
            );

            columns.Add(new McpColumnPreview
            {
                Column = columnInfo,
                DataPatterns = new ColumnDataPatterns() // Will be populated by pattern analysis
            });
        }
        
        return columns.AsReadOnly();
    }

    private async Task<List<Dictionary<string, object?>>> GetSampleDataAsync(SqliteConnection connection, string tableName, int maxRows)
    {
        var sampleData = new List<Dictionary<string, object?>>();
        
        using var command = new SqliteCommand($"SELECT * FROM \"{tableName.Replace("\"", "\"\"")}\" LIMIT {maxRows};", connection);
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                row[reader.GetName(i)] = value;
            }
            sampleData.Add(row);
        }
        
        return sampleData;
    }

    private async Task<TableDataPatterns> AnalyzeTableDataPatternsAsync(
        SqliteConnection connection, 
        string tableName, 
        IReadOnlyList<McpColumnPreview> columns,
        List<Dictionary<string, object?>> sampleData)
    {
        var timestampColumns = new List<string>();
        var piiColumns = new List<string>();
        var jsonColumns = new List<string>();
        var partitionableColumns = new List<string>();
        var idColumns = new List<string>();

        foreach (var columnPreview in columns)
        {
            var column = columnPreview.Column;
            var columnName = column.Name.ToLowerInvariant();
            var columnType = column.Type.ToLowerInvariant();

            // Detect timestamp columns
            if (IsTimestampColumn(columnName, columnType))
            {
                timestampColumns.Add(column.Name);
                partitionableColumns.Add(column.Name);
            }

            // Detect potential PII
            if (IsPotentialPiiColumn(columnName))
            {
                piiColumns.Add(column.Name);
            }

            // Detect JSON columns
            if (IsJsonColumn(columnName, columnType, sampleData, column.Name))
            {
                jsonColumns.Add(column.Name);
            }

            // Detect ID columns
            if (IsIdColumn(columnName, column.IsPrimaryKey))
            {
                idColumns.Add(column.Name);
            }
        }

        return new TableDataPatterns
        {
            TimestampColumns = timestampColumns.AsReadOnly(),
            PotentialPiiColumns = piiColumns.AsReadOnly(),
            JsonColumns = jsonColumns.AsReadOnly(),
            PartitionableColumns = partitionableColumns.AsReadOnly(),
            IdColumns = idColumns.AsReadOnly()
        };
    }

    private static bool IsTimestampColumn(string columnName, string columnType)
    {
        var timestampIndicators = new[] { "time", "date", "created", "updated", "modified", "timestamp" };
        return timestampIndicators.Any(indicator => columnName.Contains(indicator)) ||
               columnType.Contains("datetime") || columnType.Contains("timestamp");
    }

    private static bool IsPotentialPiiColumn(string columnName)
    {
        var piiIndicators = new[] { "email", "phone", "ssn", "social", "name", "address", "zip", "postal" };
        return piiIndicators.Any(indicator => columnName.Contains(indicator));
    }

    private static bool IsJsonColumn(string columnName, string columnType, List<Dictionary<string, object?>> sampleData, string actualColumnName)
    {
        if (columnName.Contains("json") || columnType.Contains("json"))
            return true;

        // Check sample data for JSON patterns
        var sampleValues = sampleData
            .Select(row => row.GetValueOrDefault(actualColumnName)?.ToString())
            .Where(v => !string.IsNullOrEmpty(v))
            .Take(5);

        return sampleValues.Any(v => v!.TrimStart().StartsWith("{") || v.TrimStart().StartsWith("["));
    }

    private static bool IsIdColumn(string columnName, bool isPrimaryKey)
    {
        if (isPrimaryKey) return true;
        
        var idIndicators = new[] { "id", "_id", "key", "_key" };
        return idIndicators.Any(indicator => columnName.EndsWith(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyList<RelationshipInfo>> GetRelationshipsAsync(string connectionString)
    {
        // For now, return empty list - FK detection would require more complex analysis
        return Array.Empty<RelationshipInfo>();
    }

    private async Task<List<string>> GetTableNamesAsync(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        
        var tables = new List<string>();
        using var command = new SqliteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;", connection);
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }
        
        return tables;
    }

    private async Task<long> GetCountAsync(SqliteConnection connection, string sql)
    {
        using var command = new SqliteCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result ?? 0);
    }

    private async Task<string?> GetScalarAsync(SqliteConnection connection, string sql)
    {
        using var command = new SqliteCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return result?.ToString();
    }

    private async Task<DeltaExportResult> ExportTableDeltaAsync(
        string connectionString, 
        string tableName, 
        McpDeltaRequest request, 
        string outputDirectory)
    {
        var deltaMode = request.Strategy.Equals("changelog", StringComparison.OrdinalIgnoreCase)
            ? DeltaExportMode.ChangeLog
            : DeltaExportMode.Watermark;

        var selectionHash = GenerateSelectionHash(tableName, request.WatermarkColumn ?? "");
        var lastCheckpoint = await _manifestManager.GetLatestCheckpointAsync(
            outputDirectory, tableName, selectionHash, deltaMode);

        if (deltaMode == DeltaExportMode.Watermark)
        {
            var watermarkColumn = request.WatermarkColumn ?? "updated_at";
            var deltaOptions = new DeltaExportOptions
            {
                OutputDirectory = outputDirectory,
                Format = ExportFormat.Jsonl,
                FileNamePattern = $"{tableName}_delta_{{timestamp}}_{{sequence}}"
            };

            return await _watermarkExporter.ExportDeltaAsync(
                connectionString, tableName, watermarkColumn, lastCheckpoint, deltaOptions);
        }
        else
        {
            var changeLogOptions = new ChangeLogDeltaExportOptions
            {
                ChangeLogTableName = "__changes",
                OutputDirectory = outputDirectory,
                Format = ExportFormat.Jsonl,
                FileNamePattern = $"{tableName}_changelog_{{timestamp}}_{{sequence}}"
            };

            return await _changeLogExporter.ExportDeltaAsync(
                connectionString, tableName, lastCheckpoint, changeLogOptions);
        }
    }

    private static string GenerateSelectionHash(string tableName, string watermarkColumn)
    {
        using var sha256 = SHA256.Create();
        var input = $"{tableName}_{watermarkColumn}";
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hash)[..8];
    }

    private static IReadOnlyList<string> DetermineChangeTypes(DeltaExportResult deltaResult)
    {
        // This would analyze the actual delta data to determine change types
        // For now, return a default set
        return new[] { "INSERT", "UPDATE" };
    }

    private async Task<DeltaCheckpointInfo?> GenerateCheckpointInfoAsync(McpDeltaRequest request)
    {
        if (string.IsNullOrEmpty(request.CheckpointFile))
            return null;

        var checkpointPath = Path.Combine(request.OutputDirectory, "checkpoint.json");
        var watermarks = new Dictionary<string, object>();

        // This would collect actual watermark values from the delta exports
        // For now, create a basic checkpoint structure

        return new DeltaCheckpointInfo
        {
            CheckpointPath = checkpointPath,
            LastWatermarks = watermarks,
            TotalRowsProcessed = 0,
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task<IReadOnlyList<ExportedFileInfo>> CollectExportedFilesAsync(string outputDirectory)
    {
        var exportedFiles = new List<ExportedFileInfo>();
        
        if (!Directory.Exists(outputDirectory))
            return exportedFiles;

        foreach (var filePath in Directory.GetFiles(outputDirectory, "*", SearchOption.AllDirectories))
        {
            var fileInfo = new FileInfo(filePath);
            var relativePath = Path.GetRelativePath(outputDirectory, filePath);
            
            var hash = await ComputeFileHashAsync(filePath);
            
            exportedFiles.Add(new ExportedFileInfo
            {
                RelativePath = relativePath.Replace('\\', '/'),
                FullPath = filePath,
                TableName = ExtractTableNameFromPath(relativePath),
                Format = Path.GetExtension(filePath).TrimStart('.'),
                RowCount = await EstimateRowCountAsync(filePath),
                FileSizeBytes = fileInfo.Length,
                Sha256Hash = hash,
                IsSample = relativePath.Contains("sample", StringComparison.OrdinalIgnoreCase),
                CreatedAt = fileInfo.CreationTimeUtc
            });
        }
        
        return exportedFiles.AsReadOnly();
    }

    private static string ExtractTableNameFromPath(string relativePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        // Extract table name from patterns like "table_name.jsonl" or "table_name_part_001.jsonl"
        var parts = fileName.Split('_');
        return parts.Length > 0 ? parts[0] : fileName;
    }

    private async Task<long> EstimateRowCountAsync(string filePath)
    {
        try
        {
            if (Path.GetExtension(filePath).Equals(".jsonl", StringComparison.OrdinalIgnoreCase))
            {
                return File.ReadLines(filePath).Count();
            }
        }
        catch
        {
            // Ignore errors in row count estimation
        }
        
        return 0;
    }

    private async Task<string> ComputeFileHashAsync(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            var hash = await sha256.ComputeHashAsync(stream);
            return Convert.ToBase64String(hash);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static ExportStatistics CalculateExportStatistics(IReadOnlyList<ExportedFileInfo> exportedFiles, BundleExportResult bundleResult)
    {
        var tableGroups = exportedFiles.GroupBy(f => f.TableName).ToList();
        var largestTable = tableGroups.MaxBy(g => g.Sum(f => f.RowCount));

        return new ExportStatistics
        {
            TablesExported = tableGroups.Count,
            TotalRowsExported = exportedFiles.Sum(f => f.RowCount),
            FilesCreated = exportedFiles.Count,
            TotalSizeBytes = exportedFiles.Sum(f => f.FileSizeBytes),
            PiiColumnsRedacted = 0, // Would be calculated from PII processing
            AverageRowsPerTable = tableGroups.Count > 0 ? (double)exportedFiles.Sum(f => f.RowCount) / tableGroups.Count : 0,
            LargestTableName = largestTable?.Key,
            LargestTableRows = largestTable?.Sum(f => f.RowCount) ?? 0
        };
    }

    private async Task<string> GenerateAiManifestAsync(string outputDirectory, IReadOnlyList<ExportedFileInfo> exportedFiles, ExportStatistics statistics)
    {
        var manifestPath = Path.Combine(outputDirectory, "ai_manifest.json");
        
        var manifest = new
        {
            generated_at = DateTime.UtcNow,
            generator = "DB2XL MCP Service",
            version = "1.0",
            statistics,
            files = exportedFiles.Select(f => new
            {
                path = f.RelativePath,
                table = f.TableName,
                format = f.Format,
                rows = f.RowCount,
                size_bytes = f.FileSizeBytes,
                sha256 = f.Sha256Hash,
                is_sample = f.IsSample
            }).ToArray()
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        await File.WriteAllTextAsync(manifestPath, json);
        return manifestPath;
    }

    private static DatabaseSchema MapToMcpSchema(DatabaseInfo schema, McpSchemaRequest request)
    {
        var tables = schema.Tables.Select(t => new TableSchema
        {
            Name = t.Name,
            Columns = t.Columns.Select((c, idx) => new ColumnSchema
            {
                Name = c.Name,
                Type = c.DataType,
                IsNullable = c.IsNullable,
                IsPrimaryKey = c.IsPrimaryKey,
                DefaultValue = c.DefaultValue,
                Position = idx
            }).ToList(),
            CreateSql = request.IncludeCreateSql ? t.CreateStatement : null,
            WithoutRowId = false // Would need to be detected from CREATE statement
        }).ToList();

        return new DatabaseSchema
        {
            DatabasePath = string.Empty, // Would be set from request
            Tables = tables,
            Views = Array.Empty<ViewSchema>(),
            Indexes = Array.Empty<IndexSchema>(),
            ForeignKeys = Array.Empty<ForeignKeySchema>()
        };
    }

    private static bool IsQuerySafe(string sql, bool allowWrites)
    {
        var normalizedSql = sql.Trim().ToUpperInvariant();
        
        // Block dangerous operations
        var dangerousPatterns = new[]
        {
            "DROP ",
            "TRUNCATE ",
            "ALTER ",
            "CREATE ",
            "PRAGMA ",
            "ATTACH ",
            "DETACH "
        };

        if (dangerousPatterns.Any(pattern => normalizedSql.Contains(pattern)))
            return false;

        // Block write operations if not allowed
        if (!allowWrites)
        {
            var writePatterns = new[] { "INSERT ", "UPDATE ", "DELETE " };
            if (writePatterns.Any(pattern => normalizedSql.Contains(pattern)))
                return false;
        }

        return true;
    }
}