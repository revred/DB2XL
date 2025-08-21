namespace DB2XL.Core.Enums;

/// <summary>
/// Specifies how BLOB data should be rendered in exports
/// </summary>
public enum BlobRenderMode
{
    /// <summary>
    /// Skip BLOB data (leave empty)
    /// </summary>
    Skip,
    
    /// <summary>
    /// Render as hexadecimal string
    /// </summary>
    Hex,
    
    /// <summary>
    /// Render as Base64 encoded string
    /// </summary>
    Base64
}