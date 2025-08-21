namespace DB2XL.Core.Enums;

/// <summary>
/// Types of relationships between database tables
/// </summary>
public enum RelationshipType
{
    /// <summary>
    /// Foreign key relationship (explicit)
    /// </summary>
    ForeignKey,
    
    /// <summary>
    /// Inferred relationship based on naming patterns
    /// </summary>
    Inferred,
    
    /// <summary>
    /// Junction table relationship (many-to-many)
    /// </summary>
    Junction,
    
    /// <summary>
    /// Self-referential relationship (hierarchical)
    /// </summary>
    SelfReferential,
    
    /// <summary>
    /// Potential relationship with low confidence
    /// </summary>
    Potential
}