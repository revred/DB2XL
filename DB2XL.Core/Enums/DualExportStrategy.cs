namespace DB2XL.Core.Enums;

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