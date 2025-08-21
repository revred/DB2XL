namespace DB2XL.Core.Enums;

/// <summary>
/// Algorithms for traversing database relationship graphs
/// </summary>
public enum GraphTraversalAlgorithm
{
    /// <summary>
    /// Breadth-first search - explores neighbors before going deeper
    /// </summary>
    BreadthFirst,
    
    /// <summary>
    /// Depth-first search - explores as far as possible along each branch
    /// </summary>
    DepthFirst,
    
    /// <summary>
    /// Dijkstra's shortest path algorithm
    /// </summary>
    Dijkstra,
    
    /// <summary>
    /// A* pathfinding with heuristics
    /// </summary>
    AStar,
    
    /// <summary>
    /// Topological sort for dependency ordering
    /// </summary>
    TopologicalSort
}