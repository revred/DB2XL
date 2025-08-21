namespace DB2XL.Core.Enums;

/// <summary>
/// Cardinality types for database relationships
/// </summary>
public enum RelationshipCardinality
{
    /// <summary>
    /// One-to-one relationship
    /// </summary>
    OneToOne,
    
    /// <summary>
    /// One-to-many relationship
    /// </summary>
    OneToMany,
    
    /// <summary>
    /// Many-to-one relationship
    /// </summary>
    ManyToOne,
    
    /// <summary>
    /// Many-to-many relationship
    /// </summary>
    ManyToMany
}