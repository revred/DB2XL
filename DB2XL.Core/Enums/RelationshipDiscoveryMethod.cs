namespace DB2XL.Core.Enums;

/// <summary>
/// Methods used to discover database relationships
/// </summary>
public enum RelationshipDiscoveryMethod
{
    /// <summary>
    /// Discovered via PRAGMA foreign_key_list
    /// </summary>
    ForeignKey,
    
    /// <summary>
    /// Discovered via column naming patterns (e.g., table_id)
    /// </summary>
    NamingPattern,
    
    /// <summary>
    /// Discovered via data type and constraint analysis
    /// </summary>
    TypeAnalysis,
    
    /// <summary>
    /// Discovered via statistical data analysis
    /// </summary>
    StatisticalAnalysis,
    
    /// <summary>
    /// Manually specified relationship
    /// </summary>
    Manual
}