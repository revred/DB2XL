namespace DB2XL.Core.Enums;

/// <summary>
/// Strategies for resolving conflicts between overlapping relationships
/// </summary>
public enum ConflictResolutionStrategy
{
    /// <summary>
    /// Select the relationship with the highest confidence score
    /// </summary>
    HighestConfidence,
    
    /// <summary>
    /// Prefer explicit foreign key relationships over inferred ones
    /// </summary>
    PreferForeignKeys,
    
    /// <summary>
    /// Select the most restrictive relationship (tightest constraints)
    /// </summary>
    MostRestrictive,
    
    /// <summary>
    /// Keep all valid relationships, merging where compatible
    /// </summary>
    KeepAll
}