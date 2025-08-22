using DB2XL;
using DB2XL.Core.Models;
using DB2XL.Core.Services;
using DB2XL.Data.Schema;
using DB2XL.Export.Excel;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DB2XL.Console.Services;

/// <summary>
/// Simplified MCP export service implementation for DB2XL console.
/// Provides core database export and query functionality for AI assistants.
/// </summary>
public sealed class SimpleMcpExportService : IMcpExportService
{
    private readonly JsonSerializerOptions _jsonOptions;

    public SimpleMcpExportService()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task<McpPreviewResult> PreviewDatabaseAsync(McpPreviewRequest request)
    {
        var startTime = DateTime.UtcNow;
        var errors = new List<string>();

        try
        {
            if (!File.Exists(request.DatabasePath))
            {
                return new McpPreviewResult
                {
                    IsSuccess = false,
                    Errors = new[] { $"Database file not found: {request.DatabasePath}" },
                    Duration = DateTime.UtcNow - startTime
                };
            }

            var fileInfo = new FileInfo(request.DatabasePath);
            using var connection = new SqliteConnection($"Data Source={request.DatabasePath};Mode=ReadOnly");
            await connection.OpenAsync();

            // Get database summary
            var summary = new DatabaseSummary
            {
                FilePath = request.DatabasePath,
                FileSizeBytes = fileInfo.Length,
                LastModified = fileInfo.LastWriteTime,
                SqliteVersion = connection.ServerVersion ?? "Unknown"
            };

            // Get schema information
            var dbObjects = SqliteSchemaReader.GetDatabaseObjects(connection, null, true);
            summary = summary with
            {
                TableCount = dbObjects.Count(t => t.Type == "table"),
                IndexCount = 0 // Would need separate query for indexes
            };

            // Get table previews
            var tables = new List<TablePreview>();
            foreach (var tableInfo in dbObjects.Where(t => t.Type == "table"))
            {
                if (request.IncludeTables?.Count > 0 && !request.IncludeTables.Contains(tableInfo.Name))
                    continue;

                var preview = await GetTablePreviewAsync(connection, tableInfo, request.MaxPreviewRows, request.IncludeSampleData);
                tables.Add(preview);
                summary = summary with { TotalEstimatedRows = summary.TotalEstimatedRows + preview.EstimatedRows };
            }

            // Get relationships if requested
            var relationships = new List<RelationshipInfo>();
            if (request.IncludeRelationships)
            {
                // Would need to query foreign key information separately
            }

            return new McpPreviewResult
            {
                IsSuccess = true,
                Summary = summary,
                Tables = tables,
                Relationships = relationships,
                Errors = errors,
                Duration = DateTime.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Error during preview: {ex.Message}");
            return new McpPreviewResult
            {
                IsSuccess = false,
                Errors = errors,
                Duration = DateTime.UtcNow - startTime
            };
        }
    }

    public async Task<McpExportResult> ExportDatabaseAsync(McpExportRequest request)
    {
        var startTime = DateTime.UtcNow;
        var errors = new List<string>();
        var exportedFiles = new List<ExportedFileInfo>();

        try
        {
            if (!File.Exists(request.DatabasePath))
            {
                return new McpExportResult
                {
                    IsSuccess = false,
                    Errors = new[] { $"Database file not found: {request.DatabasePath}" },
                    Duration = DateTime.UtcNow - startTime
                };
            }

            Directory.CreateDirectory(request.OutputDirectory);

            var format = request.Format.ToLowerInvariant();
            
            using var connection = new SqliteConnection($"Data Source={request.DatabasePath};Mode=ReadOnly");
            await connection.OpenAsync();
            var dbObjects = SqliteSchemaReader.GetDatabaseObjects(connection, null, false);
            var statistics = new ExportStatistics { TablesExported = 0 };

            foreach (var table in dbObjects)
            {
                if (request.IncludeTables?.Count > 0 && !request.IncludeTables.Contains(table.Name))
                    continue;

                try
                {
                    var fileInfo = format switch
                    {
                        "jsonl" => await ExportTableToJsonlAsync(request.DatabasePath, request.OutputDirectory, table, request.SampleRowLimit),
                        "excel" => await ExportTableToExcelAsync(request.DatabasePath, request.OutputDirectory, table, request.SampleRowLimit),
                        _ => await ExportTableToJsonlAsync(request.DatabasePath, request.OutputDirectory, table, request.SampleRowLimit)
                    };

                    if (fileInfo != null)
                    {
                        exportedFiles.Add(fileInfo);
                        statistics = statistics with
                        {
                            TablesExported = statistics.TablesExported + 1,
                            TotalRowsExported = statistics.TotalRowsExported + fileInfo.RowCount,
                            FilesCreated = statistics.FilesCreated + 1,
                            TotalSizeBytes = statistics.TotalSizeBytes + fileInfo.FileSizeBytes
                        };
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Error exporting table {table.Name}: {ex.Message}");
                }
            }

            // Generate manifest if requested
            string? manifestPath = null;
            if (request.GenerateManifest)
            {
                manifestPath = await GenerateManifestAsync(request.OutputDirectory, exportedFiles, dbObjects);
            }

            return new McpExportResult
            {
                IsSuccess = errors.Count == 0,
                ExportedFiles = exportedFiles,
                Statistics = statistics,
                ManifestPath = manifestPath,
                Errors = errors,
                Duration = DateTime.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Export failed: {ex.Message}");
            return new McpExportResult
            {
                IsSuccess = false,
                Errors = errors,
                Duration = DateTime.UtcNow - startTime
            };
        }
    }

    public Task<McpDeltaResult> ExportDeltaAsync(McpDeltaRequest request)
    {
        // Simplified delta export - for now just export full tables
        var startTime = DateTime.UtcNow;
        
        return Task.FromResult(new McpDeltaResult
        {
            IsSuccess = false,
            Errors = new[] { "Delta export not yet implemented in simplified MCP server" },
            Duration = DateTime.UtcNow - startTime
        });
    }

    public async Task<McpSchemaResult> GetSchemaAsync(McpSchemaRequest request)
    {
        var startTime = DateTime.UtcNow;
        var errors = new List<string>();

        try
        {
            if (!File.Exists(request.DatabasePath))
            {
                return new McpSchemaResult
                {
                    IsSuccess = false,
                    Errors = new[] { $"Database file not found: {request.DatabasePath}" },
                };
            }

            using var connection = new SqliteConnection($"Data Source={request.DatabasePath};Mode=ReadOnly");
            await connection.OpenAsync();
            var dbObjects = SqliteSchemaReader.GetDatabaseObjects(connection, null, true);
            
            var dbSchema = new DatabaseSchema
            {
                DatabasePath = request.DatabasePath,
                Tables = dbObjects.Where(t => t.Type == "table").Select(t => new TableSchema
                {
                    Name = t.Name,
                    Columns = SqliteSchemaReader.GetTableColumns(connection, t.Name).Select((c, idx) => new ColumnSchema
                    {
                        Name = c.Name,
                        Type = c.Type,
                        IsNullable = !c.NotNull,
                        IsPrimaryKey = c.IsPrimaryKey,
                        DefaultValue = c.DefaultValue?.ToString(),
                        Position = idx
                    }).ToList(),
                    CreateSql = request.IncludeCreateSql ? GetTableCreateSql(connection, t.Name) : null,
                    WithoutRowId = false // Would need to check CREATE SQL
                }).ToList(),
                Views = dbObjects.Where(t => t.Type == "view").Select(v => new ViewSchema
                {
                    Name = v.Name,
                    CreateSql = GetTableCreateSql(connection, v.Name) ?? string.Empty
                }).ToList(),
                Indexes = Array.Empty<IndexSchema>(), // Would need separate query
                ForeignKeys = Array.Empty<ForeignKeySchema>() // Would need separate query
            };

            return new McpSchemaResult
            {
                IsSuccess = true,
                Schema = dbSchema,
                Errors = errors
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Schema query failed: {ex.Message}");
            return new McpSchemaResult
            {
                IsSuccess = false,
                Errors = errors
            };
        }
    }

    public async Task<McpQueryResult> ExecuteQueryAsync(McpQueryRequest request)
    {
        var startTime = DateTime.UtcNow;
        var errors = new List<string>();

        try
        {
            if (!File.Exists(request.DatabasePath))
            {
                return new McpQueryResult
                {
                    IsSuccess = false,
                    Errors = new[] { $"Database file not found: {request.DatabasePath}" },
                    Duration = DateTime.UtcNow - startTime
                };
            }

            // Check if query is read-only
            var queryUpper = request.SqlQuery.ToUpperInvariant();
            var isWriteQuery = queryUpper.Contains("INSERT") || queryUpper.Contains("UPDATE") || 
                               queryUpper.Contains("DELETE") || queryUpper.Contains("DROP") || 
                               queryUpper.Contains("CREATE") || queryUpper.Contains("ALTER");

            if (isWriteQuery && !request.AllowWrites)
            {
                return new McpQueryResult
                {
                    IsSuccess = false,
                    Errors = new[] { "Write operations are not allowed. Set AllowWrites=true to enable." },
                    Duration = DateTime.UtcNow - startTime
                };
            }

            var mode = request.AllowWrites ? "ReadWrite" : "ReadOnly";
            using var connection = new SqliteConnection($"Data Source={request.DatabasePath};Mode={mode}");
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = request.SqlQuery;
            command.CommandTimeout = request.TimeoutSeconds;

            var rows = new List<Dictionary<string, object?>>();
            var columns = new List<QueryColumnInfo>();

            using var reader = await command.ExecuteReaderAsync();
            
            // Get column information
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(new QueryColumnInfo
                {
                    Name = reader.GetName(i),
                    Type = reader.GetDataTypeName(i),
                    Position = i
                });
            }

            // Read rows
            int rowCount = 0;
            while (await reader.ReadAsync() && rowCount < request.MaxRows)
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                rows.Add(row);
                rowCount++;
            }

            bool isTruncated = rowCount >= request.MaxRows && await reader.ReadAsync();

            return new McpQueryResult
            {
                IsSuccess = true,
                Rows = rows,
                Columns = columns,
                RowsAffected = reader.RecordsAffected,
                IsTruncated = isTruncated,
                Duration = DateTime.UtcNow - startTime,
                Errors = errors
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Query execution failed: {ex.Message}");
            return new McpQueryResult
            {
                IsSuccess = false,
                Errors = errors,
                Duration = DateTime.UtcNow - startTime
            };
        }
    }

    private string? GetTableCreateSql(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE name = @name";
        command.Parameters.AddWithValue("@name", tableName);
        return command.ExecuteScalar()?.ToString();
    }

    private async Task<TablePreview> GetTablePreviewAsync(SqliteConnection connection, TableInfo tableInfo, int maxRows, bool includeSampleData)
    {
        var columns = SqliteSchemaReader.GetTableColumns(connection, tableInfo.Name);
        var preview = new TablePreview
        {
            Name = tableInfo.Name,
            Type = "table",
            Columns = columns.Select(c => new McpColumnPreview
            {
                Column = c,
                DataPatterns = new ColumnDataPatterns()
            }).ToList(),
            PrimaryKeys = columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList(),
            CreateSql = GetTableCreateSql(connection, tableInfo.Name)
        };

        // Get row count estimate
        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM [{tableInfo.Name}]";
        preview = preview with { EstimatedRows = Convert.ToInt64(await countCmd.ExecuteScalarAsync() ?? 0) };

        // Get sample data if requested
        if (includeSampleData && maxRows > 0)
        {
            var sampleData = new List<Dictionary<string, object?>>();
            using var sampleCmd = connection.CreateCommand();
            sampleCmd.CommandText = $"SELECT * FROM [{tableInfo.Name}] LIMIT {maxRows}";
            
            using var reader = await sampleCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                sampleData.Add(row);
            }
            
            preview = preview with { SampleData = sampleData };
        }

        return preview;
    }

    private async Task<ExportedFileInfo?> ExportTableToJsonlAsync(string dbPath, string outputDir, TableInfo table, int maxRows)
    {
        var fileName = $"{table.Name}.jsonl";
        var filePath = Path.Combine(outputDir, fileName);
        
        var options = new JsonLinesExportOptions
        {
            TableNameLikeFilter = table.Name,
            WriteAllAsStrings = false
        };

        // Use static method
        await Task.Run(() => JsonLinesExporter.Export(dbPath, outputDir, options));

        // Check if file was created
        if (File.Exists(filePath))
        {
            var fileInfo = new FileInfo(filePath);
            
            // Count lines in file for row count
            var rowCount = File.ReadLines(filePath).Count();
            
            return new ExportedFileInfo
            {
                RelativePath = fileName,
                FullPath = filePath,
                TableName = table.Name,
                Format = "jsonl",
                RowCount = rowCount,
                FileSizeBytes = fileInfo.Length,
                Sha256Hash = await ComputeFileHashAsync(filePath),
                IsSample = maxRows > 0 && rowCount >= maxRows,
                CreatedAt = DateTime.UtcNow
            };
        }

        return null;
    }

    private async Task<ExportedFileInfo?> ExportTableToExcelAsync(string dbPath, string outputDir, TableInfo table, int maxRows)
    {
        var fileName = $"{table.Name}.xlsx";
        var filePath = Path.Combine(outputDir, fileName);
        
        var options = new SqliteToExcelOptions
        {
            TableNameLikeFilter = table.Name,
            ReadBatchSize = maxRows > 0 ? Math.Min(maxRows, 10000) : 10000
        };

        SqliteToExcel.Export(dbPath, filePath, options);

        var fileInfo = new FileInfo(filePath);
        return new ExportedFileInfo
        {
            RelativePath = fileName,
            FullPath = filePath,
            TableName = table.Name,
            Format = "excel",
            RowCount = 0, // Would need to read the Excel file to get actual count
            FileSizeBytes = fileInfo.Length,
            Sha256Hash = await ComputeFileHashAsync(filePath),
            IsSample = false,
            CreatedAt = DateTime.UtcNow
        };
    }

    private async Task<string> GenerateManifestAsync(string outputDir, IReadOnlyList<ExportedFileInfo> files, List<TableInfo> dbObjects)
    {
        var manifestPath = Path.Combine(outputDir, "manifest.json");
        
        var manifest = new
        {
            generatedAt = DateTime.UtcNow,
            version = "1.0.0",
            database = new
            {
                tables = dbObjects.Count(t => t.Type == "table"),
                views = dbObjects.Count(t => t.Type == "view")
            },
            files = files.Select(f => new
            {
                f.RelativePath,
                f.TableName,
                f.Format,
                f.RowCount,
                f.FileSizeBytes,
                f.Sha256Hash,
                f.IsSample
            })
        };

        var json = JsonSerializer.Serialize(manifest, _jsonOptions);
        await File.WriteAllTextAsync(manifestPath, json);
        
        return manifestPath;
    }

    private async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private IReadOnlyList<string> ParseIndexColumns(string? createSql)
    {
        if (string.IsNullOrEmpty(createSql))
            return Array.Empty<string>();

        // Simple parser for index columns from CREATE INDEX statement
        var start = createSql.IndexOf('(');
        var end = createSql.LastIndexOf(')');
        
        if (start > 0 && end > start)
        {
            var columnsPart = createSql.Substring(start + 1, end - start - 1);
            return columnsPart.Split(',').Select(c => c.Trim().Trim('"', '[', ']')).ToList();
        }

        return Array.Empty<string>();
    }
}