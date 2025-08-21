namespace DB2XL.Core.Enums;

/// <summary>
/// Specifies how rows should be ordered during export
/// </summary>
public enum OrderMode
{
    /// <summary>
    /// No specific ordering
    /// </summary>
    None,
    
    /// <summary>
    /// Order by primary key columns
    /// </summary>
    PrimaryKey,
    
    /// <summary>
    /// Order by SQLite's internal rowid
    /// </summary>
    Rowid
}