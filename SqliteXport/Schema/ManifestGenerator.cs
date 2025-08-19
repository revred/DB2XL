using System.Text.Json;
using Microsoft.Data.Sqlite;
using DB2XL.Configuration;
using DB2XL.Transformers;

namespace DB2XL.Schema;

/// <summary>
/// Generates comprehensive schema and provenance manifests for exports
/// </summary>
public static class ManifestGenerator
{
    /// <summary>
    /// Generates a complete schema manifest for an export
    /// </summary>
    public static SchemaManifest GenerateManifest(
        SqliteConnection connection,
        string databasePath,
        string exportPath,
        string exportFormat,
        SqliteToExcelOptions? options = null,
        TransformationPipeline? transformationPipeline = null)
    {
        options ??= new SqliteToExcelOptions();
        
        // Analyze the database schema
        var databaseSchema = SchemaAnalyzer.AnalyzeDatabase(connection, databasePath, options, transformationPipeline);
        
        // Generate provenance manifest
        var provenanceManifest = SchemaAnalyzer.GenerateProvenanceManifest(
            databasePath, databaseSchema, transformationPipeline, exportPath, exportFormat);
        
        var manifest = new SchemaManifest
        {
            ExportFormat = exportFormat,
            GeneratedTimestamp = DateTime.UtcNow,
            SourceDatabase = databasePath,
            DatabaseSchema = databaseSchema,
            ProvenanceManifest = provenanceManifest
        };

        // Add format-specific metadata
        switch (exportFormat.ToLowerInvariant())
        {
            case "excel":
            case "xlsx":
                AddExcelSpecificMetadata(manifest, options);
                break;
            case "jsonl":
            case "json-lines":
                AddJsonLinesSpecificMetadata(manifest, options);
                break;
        }

        return manifest;
    }

    /// <summary>
    /// Saves a manifest to a JSON file
    /// </summary>
    public static void SaveManifest(SchemaManifest manifest, string filePath)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.Serialize(manifest, options);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Loads a manifest from a JSON file
    /// </summary>
    public static SchemaManifest LoadManifest(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        return JsonSerializer.Deserialize<SchemaManifest>(json, options) 
            ?? throw new InvalidOperationException($"Failed to deserialize manifest from {filePath}");
    }

    /// <summary>
    /// Generates a lightweight summary manifest for quick validation
    /// </summary>
    public static SummaryManifest GenerateSummaryManifest(SchemaManifest fullManifest)
    {
        return new SummaryManifest
        {
            GeneratedTimestamp = fullManifest.GeneratedTimestamp,
            SourceDatabase = fullManifest.SourceDatabase,
            ExportFormat = fullManifest.ExportFormat,
            DatabaseChecksum = fullManifest.ProvenanceManifest.DatabaseChecksum,
            TableCount = fullManifest.DatabaseSchema.Tables.Count,
            TotalRows = fullManifest.DatabaseSchema.TotalRows,
            TotalColumns = fullManifest.DatabaseSchema.TotalColumns,
            TransformationsApplied = fullManifest.ProvenanceManifest.TransformationsApplied,
            TransformationErrors = fullManifest.ProvenanceManifest.TransformationErrors,
            Tables = fullManifest.DatabaseSchema.Tables.Select(t => new TableSummary
            {
                Name = t.Name,
                Type = t.Type,
                RowCount = t.RowCount,
                ColumnCount = t.Columns.Count,
                SchemaChecksum = t.SchemaChecksum,
                HasTransformations = t.Columns.Any(c => c.HasTransformations),
                ExcludedColumns = t.Columns.Count(c => c.ExcludedByTransformation)
            }).ToList()
        };
    }

    /// <summary>
    /// Validates that an export matches its manifest
    /// </summary>
    public static ManifestValidationResult ValidateExport(
        string exportPath, 
        SchemaManifest manifest,
        bool validateChecksums = true)
    {
        var result = new ManifestValidationResult
        {
            IsValid = true,
            ValidationTimestamp = DateTime.UtcNow,
            ExportPath = exportPath,
            ManifestPath = ""
        };

        try
        {
            // Check if export file exists
            if (!File.Exists(exportPath))
            {
                result.IsValid = false;
                result.Errors.Add($"Export file not found: {exportPath}");
                return result;
            }

            // Get file info
            var fileInfo = new FileInfo(exportPath);
            result.ExportFileSizeBytes = fileInfo.Length;
            result.ExportLastModified = fileInfo.LastWriteTimeUtc;

            // Format-specific validation
            switch (manifest.ExportFormat.ToLowerInvariant())
            {
                case "excel":
                case "xlsx":
                    ValidateExcelExport(exportPath, manifest, result);
                    break;
                case "jsonl":
                case "json-lines":
                    ValidateJsonLinesExport(exportPath, manifest, result);
                    break;
                default:
                    result.Warnings.Add($"Unknown export format: {manifest.ExportFormat}");
                    break;
            }

            // Checksum validation if requested
            if (validateChecksums)
            {
                // This could be extended to re-calculate checksums and compare
                result.Warnings.Add("Checksum validation not yet implemented");
            }
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Validation failed: {ex.Message}");
        }

        return result;
    }

    private static void AddExcelSpecificMetadata(SchemaManifest manifest, SqliteToExcelOptions options)
    {
        manifest.FormatSpecificMetadata["writeAllAsText"] = options.WriteAllAsText;
        manifest.FormatSpecificMetadata["preserveNumericTypes"] = options.PreserveNumericTypes;
        manifest.FormatSpecificMetadata["includeMetadataSheet"] = options.IncludeMetadataSheet;
        manifest.FormatSpecificMetadata["metadataSheetName"] = options.MetadataSheetName;
        manifest.FormatSpecificMetadata["blobMode"] = options.BlobMode.ToString();
        manifest.FormatSpecificMetadata["splitOversizeSheets"] = options.SplitOversizeSheets;
        manifest.FormatSpecificMetadata["dualExportStrategy"] = options.DualExportStrategy.ToString();
        manifest.FormatSpecificMetadata["maxExcelRows"] = 1048576;
        manifest.FormatSpecificMetadata["maxExcelColumns"] = 16384;
    }

    private static void AddJsonLinesSpecificMetadata(SchemaManifest manifest, SqliteToExcelOptions options)
    {
        // Convert Excel options to equivalent JSONL concepts
        manifest.FormatSpecificMetadata["writeAllAsStrings"] = options.WriteAllAsText;
        manifest.FormatSpecificMetadata["preserveTypes"] = !options.WriteAllAsText;
        manifest.FormatSpecificMetadata["blobMode"] = options.BlobMode.ToString();
        manifest.FormatSpecificMetadata["dualExportStrategy"] = options.DualExportStrategy.ToString();
        manifest.FormatSpecificMetadata["includeSchemaManifests"] = true;
        manifest.FormatSpecificMetadata["jsonLinesFormat"] = "standard";
    }

    private static void ValidateExcelExport(string exportPath, SchemaManifest manifest, ManifestValidationResult result)
    {
        try
        {
            // Basic Excel file validation
            using var workbook = new ClosedXML.Excel.XLWorkbook(exportPath);
            
            result.ActualSheetCount = workbook.Worksheets.Count;
            
            // Count expected sheets (tables + metadata if enabled)
            var expectedSheets = manifest.DatabaseSchema.Tables.Count;
            if (manifest.FormatSpecificMetadata.TryGetValue("includeMetadataSheet", out var includeMetadata) 
                && includeMetadata is bool include && include)
            {
                expectedSheets++;
            }

            if (result.ActualSheetCount != expectedSheets)
            {
                result.Warnings.Add($"Sheet count mismatch: expected {expectedSheets}, found {result.ActualSheetCount}");
            }

            // Validate table sheets exist
            foreach (var table in manifest.DatabaseSchema.Tables)
            {
                var sheetName = table.Name.Length > 31 ? table.Name.Substring(0, 31) : table.Name;
                if (!workbook.Worksheets.Any(ws => ws.Name.StartsWith(sheetName)))
                {
                    result.Warnings.Add($"Expected sheet for table '{table.Name}' not found");
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Excel validation failed: {ex.Message}");
        }
    }

    private static void ValidateJsonLinesExport(string exportPath, SchemaManifest manifest, ManifestValidationResult result)
    {
        try
        {
            // For JSONL exports, validate directory structure
            if (Directory.Exists(exportPath))
            {
                var jsonlFiles = Directory.GetFiles(exportPath, "*.jsonl");
                result.ActualFileCount = jsonlFiles.Length;
                
                var expectedFiles = manifest.DatabaseSchema.Tables.Count;
                if (result.ActualFileCount != expectedFiles)
                {
                    result.Warnings.Add($"JSONL file count mismatch: expected {expectedFiles}, found {result.ActualFileCount}");
                }

                // Check for manifest file
                var manifestFile = Path.Combine(exportPath, "export_manifest.json");
                if (!File.Exists(manifestFile))
                {
                    result.Warnings.Add("Export manifest file not found");
                }

                // Validate table files exist
                foreach (var table in manifest.DatabaseSchema.Tables)
                {
                    var expectedFile = Path.Combine(exportPath, $"{table.Name}.jsonl");
                    if (!File.Exists(expectedFile))
                    {
                        result.Warnings.Add($"Expected JSONL file for table '{table.Name}' not found");
                    }
                }
            }
            else
            {
                result.Errors.Add($"JSONL export directory not found: {exportPath}");
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"JSONL validation failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Lightweight manifest summary for quick validation
/// </summary>
public class SummaryManifest
{
    public DateTime GeneratedTimestamp { get; set; }
    public string SourceDatabase { get; set; } = "";
    public string ExportFormat { get; set; } = "";
    public string DatabaseChecksum { get; set; } = "";
    public int TableCount { get; set; }
    public long TotalRows { get; set; }
    public int TotalColumns { get; set; }
    public bool TransformationsApplied { get; set; }
    public int TransformationErrors { get; set; }
    public List<TableSummary> Tables { get; set; } = new();
}

/// <summary>
/// Summary information for a single table
/// </summary>
public class TableSummary
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public long RowCount { get; set; }
    public int ColumnCount { get; set; }
    public string SchemaChecksum { get; set; } = "";
    public bool HasTransformations { get; set; }
    public int ExcludedColumns { get; set; }
}

/// <summary>
/// Result of manifest validation
/// </summary>
public class ManifestValidationResult
{
    public bool IsValid { get; set; }
    public DateTime ValidationTimestamp { get; set; }
    public string ExportPath { get; set; } = "";
    public string ManifestPath { get; set; } = "";
    public long ExportFileSizeBytes { get; set; }
    public DateTime ExportLastModified { get; set; }
    public int ActualSheetCount { get; set; }
    public int ActualFileCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}