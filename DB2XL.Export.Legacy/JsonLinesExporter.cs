using DB2XL.Data.Query;
using DB2XL.Data.Schema;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Globalization;
using DB2XL.Transform.Configuration;
using DB2XL.Transform.Interfaces;
using DB2XL.Schema;
using Microsoft.Extensions.Logging;
using DB2XL.Core.Models;
using DB2XL.Core.Enums;

namespace DB2XL;

/// <summary>
/// Options for JSONL export functionality
/// </summary>
public sealed class JsonLinesExportOptions
{
    /// <summary>
    /// Whether to write all values as strings (default: false - preserve types)
    /// </summary>
    public bool WriteAllAsStrings { get; init; } = false;
    
    /// <summary>
    /// Transformation configuration for data processing during export
    /// </summary>
    public TransformationConfig? TransformationConfig { get; init; } = null;
    
    /// <summary>
    /// Transformer registry for creating transformer instances
    /// </summary>
    public ITransformerRegistry? TransformerRegistry { get; init; } = null;
    
    /// <summary>
    /// Dual export strategy for handling original and transformed data
    /// </summary>
    public DualExportStrategy DualExportStrategy { get; init; } = DualExportStrategy.TransformedOnly;
    
    /// <summary>
    /// Suffix for original/raw data files when using dual export strategy
    /// </summary>
    public string RawDataSuffix { get; init; } = "_raw";
    
    /// <summary>
    /// Suffix for transformed data files when using dual export strategy
    /// </summary>
    public string TransformedDataSuffix { get; init; } = "_transformed";
    
    /// <summary>
    /// Command timeout in seconds for SQL operations
    /// </summary>
    public int CommandTimeoutSeconds { get; init; } = 180;
    
    /// <summary>
    /// Whether to include views in the export
    /// </summary>
    public bool IncludeViews { get; init; } = false;
    
    /// <summary>
    /// Table name LIKE filter (e.g., "user_%")
    /// </summary>
    public string? TableNameLikeFilter { get; init; } = null;
    
    /// <summary>
    /// Whether to order rows deterministically
    /// </summary>
    public bool OrderRowsDeterministically { get; init; } = true;
    
    /// <summary>
    /// Culture for formatting (default: InvariantCulture)
    /// </summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;
    
    /// <summary>
    /// Whether to include schema manifests
    /// </summary>
    public bool IncludeSchemaManifests { get; init; } = true;
    
    /// <summary>
    /// How to handle BLOB data
    /// </summary>
    public BlobRenderMode BlobMode { get; init; } = BlobRenderMode.Base64;
}

/// <summary>
/// Exports SQLite databases to JSONL (JSON Lines) format for LLM applications
/// </summary>
public static class JsonLinesExporter
{
    public static void Export(string sqlitePath, string outputDirectory, JsonLinesExportOptions? options = null)
    {
        options ??= new JsonLinesExportOptions();
        
        ValidateInputs(sqlitePath, outputDirectory);
        
        // Handle different dual export strategies
        switch (options.DualExportStrategy)
        {
            case DualExportStrategy.TransformedOnly:
                ExportSingle(sqlitePath, outputDirectory, options, useTransformations: true);
                break;
                
            case DualExportStrategy.RawOnly:
                ExportSingle(sqlitePath, outputDirectory, options, useTransformations: false);
                break;
                
            case DualExportStrategy.DualSheets: // For JSONL, this means dual directories
                ExportDualDirectories(sqlitePath, outputDirectory, options);
                break;
                
            case DualExportStrategy.DualWorkbooks: // For JSONL, this means dual sets of files
                ExportDualSets(sqlitePath, outputDirectory, options);
                break;
                
            default:
                throw new ArgumentOutOfRangeException(nameof(options.DualExportStrategy), 
                    $"Unsupported dual export strategy: {options.DualExportStrategy}");
        }
    }
    
    private static void ExportSingle(string sqlitePath, string outputDirectory, JsonLinesExportOptions options, bool useTransformations)
    {
        Directory.CreateDirectory(outputDirectory);
        
        using var connection = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly;Cache=Shared;Pooling=True;");
        connection.Open();
        
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = OFF;";
        command.ExecuteNonQuery();
        
        using var transaction = connection.BeginTransaction();
        
        var tables = SqliteSchemaReader.GetDatabaseObjects(connection, options.TableNameLikeFilter, options.IncludeViews);
        
        // Initialize transformation pipeline if configured and requested
        TransformationPipeline? transformationPipeline = null;
        if (useTransformations && options.TransformationConfig != null)
        {
            var registry = options.TransformerRegistry ?? TransformerRegistryBuilder.CreateDefault();
            transformationPipeline = new TransformationPipeline(options.TransformationConfig, registry);
        }
        
        var exportManifest = new JsonLinesExportManifest
        {
            ExportTimestamp = DateTime.UtcNow,
            SourceDatabase = sqlitePath,
            TransformationsEnabled = transformationPipeline?.AreTransformationsEnabled ?? false,
            DualExportStrategy = options.DualExportStrategy.ToString(),
            Tables = new List<JsonLinesTableInfo>()
        };
        
        foreach (var table in tables)
        {
            var tableInfo = ExportTable(connection, outputDirectory, table, options, transformationPipeline, "");
            exportManifest.Tables.Add(tableInfo);
        }
        
        // Write export manifest
        if (options.IncludeSchemaManifests)
        {
            WriteExportManifest(outputDirectory, exportManifest, "");
        }
        
        transaction.Commit();
    }
    
    private static void ExportDualDirectories(string sqlitePath, string outputDirectory, JsonLinesExportOptions options)
    {
        var rawDir = Path.Combine(outputDirectory, "raw");
        var transformedDir = Path.Combine(outputDirectory, "transformed");
        
        // Export raw data
        ExportSingle(sqlitePath, rawDir, options, useTransformations: false);
        
        // Export transformed data if transformations are configured
        if (options.TransformationConfig != null)
        {
            ExportSingle(sqlitePath, transformedDir, options, useTransformations: true);
        }
    }
    
    private static void ExportDualSets(string sqlitePath, string outputDirectory, JsonLinesExportOptions options)
    {
        // Export raw data to main directory
        ExportSingle(sqlitePath, outputDirectory, options, useTransformations: false);
        
        // Export transformed data with suffix
        if (options.TransformationConfig != null)
        {
            var transformedDir = outputDirectory + options.TransformedDataSuffix;
            ExportSingle(sqlitePath, transformedDir, options, useTransformations: true);
        }
    }
    
    private static JsonLinesTableInfo ExportTable(
        SqliteConnection connection, 
        string outputDirectory, 
        TableInfo table, 
        JsonLinesExportOptions options, 
        TransformationPipeline? transformationPipeline,
        string fileSuffix)
    {
        var columns = SqliteSchemaReader.GetTableColumns(connection, table.Name);
        var orderInfo = SqliteSchemaReader.DetermineTableOrdering(connection, table.Name, columns);
        var sql = SqlQueryBuilder.BuildSelectQuery(table.Name, columns, orderInfo, options.OrderRowsDeterministically);
        
        var fileName = $"{SanitizeFileName(table.Name)}{fileSuffix}.jsonl";
        var filePath = Path.Combine(outputDirectory, fileName);
        var schemaFilePath = Path.Combine(outputDirectory, $"{SanitizeFileName(table.Name)}{fileSuffix}.schema.json");
        
        var tableInfo = new JsonLinesTableInfo
        {
            TableName = table.Name,
            TableType = table.Type,
            FileName = fileName,
            SchemaFileName = options.IncludeSchemaManifests ? $"{SanitizeFileName(table.Name)}{fileSuffix}.schema.json" : null,
            ColumnCount = columns.Count,
            OrderMode = orderInfo.Mode.ToString()
        };
        
        var jsonOptions = new JsonWriterOptions
        {
            Indented = false // JSONL should not be indented
        };
        
        var schema = new JsonLinesTableSchema
        {
            TableName = table.Name,
            TableType = table.Type,
            Columns = columns.Select(c => new JsonLinesColumnInfo
            {
                Name = c.Name,
                Type = c.Type,
                NotNull = c.NotNull,
                DefaultValue = c.DefaultValue?.ToString(),
                IsPrimaryKey = c.IsPrimaryKey
            }).ToList(),
            OrderMode = orderInfo.Mode.ToString(),
            OrderColumns = orderInfo.Columns.ToList(),
            ExportTimestamp = DateTime.UtcNow
        };
        
        long rowCount = 0;
        var rowChecksumBuilder = new ChecksumBuilder();
        
        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var writer = new StreamWriter(fileStream);
        
        using var cmd = connection.CreateCommand();
        cmd.CommandTimeout = options.CommandTimeoutSeconds;
        cmd.CommandText = sql;
        
        using var reader = cmd.ExecuteReader(System.Data.CommandBehavior.SequentialAccess);
        
        while (reader.Read())
        {
            rowCount++;
            var jsonRow = new Dictionary<string, object?>();
            
            for (int i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                var columnName = column.Name;
                
                // Skip excluded columns
                if (transformationPipeline?.IsColumnExcluded(table.Name, columnName) == true)
                    continue;
                
                var (value, _) = DataConverter.ReadValueAsText(reader, i, new SqliteToExcelOptions 
                { 
                    BlobMode = options.BlobMode, 
                    InvariantCulture = options.Culture 
                }, table.Name, columnName, (int)rowCount, transformationPipeline);
                
                // Convert to appropriate JSON type
                object? jsonValue = ConvertToJsonValue(value, options.WriteAllAsStrings, reader, i);
                jsonRow[columnName] = jsonValue;
                
                // Update checksum
                rowChecksumBuilder.UpdateField(value);
            }
            
            rowChecksumBuilder.EndRow();
            
            // Write JSON line
            var jsonString = JsonSerializer.Serialize(jsonRow, (JsonSerializerOptions?)null);
            writer.WriteLine(jsonString);
        }
        
        tableInfo.RowCount = rowCount;
        tableInfo.Checksum = rowChecksumBuilder.FinalizeHex();
        
        // Write schema file if requested
        if (options.IncludeSchemaManifests)
        {
            var schemaJson = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(schemaFilePath, schemaJson);
        }
        
        return tableInfo;
    }
    
    private static object? ConvertToJsonValue(string? value, bool writeAllAsStrings, SqliteDataReader reader, int columnIndex)
    {
        if (value == null || reader.IsDBNull(columnIndex))
            return null;
            
        if (writeAllAsStrings)
            return value;
        
        // Try to preserve types for better JSON compatibility
        var fieldType = reader.GetFieldType(columnIndex);
        
        return fieldType switch
        {
            Type t when t == typeof(long) => long.TryParse(value, out var longVal) ? longVal : value,
            Type t when t == typeof(double) => double.TryParse(value, out var doubleVal) ? doubleVal : value,
            Type t when t == typeof(decimal) => decimal.TryParse(value, out var decimalVal) ? decimalVal : value,
            Type t when t == typeof(bool) => bool.TryParse(value, out var boolVal) ? boolVal : value,
            _ => value // Keep as string
        };
    }
    
    private static void WriteExportManifest(string outputDirectory, JsonLinesExportManifest manifest, string suffix)
    {
        var manifestPath = Path.Combine(outputDirectory, $"export_manifest{suffix}.json");
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(manifestPath, manifestJson);
    }
    
    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return sanitized;
    }
    
    private static void ValidateInputs(string sqlitePath, string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(sqlitePath))
            throw new ArgumentException("SQLite path cannot be null or whitespace.", nameof(sqlitePath));
            
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output directory cannot be null or whitespace.", nameof(outputDirectory));
            
        if (!File.Exists(sqlitePath))
            throw new FileNotFoundException($"SQLite database file not found: {sqlitePath}");
    }

    /// <summary>
    /// Generates a comprehensive schema manifest for a JSONL export
    /// </summary>
    public static SchemaManifest GenerateManifest(string sqlitePath, string outputDirectory, JsonLinesExportOptions? options = null)
    {
        options ??= new JsonLinesExportOptions();
        ValidateInputs(sqlitePath, outputDirectory);

        using var connection = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly;Cache=Shared;Pooling=True;");
        connection.Open();

        // Initialize transformation pipeline if configured
        TransformationPipeline? transformationPipeline = null;
        if (options.TransformationConfig != null)
        {
            var registry = options.TransformerRegistry ?? TransformerRegistryBuilder.CreateDefault();
            transformationPipeline = new TransformationPipeline(options.TransformationConfig, registry);
        }

        // Convert JSONL options to Excel options for manifest generation
        var excelOptions = new SqliteToExcelOptions
        {
            WriteAllAsText = options.WriteAllAsStrings,
            TableNameLikeFilter = options.TableNameLikeFilter,
            IncludeViews = options.IncludeViews,
            OrderRowsDeterministically = options.OrderRowsDeterministically,
            BlobMode = options.BlobMode,
            InvariantCulture = options.Culture,
            TransformationConfig = options.TransformationConfig,
            TransformerRegistry = options.TransformerRegistry,
            DualExportStrategy = options.DualExportStrategy
        };

        return ManifestGenerator.GenerateManifest(connection, sqlitePath, outputDirectory, "JSONL", excelOptions, transformationPipeline);
    }

    /// <summary>
    /// Exports SQLite to JSONL and generates a comprehensive schema manifest
    /// </summary>
    public static SchemaManifest ExportWithManifest(string sqlitePath, string outputDirectory, JsonLinesExportOptions? options = null)
    {
        // Perform the export
        Export(sqlitePath, outputDirectory, options);
        
        // Generate manifest
        var manifest = GenerateManifest(sqlitePath, outputDirectory, options);
        
        // Save manifest in the output directory
        var manifestPath = Path.Combine(outputDirectory, "schema_manifest.json");
        ManifestGenerator.SaveManifest(manifest, manifestPath);
        
        return manifest;
    }

    /// <summary>
    /// Validates a JSONL export against its manifest
    /// </summary>
    public static ManifestValidationResult ValidateExport(string outputDirectory, string? manifestPath = null)
    {
        manifestPath ??= Path.Combine(outputDirectory, "schema_manifest.json");
        
        if (!File.Exists(manifestPath))
        {
            return new ManifestValidationResult
            {
                IsValid = false,
                ValidationTimestamp = DateTime.UtcNow,
                ExportPath = outputDirectory,
                ManifestPath = manifestPath,
                Errors = { $"Schema manifest file not found: {manifestPath}" }
            };
        }

        var manifest = ManifestGenerator.LoadManifest(manifestPath);
        var result = ManifestGenerator.ValidateExport(outputDirectory, manifest);
        result.ManifestPath = manifestPath;  // Set the manifest path that was used
        return result;
    }
}

/// <summary>
/// Manifest for JSONL export containing metadata about the export
/// </summary>
public class JsonLinesExportManifest
{
    public DateTime ExportTimestamp { get; set; }
    public string SourceDatabase { get; set; } = "";
    public bool TransformationsEnabled { get; set; }
    public string DualExportStrategy { get; set; } = "";
    public List<JsonLinesTableInfo> Tables { get; set; } = new();
}

/// <summary>
/// Information about a table in the JSONL export
/// </summary>
public class JsonLinesTableInfo
{
    public string TableName { get; set; } = "";
    public string TableType { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? SchemaFileName { get; set; }
    public long RowCount { get; set; }
    public int ColumnCount { get; set; }
    public string OrderMode { get; set; } = "";
    public string Checksum { get; set; } = "";
}

/// <summary>
/// Schema information for a JSONL table export
/// </summary>
public class JsonLinesTableSchema
{
    public string TableName { get; set; } = "";
    public string TableType { get; set; } = "";
    public List<JsonLinesColumnInfo> Columns { get; set; } = new();
    public string OrderMode { get; set; } = "";
    public List<string> OrderColumns { get; set; } = new();
    public DateTime ExportTimestamp { get; set; }
}

/// <summary>
/// Column information in JSONL schema
/// </summary>
public class JsonLinesColumnInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool NotNull { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsPrimaryKey { get; set; }
}