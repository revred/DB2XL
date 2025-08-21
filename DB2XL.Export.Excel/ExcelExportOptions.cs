using System.Globalization;
using DB2XL.Core.Interfaces;
using DB2XL.Core.Enums;
using DB2XL.Query;

namespace DB2XL.Export.Excel;

/// <summary>
/// Configuration options for Excel exports
/// </summary>
public sealed record ExcelExportOptions : IExportOptions
{
    /// <summary>
    /// Whether to write all values as text (preserves data fidelity)
    /// </summary>
    public bool WriteAllAsText { get; init; } = true;
    
    /// <summary>
    /// Whether to preserve numeric types when WriteAllAsText is false
    /// </summary>
    public bool PreserveNumericTypes { get; init; } = false;
    
    /// <summary>
    /// Whether to include a metadata sheet with export information
    /// </summary>
    public bool IncludeMetadataSheet { get; init; } = true;
    
    /// <summary>
    /// Name of the metadata sheet
    /// </summary>
    public string MetadataSheetName { get; init; } = "_Export_Metadata";
    
    /// <summary>
    /// How to render BLOB data in the export
    /// </summary>
    public BlobRenderMode BlobMode { get; init; } = BlobRenderMode.Hex;
    
    /// <summary>
    /// Whether to split tables that exceed Excel's row limit across multiple sheets
    /// </summary>
    public bool SplitOversizeSheets { get; init; } = true;
    
    /// <summary>
    /// Culture to use for number formatting
    /// </summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;
    
    /// <summary>
    /// Strategy for handling dual export scenarios
    /// </summary>
    public DualExportStrategy DualExportStrategy { get; init; } = DualExportStrategy.TransformedOnly;
    
    /// <summary>
    /// Suffix for raw data sheets when using dual sheet strategy
    /// </summary>
    public string RawDataSuffix { get; init; } = "_Raw";
    
    /// <summary>
    /// Suffix for transformed data sheets when using dual sheet strategy
    /// </summary>
    public string TransformedDataSuffix { get; init; } = "_Transformed";
    
    /// <summary>
    /// Advanced selection grammar for sophisticated filtering and querying
    /// When provided, overrides simple table name filtering
    /// </summary>
    public SelectionGrammar? SelectionGrammar { get; init; } = null;
    
    // IExportOptions implementation
    public int CommandTimeoutSeconds { get; init; } = 180;
    public string? TableNameFilter { get; init; } = null;
    public bool IncludeViews { get; init; } = false;
    public bool OrderRowsDeterministically { get; init; } = true;
}

/// <summary>
/// Strategy for handling original and transformed data in Excel exports
/// </summary>
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