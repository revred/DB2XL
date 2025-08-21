using DB2XL.Data.Query;
using DB2XL.Data.Schema;
using System.Globalization;
using DB2XL.Transform.Configuration;
using DB2XL.Transform.Interfaces;
using DB2XL.DeltaExport;
using DB2XL.Query;
using DB2XL.Core.Enums;

namespace DB2XL;

// BlobRenderMode moved to DB2XL.Core.Enums to avoid duplication
// DualExportStrategy moved to DB2XL.Core.Enums to avoid duplication

public sealed class SqliteToExcelOptions
{
    public bool WriteAllAsText { get; init; } = true;
    public bool PreserveNumericTypes { get; init; } = false;
    public bool IncludeMetadataSheet { get; init; } = true;
    public string MetadataSheetName { get; init; } = "_Export_Metadata";
    public int ReadBatchSize { get; init; } = 25_000;
    public int CommandTimeoutSeconds { get; init; } = 180;
    public string? TableNameLikeFilter { get; init; } = null;
    public bool IncludeViews { get; init; } = false;
    public BlobRenderMode BlobMode { get; init; } = BlobRenderMode.Hex;
    public bool OrderRowsDeterministically { get; init; } = true;
    public bool SplitOversizeSheets { get; init; } = true;
    public CultureInfo InvariantCulture { get; init; } = CultureInfo.InvariantCulture;
    
    /// <summary>
    /// Transformation configuration for data processing during export
    /// </summary>
    public TransformationConfig? TransformationConfig { get; init; } = null;
    
    /// <summary>
    /// Transformer registry for creating transformer instances
    /// If not provided, default registry will be used when transformations are enabled
    /// </summary>
    public ITransformerRegistry? TransformerRegistry { get; init; } = null;
    
    /// <summary>
    /// Dual export strategy for handling original and transformed data
    /// </summary>
    public DualExportStrategy DualExportStrategy { get; init; } = DualExportStrategy.TransformedOnly;
    
    /// <summary>
    /// Suffix for original/raw data sheets when using dual sheet strategy
    /// </summary>
    public string RawDataSuffix { get; init; } = "_Raw";
    
    /// <summary>
    /// Suffix for transformed data sheets when using dual sheet strategy
    /// </summary>
    public string TransformedDataSuffix { get; init; } = "_Transformed";
    
    /// <summary>
    /// Delta export configuration for incremental exports
    /// If provided, only changes since the last export will be included
    /// </summary>
    public DeltaExportConfig? DeltaExportConfig { get; init; } = null;
    
    /// <summary>
    /// Whether to include delta export metadata in the output
    /// Shows checkpoint information, strategy used, and export statistics
    /// </summary>
    public bool IncludeDeltaMetadata { get; init; } = true;
    
    /// <summary>
    /// Custom checkpoint service for delta exports
    /// If not provided, file-based checkpoint service will be used
    /// </summary>
    public IDeltaCheckpointService? DeltaCheckpointService { get; init; } = null;
    
    /// <summary>
    /// Advanced selection grammar for sophisticated filtering and querying
    /// When provided, overrides simple table name filtering
    /// </summary>
    public SelectionGrammar? SelectionGrammar { get; init; } = null;
    
    /// <summary>
    /// Security filter configuration for restricting table and column access
    /// If provided, all table and column access will be validated against these rules
    /// </summary>
    public SecurityFilterConfig? SecurityFilter { get; init; } = null;
}