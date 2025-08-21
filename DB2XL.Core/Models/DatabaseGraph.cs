using DB2XL.Core.Enums;

namespace DB2XL.Core.Models;

/// <summary>
/// Represents the complete relationship graph of a database
/// </summary>
public sealed class DatabaseGraph
{
    /// <summary>
    /// All table nodes in the graph
    /// </summary>
    public IReadOnlyDictionary<string, GraphNode> Nodes { get; init; } = 
        new Dictionary<string, GraphNode>();
    
    /// <summary>
    /// All relationship edges in the graph
    /// </summary>
    public IReadOnlyList<GraphEdge> Edges { get; init; } = Array.Empty<GraphEdge>();
    
    /// <summary>
    /// Options used to generate this graph
    /// </summary>
    public GraphAnalysisOptions Options { get; init; } = new();
    
    /// <summary>
    /// When this graph was created
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// Statistics about the graph
    /// </summary>
    public GraphStatistics Statistics { get; init; } = new();
    
    /// <summary>
    /// Gets all edges originating from a specific table
    /// </summary>
    public IEnumerable<GraphEdge> GetOutgoingEdges(string tableName) =>
        Edges.Where(e => e.FromTable.Equals(tableName, StringComparison.OrdinalIgnoreCase));
    
    /// <summary>
    /// Gets all edges pointing to a specific table
    /// </summary>
    public IEnumerable<GraphEdge> GetIncomingEdges(string tableName) =>
        Edges.Where(e => e.ToTable.Equals(tableName, StringComparison.OrdinalIgnoreCase));
    
    /// <summary>
    /// Gets all tables that have relationships with the specified table
    /// </summary>
    public IEnumerable<string> GetRelatedTables(string tableName)
    {
        var outgoing = GetOutgoingEdges(tableName).Select(e => e.ToTable);
        var incoming = GetIncomingEdges(tableName).Select(e => e.FromTable);
        return outgoing.Concat(incoming).Distinct();
    }
    
    /// <summary>
    /// Checks if there's a direct relationship between two tables
    /// </summary>
    public bool HasDirectRelationship(string fromTable, string toTable) =>
        Edges.Any(e => 
            e.FromTable.Equals(fromTable, StringComparison.OrdinalIgnoreCase) &&
            e.ToTable.Equals(toTable, StringComparison.OrdinalIgnoreCase));
    
    /// <summary>
    /// Gets the strongest relationship between two tables (highest confidence)
    /// </summary>
    public GraphEdge? GetStrongestRelationship(string fromTable, string toTable) =>
        Edges.Where(e => 
            e.FromTable.Equals(fromTable, StringComparison.OrdinalIgnoreCase) &&
            e.ToTable.Equals(toTable, StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(e => e.ConfidenceScore)
        .FirstOrDefault();
}