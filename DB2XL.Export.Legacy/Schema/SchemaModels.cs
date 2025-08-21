using DB2XL.Data.Query;
using DB2XL.Data.Schema;
namespace DB2XL.Schema;

/// <summary>
/// Comprehensive database schema information
/// </summary>
public class DatabaseSchema
{
    public string DatabasePath { get; set; } = "";
    public DateTime AnalysisTimestamp { get; set; }
    public string SchemaVersion { get; set; } = "";
    public string UserVersion { get; set; } = "";
    public string JournalMode { get; set; } = "";
    public bool ForeignKeysEnabled { get; set; }
    public long PageSize { get; set; }
    public long PageCount { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public bool TransformationsEnabled { get; set; }
    public int TransformationErrors { get; set; }
    public List<TableSchema> Tables { get; set; } = new();
    
    // Computed statistics
    public int TotalTables { get; set; }
    public int TotalViews { get; set; }
    public long TotalRows { get; set; }
    public int TotalColumns { get; set; }
}

/// <summary>
/// Schema information for a single table or view
/// </summary>
public class TableSchema
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public long RowCount { get; set; }
    public string OrderMode { get; set; } = "";
    public List<string> OrderColumns { get; set; } = new();
    public List<ColumnSchema> Columns { get; set; } = new();
    public DateTime AnalysisTimestamp { get; set; }
    public string SchemaChecksum { get; set; } = "";
}

/// <summary>
/// Detailed schema and statistics for a single column
/// </summary>
public class ColumnSchema
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool NotNull { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsPrimaryKey { get; set; }
    public DateTime AnalysisTimestamp { get; set; }
    
    // Statistics
    public long NullCount { get; set; }
    public long NonNullCount { get; set; }
    public long DistinctCount { get; set; }
    
    // Text column statistics
    public long? MinLength { get; set; }
    public long? MaxLength { get; set; }
    public double? AvgLength { get; set; }
    
    // Numeric column statistics
    public string? MinValue { get; set; }
    public string? MaxValue { get; set; }
    public double? AvgValue { get; set; }
    
    // Transformation information
    public bool ExcludedByTransformation { get; set; }
    public bool HasTransformations { get; set; }
    public List<string>? TransformerNames { get; set; }
    
    // Error tracking
    public string? AnalysisError { get; set; }
}

/// <summary>
/// Provenance manifest tracking data lineage and transformations
/// </summary>
public class ProvenanceManifest
{
    public DateTime GeneratedTimestamp { get; set; }
    public string SourceDatabase { get; set; } = "";
    public string? ExportPath { get; set; }
    public string ExportFormat { get; set; } = "";
    public string SchemaVersion { get; set; } = "";
    public string UserVersion { get; set; } = "";
    public string DatabaseChecksum { get; set; } = "";
    public string ExportToolVersion { get; set; } = "";
    
    // Transformation information
    public bool TransformationsApplied { get; set; }
    public int TransformationErrors { get; set; }
    public string TransformationConfigVersion { get; set; } = "";
    public string ErrorHandlingStrategy { get; set; } = "";
    
    // Data lineage
    public List<DataLineage> DataLineages { get; set; } = new();
}

/// <summary>
/// Data lineage for a single table showing transformation flow
/// </summary>
public class DataLineage
{
    public string TableName { get; set; } = "";
    public long SourceRowCount { get; set; }
    public long? ExportedRowCount { get; set; }
    public List<string> OriginalColumns { get; set; } = new();
    public List<string> ExcludedColumns { get; set; } = new();
    public List<string> TransformedColumns { get; set; } = new();
    public List<TransformationDetail> TransformationDetails { get; set; } = new();
}

/// <summary>
/// Details of transformations applied to a specific column
/// </summary>
public class TransformationDetail
{
    public string ColumnName { get; set; } = "";
    public string OriginalType { get; set; } = "";
    public string? TransformedType { get; set; }
    public List<string> TransformerNames { get; set; } = new();
    public string? TransformationSummary { get; set; }
}

/// <summary>
/// Export format-specific schema manifest
/// </summary>
public class SchemaManifest
{
    public string ExportFormat { get; set; } = "";
    public DateTime GeneratedTimestamp { get; set; }
    public string SourceDatabase { get; set; } = "";
    public DatabaseSchema DatabaseSchema { get; set; } = new();
    public ProvenanceManifest ProvenanceManifest { get; set; } = new();
    public Dictionary<string, object> FormatSpecificMetadata { get; set; } = new();
}