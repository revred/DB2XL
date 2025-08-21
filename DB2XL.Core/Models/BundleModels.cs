using System.Text.Json.Serialization;

namespace DB2XL.Core.Models;

/// <summary>
/// Configuration options for bundle export operations.
/// Deterministic and hashable for provenance tracking.
/// </summary>
public sealed record BundleExportOptions
{
    /// <summary>Root path for the bundle directory structure. If empty, uses temp directory with timestamp.</summary>
    public string BundleRootPath { get; init; } = string.Empty;
    
    /// <summary>Name of the Excel index workbook file.</summary>
    public string IndexWorkbookName { get; init; } = "index.xlsx";
    
    /// <summary>Directory name for manifest files (schema, provenance, etc.).</summary>
    public string ManifestDirectoryName { get; init; } = "manifest";
    
    /// <summary>Directory name for table partition files.</summary>
    public string TablesDirectoryName { get; init; } = "tables";
    
    /// <summary>Whether to generate Parquet files alongside JSONL.</summary>
    public bool GenerateParquet { get; init; } = false;
    
    /// <summary>Whether to include sample files for tables.</summary>
    public bool IncludeSamples { get; init; } = false;
    
    /// <summary>Maximum number of rows in sample files.</summary>
    public int SampleRowLimit { get; init; } = 10_000;
    
    /// <summary>Use deterministic timestamps for testing. When false, uses DateTime.UtcNow.</summary>
    public bool DeterministicTimestamps { get; init; } = false;
}

/// <summary>
/// Represents the complete directory and file layout of a bundle export.
/// All paths are absolute for internal use, converted to relative for manifests.
/// </summary>
public sealed record BundleLayout
{
    /// <summary>Absolute path to the bundle root directory.</summary>
    public string RootPath { get; init; } = string.Empty;
    
    /// <summary>Absolute path to the Excel index workbook.</summary>
    public string IndexWorkbookPath { get; init; } = string.Empty;
    
    /// <summary>Absolute path to the manifest directory.</summary>
    public string ManifestPath { get; init; } = string.Empty;
    
    /// <summary>Absolute path to the tables directory.</summary>
    public string TablesPath { get; init; } = string.Empty;
    
    /// <summary>UTC timestamp when the export was initiated.</summary>
    public DateTime ExportTimestamp { get; init; }
    
    /// <summary>
    /// Gets the absolute directory path for a specific table's partition files.
    /// </summary>
    /// <param name="tableName">Name of the database table</param>
    /// <returns>Absolute path to table directory</returns>
    public string GetTableDirectory(string tableName) => 
        Path.Combine(TablesPath, SanitizePathComponent(tableName));
    
    /// <summary>
    /// Sanitizes a string for use as a file system path component.
    /// Replaces invalid characters with underscores.
    /// </summary>
    /// <param name="name">Input string to sanitize</param>
    /// <returns>Sanitized path component</returns>
    private static string SanitizePathComponent(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "_empty_";
            
        var invalidChars = Path.GetInvalidFileNameChars();
        var result = new char[name.Length];
        var resultIndex = 0;
        
        for (int i = 0; i < name.Length; i++)
        {
            if (invalidChars.Contains(name[i]))
            {
                result[resultIndex++] = '_';
            }
            else
            {
                result[resultIndex++] = name[i];
            }
        }
        
        var sanitized = new string(result, 0, resultIndex).Trim();
        
        // Handle edge cases
        if (string.IsNullOrWhiteSpace(sanitized))
            return "_sanitized_";
            
        return sanitized;
    }
}

/// <summary>
/// Information about a single partition within a bundle export.
/// Used for manifest generation and integrity verification.
/// </summary>
public sealed record PartitionInfo
{
    /// <summary>Name of the source database table.</summary>
    public string TableName { get; init; } = string.Empty;
    
    /// <summary>Human-readable partition label (e.g., "2025Q1", "p00001", "WARN").</summary>
    public string PartitionLabel { get; init; } = string.Empty;
    
    /// <summary>Partitioning strategy description (e.g., "by=quarter,field=created_at").</summary>
    public string Strategy { get; init; } = string.Empty;
    
    /// <summary>Total number of rows in this partition.</summary>
    public long RowCount { get; init; }
    
    /// <summary>Relative path to the partition file from bundle root.</summary>
    public string RelativePath { get; init; } = string.Empty;
    
    /// <summary>SHA-256 hash of the partition file contents.</summary>
    public string Sha256Hash { get; init; } = string.Empty;
    
    /// <summary>First primary key value in this partition (for audit/replay).</summary>
    public string? FirstPrimaryKey { get; init; }
    
    /// <summary>Last primary key value in this partition (for audit/replay).</summary>
    public string? LastPrimaryKey { get; init; }
    
    /// <summary>File format of this partition (jsonl, parquet, etc.).</summary>
    public string Format { get; init; } = "jsonl";
    
    /// <summary>File size in bytes.</summary>
    public long FileSizeBytes { get; init; }
}

/// <summary>
/// Supported partitioning strategies for bundle exports.
/// </summary>
public enum PartitionStrategy
{
    /// <summary>No partitioning - single file per table.</summary>
    None,
    
    /// <summary>Fixed number of rows per partition.</summary>
    RowCount,
    
    /// <summary>Time-based partitioning (day, week, month, quarter, year).</summary>
    TimeBased,
    
    /// <summary>Filter-based partitioning using WHERE clauses.</summary>
    FilterBased
}

/// <summary>
/// Time-based partitioning granularities.
/// </summary>
public enum TimePartitionGranularity
{
    Day,
    Week,
    Month,
    Quarter,
    Year
}

/// <summary>
/// Configuration for a specific table's partitioning strategy.
/// </summary>
public sealed record TablePartitionConfig
{
    /// <summary>Name of the table to partition.</summary>
    public string TableName { get; init; } = string.Empty;
    
    /// <summary>Partitioning strategy to use.</summary>
    public PartitionStrategy Strategy { get; init; } = PartitionStrategy.None;
    
    /// <summary>For RowCount strategy: number of rows per partition.</summary>
    public int RowsPerPartition { get; init; } = 200_000;
    
    /// <summary>For TimeBased strategy: column name containing datetime values.</summary>
    public string? TimeColumn { get; init; }
    
    /// <summary>For TimeBased strategy: granularity of time partitions.</summary>
    public TimePartitionGranularity TimeGranularity { get; init; } = TimePartitionGranularity.Month;
    
    /// <summary>For FilterBased strategy: SQL WHERE clause for this partition.</summary>
    public string? FilterExpression { get; init; }
    
    /// <summary>For FilterBased strategy: human-readable label for this filter.</summary>
    public string? FilterLabel { get; init; }
}