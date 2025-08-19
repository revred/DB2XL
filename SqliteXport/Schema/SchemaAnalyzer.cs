using Microsoft.Data.Sqlite;
using System.Text.Json;
using DB2XL.Configuration;
using DB2XL.Transformers;

namespace DB2XL.Schema;

/// <summary>
/// Analyzes database schemas and generates comprehensive metadata
/// </summary>
public static class SchemaAnalyzer
{
    /// <summary>
    /// Generates comprehensive schema information for a database
    /// </summary>
    public static DatabaseSchema AnalyzeDatabase(SqliteConnection connection, 
        string databasePath, 
        SqliteToExcelOptions? options = null,
        TransformationPipeline? transformationPipeline = null)
    {
        options ??= new SqliteToExcelOptions();
        
        var schema = new DatabaseSchema
        {
            DatabasePath = databasePath,
            AnalysisTimestamp = DateTime.UtcNow,
            SchemaVersion = GetPragmaValue(connection, "schema_version"),
            UserVersion = GetPragmaValue(connection, "user_version"),
            JournalMode = GetPragmaValue(connection, "journal_mode"),
            ForeignKeysEnabled = GetPragmaValue(connection, "foreign_keys") == "1",
            PageSize = long.Parse(GetPragmaValue(connection, "page_size")),
            PageCount = long.Parse(GetPragmaValue(connection, "page_count")),
            TransformationsEnabled = transformationPipeline?.AreTransformationsEnabled ?? false,
            TransformationErrors = transformationPipeline?.ErrorCount ?? 0
        };

        // Get file information
        if (File.Exists(databasePath))
        {
            var fileInfo = new FileInfo(databasePath);
            schema.FileSizeBytes = fileInfo.Length;
            schema.LastModifiedUtc = fileInfo.LastWriteTimeUtc;
        }

        // Analyze tables and views
        var tables = DatabaseDiscovery.GetObjects(connection, options.TableNameLikeFilter, options.IncludeViews);
        
        foreach (var table in tables)
        {
            var tableSchema = AnalyzeTable(connection, table, options, transformationPipeline);
            schema.Tables.Add(tableSchema);
        }

        // Generate global statistics
        schema.TotalTables = schema.Tables.Count(t => t.Type == "table");
        schema.TotalViews = schema.Tables.Count(t => t.Type == "view");
        schema.TotalRows = schema.Tables.Sum(t => t.RowCount);
        schema.TotalColumns = schema.Tables.Sum(t => t.Columns.Count);

        return schema;
    }

    /// <summary>
    /// Analyzes a single table/view schema
    /// </summary>
    internal static TableSchema AnalyzeTable(SqliteConnection connection, 
        TableInfo table, 
        SqliteToExcelOptions options,
        TransformationPipeline? transformationPipeline = null)
    {
        var columns = DatabaseDiscovery.GetColumns(connection, table.Name);
        var orderInfo = DatabaseDiscovery.DetermineOrder(connection, table.Name, columns);
        
        var tableSchema = new TableSchema
        {
            Name = table.Name,
            Type = table.Type,
            OrderMode = orderInfo.Mode.ToString(),
            OrderColumns = orderInfo.Columns.ToList(),
            AnalysisTimestamp = DateTime.UtcNow
        };

        // Count rows efficiently
        using var countCmd = connection.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM {SqlHelpers.Q(table.Name)}";
        tableSchema.RowCount = Convert.ToInt64(countCmd.ExecuteScalar() ?? 0);

        // Analyze columns
        foreach (var col in columns)
        {
            var columnSchema = AnalyzeColumn(connection, table.Name, col, transformationPipeline);
            tableSchema.Columns.Add(columnSchema);
        }

        // Generate table-level checksum for verification
        tableSchema.SchemaChecksum = GenerateTableSchemaChecksum(tableSchema);

        return tableSchema;
    }

    /// <summary>
    /// Analyzes a single column's metadata and statistics
    /// </summary>
    internal static ColumnSchema AnalyzeColumn(SqliteConnection connection, 
        string tableName, 
        Col column,
        TransformationPipeline? transformationPipeline = null)
    {
        var columnSchema = new ColumnSchema
        {
            Name = column.Name,
            Type = column.Type,
            NotNull = column.NotNull,
            DefaultValue = column.DefaultValue?.ToString(),
            IsPrimaryKey = column.IsPrimaryKey,
            AnalysisTimestamp = DateTime.UtcNow
        };

        // Check if column will be excluded by transformations
        if (transformationPipeline?.IsColumnExcluded(tableName, column.Name) == true)
        {
            columnSchema.ExcludedByTransformation = true;
            return columnSchema;
        }

        try
        {
            // Get column statistics
            using var cmd = connection.CreateCommand();
            
            // Count nulls
            cmd.CommandText = $"SELECT COUNT(*) FROM {SqlHelpers.Q(tableName)} WHERE {SqlHelpers.Q(column.Name)} IS NULL";
            columnSchema.NullCount = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            
            // Count non-nulls
            cmd.CommandText = $"SELECT COUNT(*) FROM {SqlHelpers.Q(tableName)} WHERE {SqlHelpers.Q(column.Name)} IS NOT NULL";
            columnSchema.NonNullCount = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
            
            // Get distinct count (limited to avoid performance issues)
            cmd.CommandText = $"SELECT COUNT(DISTINCT {SqlHelpers.Q(column.Name)}) FROM {SqlHelpers.Q(tableName)} LIMIT 10000";
            columnSchema.DistinctCount = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);

            // For text columns, get length statistics
            if (column.Type.ToUpperInvariant().Contains("TEXT") || column.Type.ToUpperInvariant().Contains("VARCHAR"))
            {
                cmd.CommandText = $@"
                    SELECT 
                        MIN(LENGTH({SqlHelpers.Q(column.Name)})) as MinLength,
                        MAX(LENGTH({SqlHelpers.Q(column.Name)})) as MaxLength,
                        AVG(LENGTH({SqlHelpers.Q(column.Name)})) as AvgLength
                    FROM {SqlHelpers.Q(tableName)} 
                    WHERE {SqlHelpers.Q(column.Name)} IS NOT NULL";
                
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    columnSchema.MinLength = reader.IsDBNull(0) ? null : reader.GetInt64(0);
                    columnSchema.MaxLength = reader.IsDBNull(1) ? null : reader.GetInt64(1);
                    columnSchema.AvgLength = reader.IsDBNull(2) ? null : reader.GetDouble(2);
                }
            }

            // For numeric columns, get numeric statistics
            if (IsNumericType(column.Type))
            {
                cmd.CommandText = $@"
                    SELECT 
                        MIN({SqlHelpers.Q(column.Name)}) as MinValue,
                        MAX({SqlHelpers.Q(column.Name)}) as MaxValue,
                        AVG({SqlHelpers.Q(column.Name)}) as AvgValue
                    FROM {SqlHelpers.Q(tableName)} 
                    WHERE {SqlHelpers.Q(column.Name)} IS NOT NULL";
                
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    columnSchema.MinValue = reader.IsDBNull(0) ? null : reader.GetValue(0)?.ToString();
                    columnSchema.MaxValue = reader.IsDBNull(1) ? null : reader.GetValue(1)?.ToString();
                    columnSchema.AvgValue = reader.IsDBNull(2) ? null : reader.GetDouble(2);
                }
            }

            // Check for transformation impact
            if (transformationPipeline != null && transformationPipeline.AreTransformationsEnabled)
            {
                // Simple heuristic: assume transformations might apply if column is not excluded
                // In a future enhancement, we could extract transformer lists from the pipeline
                columnSchema.HasTransformations = !transformationPipeline.IsColumnExcluded(tableName, column.Name);
                
                if (columnSchema.HasTransformations)
                {
                    // We can't easily get specific transformer names without refactoring TransformationPipeline
                    // For now, we'll indicate that transformations may apply
                    columnSchema.TransformerNames = new List<string> { "Unknown" };
                }
            }
        }
        catch (Exception ex)
        {
            // If statistics gathering fails, record the error but continue
            columnSchema.AnalysisError = ex.Message;
        }

        return columnSchema;
    }

    /// <summary>
    /// Generates provenance manifest tracking data lineage and transformations
    /// </summary>
    public static ProvenanceManifest GenerateProvenanceManifest(
        string sourceDatabasePath,
        DatabaseSchema schema,
        TransformationPipeline? transformationPipeline = null,
        string? exportPath = null,
        string? exportFormat = null)
    {
        var manifest = new ProvenanceManifest
        {
            GeneratedTimestamp = DateTime.UtcNow,
            SourceDatabase = sourceDatabasePath,
            ExportPath = exportPath,
            ExportFormat = exportFormat ?? "Unknown",
            SchemaVersion = schema.SchemaVersion,
            UserVersion = schema.UserVersion,
            DatabaseChecksum = CalculateDatabaseChecksum(schema),
            ExportToolVersion = typeof(SqliteToExcel).Assembly.GetName().Version?.ToString() ?? "1.0.0"
        };

        // Add transformation lineage
        if (transformationPipeline != null)
        {
            manifest.TransformationsApplied = transformationPipeline.AreTransformationsEnabled;
            manifest.TransformationErrors = transformationPipeline.ErrorCount;
            manifest.TransformationConfigVersion = transformationPipeline.Configuration.Version;
            manifest.ErrorHandlingStrategy = transformationPipeline.Configuration.Global.ErrorHandling.ToString();

            // Document transformation lineage per table
            foreach (var tableSchema in schema.Tables)
            {
                var lineage = new DataLineage
                {
                    TableName = tableSchema.Name,
                    SourceRowCount = tableSchema.RowCount,
                    OriginalColumns = tableSchema.Columns.Select(c => c.Name).ToList(),
                    ExcludedColumns = tableSchema.Columns.Where(c => c.ExcludedByTransformation).Select(c => c.Name).ToList(),
                    TransformedColumns = tableSchema.Columns.Where(c => c.HasTransformations).Select(c => c.Name).ToList()
                };

                // Add transformer details
                foreach (var col in tableSchema.Columns.Where(c => c.HasTransformations))
                {
                    var transformDetail = new TransformationDetail
                    {
                        ColumnName = col.Name,
                        OriginalType = col.Type,
                        TransformerNames = col.TransformerNames ?? new List<string>()
                    };
                    lineage.TransformationDetails.Add(transformDetail);
                }

                manifest.DataLineages.Add(lineage);
            }
        }

        return manifest;
    }

    private static string GetPragmaValue(SqliteConnection connection, string pragmaName)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"PRAGMA {pragmaName};";
            return cmd.ExecuteScalar()?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool IsNumericType(string sqliteType)
    {
        var upperType = sqliteType.ToUpperInvariant();
        return upperType.Contains("INT") || 
               upperType.Contains("REAL") || 
               upperType.Contains("FLOAT") || 
               upperType.Contains("DOUBLE") || 
               upperType.Contains("NUMERIC") || 
               upperType.Contains("DECIMAL");
    }

    private static string GenerateTableSchemaChecksum(TableSchema tableSchema)
    {
        var data = JsonSerializer.Serialize(new
        {
            tableSchema.Name,
            tableSchema.Type,
            Columns = tableSchema.Columns.Select(c => new
            {
                c.Name,
                c.Type,
                c.NotNull,
                c.DefaultValue,
                c.IsPrimaryKey
            }).OrderBy(c => c.Name)
        });
        
        return ComputeSha256(data);
    }

    private static string CalculateDatabaseChecksum(DatabaseSchema schema)
    {
        var data = JsonSerializer.Serialize(new
        {
            schema.SchemaVersion,
            schema.UserVersion,
            Tables = schema.Tables.Select(t => new
            {
                t.Name,
                t.Type,
                t.SchemaChecksum
            }).OrderBy(t => t.Name)
        });
        
        return ComputeSha256(data);
    }

    private static string ComputeSha256(string data)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(data);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}