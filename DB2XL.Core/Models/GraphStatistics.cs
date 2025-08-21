namespace DB2XL.Core.Models;

/// <summary>
/// Statistics about a database relationship graph
/// </summary>
public sealed record GraphStatistics
{
    /// <summary>
    /// Total number of table nodes
    /// </summary>
    public int NodeCount { get; init; }
    
    /// <summary>
    /// Total number of relationship edges
    /// </summary>
    public int EdgeCount { get; init; }
    
    /// <summary>
    /// Number of tables with no relationships
    /// </summary>
    public int IsolatedNodeCount { get; init; }
    
    /// <summary>
    /// Number of strongly connected components
    /// </summary>
    public int ConnectedComponentCount { get; init; }
    
    /// <summary>
    /// Average number of relationships per table
    /// </summary>
    public double AverageConnectivity => NodeCount > 0 ? (double)EdgeCount / NodeCount : 0;
    
    /// <summary>
    /// Graph density (actual edges / possible edges)
    /// </summary>
    public double Density => NodeCount > 1 ? (double)EdgeCount / (NodeCount * (NodeCount - 1)) : 0;
    
    /// <summary>
    /// Breakdown of relationships by discovery method
    /// </summary>
    public IReadOnlyDictionary<string, int> RelationshipsByMethod { get; init; } = 
        new Dictionary<string, int>();
    
    /// <summary>
    /// Breakdown of relationships by type
    /// </summary>
    public IReadOnlyDictionary<string, int> RelationshipsByType { get; init; } = 
        new Dictionary<string, int>();
    
    /// <summary>
    /// Average confidence score of all relationships
    /// </summary>
    public double AverageConfidenceScore { get; init; }
    
    /// <summary>
    /// Time taken to analyze the graph (in milliseconds)
    /// </summary>
    public long AnalysisDurationMs { get; init; }
}