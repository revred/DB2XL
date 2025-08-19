using System.Globalization;
using DB2XL.Configuration;
using DB2XL.Transformers;

namespace DB2XL;

public enum BlobRenderMode
{
    Skip,
    Hex,
    Base64
}

public enum DualExportStrategy
{
    /// <summary>
    /// Export only transformed data (default behavior, backward compatible)
    /// </summary>
    TransformedOnly,
    
    /// <summary>
    /// Export only raw/original data (no transformations applied)
    /// </summary>
    RawOnly,
    
    /// <summary>
    /// Export both raw and transformed data as separate sheets in the same workbook
    /// </summary>
    DualSheets,
    
    /// <summary>
    /// Export both raw and transformed data as separate workbooks
    /// Raw data goes to specified path, transformed data gets "_Transformed" suffix
    /// </summary>
    DualWorkbooks
}

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
}