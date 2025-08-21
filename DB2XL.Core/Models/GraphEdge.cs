using DB2XL.Core.Enums;

namespace DB2XL.Core.Models;

/// <summary>
/// Represents a relationship edge between two tables in the database graph
/// </summary>
public sealed record GraphEdge(
    string FromTable,
    string ToTable,
    RelationshipType Type)
{
    /// <summary>
    /// Source column(s) for the relationship
    /// </summary>
    public IReadOnlyList<string> FromColumns { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Target column(s) for the relationship
    /// </summary>
    public IReadOnlyList<string> ToColumns { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Confidence score for this relationship (0.0 to 1.0)
    /// </summary>
    public double ConfidenceScore { get; init; } = 1.0;
    
    /// <summary>
    /// How this relationship was discovered
    /// </summary>
    public RelationshipDiscoveryMethod DiscoveryMethod { get; init; } = 
        RelationshipDiscoveryMethod.ForeignKey;
    
    /// <summary>
    /// Additional metadata about this relationship
    /// </summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = 
        new Dictionary<string, object?>();
    
    /// <summary>
    /// Cardinality of the relationship (if known)
    /// </summary>
    public RelationshipCardinality? Cardinality { get; init; }
    
    /// <summary>
    /// Gets a unique identifier for this edge
    /// </summary>
    public string EdgeId => $"{FromTable}.{string.Join(",", FromColumns)}->{ToTable}.{string.Join(",", ToColumns)}";
    
    /// <summary>
    /// Gets the display name for this edge
    /// </summary>
    public string DisplayName => $"{FromTable} → {ToTable}";
}