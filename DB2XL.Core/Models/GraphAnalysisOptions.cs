using DB2XL.Core.Enums;

namespace DB2XL.Core.Models;

/// <summary>
/// Configuration options for database graph analysis
/// </summary>
public sealed class GraphAnalysisOptions
{
    /// <summary>
    /// Whether to analyze foreign key relationships from PRAGMA foreign_key_list
    /// </summary>
    public bool AnalyzeForeignKeys { get; init; } = true;
    
    /// <summary>
    /// Whether to infer relationships based on naming patterns
    /// </summary>
    public bool InferFromNaming { get; init; } = true;
    
    /// <summary>
    /// Whether to perform statistical analysis for potential relationships
    /// </summary>
    public bool PerformStatisticalAnalysis { get; init; } = false;
    
    /// <summary>
    /// Minimum confidence score to include a relationship (0.0 to 1.0)
    /// </summary>
    public double MinimumConfidenceScore { get; init; } = 0.5;
    
    /// <summary>
    /// Maximum depth for relationship traversal
    /// </summary>
    public int MaxTraversalDepth { get; init; } = 10;
    
    /// <summary>
    /// Tables to exclude from analysis (supports wildcards)
    /// </summary>
    public IReadOnlyList<string> ExcludeTables { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Tables to include in analysis (if specified, only these are analyzed)
    /// </summary>
    public IReadOnlyList<string> IncludeTables { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Whether to include views in the analysis
    /// </summary>
    public bool IncludeViews { get; init; } = false;
    
    /// <summary>
    /// Column naming patterns for foreign key inference
    /// Default patterns: *_id, *Id, fk_*, FK_*
    /// </summary>
    public IReadOnlyList<string> ForeignKeyPatterns { get; init; } = new[]
    {
        "*_id",
        "*Id", 
        "fk_*",
        "FK_*",
        "*_key",
        "*Key"
    };
    
    /// <summary>
    /// Whether to detect junction tables automatically
    /// </summary>
    public bool DetectJunctionTables { get; init; } = true;
    
    /// <summary>
    /// Maximum number of columns for a table to be considered a junction table
    /// </summary>
    public int MaxJunctionTableColumns { get; init; } = 5;
    
    /// <summary>
    /// Timeout for analysis operations in seconds
    /// </summary>
    public int AnalysisTimeoutSeconds { get; init; } = 300;
    
    /// <summary>
    /// Strategy for resolving conflicts between overlapping relationships
    /// </summary>
    public ConflictResolutionStrategy ConflictResolutionStrategy { get; init; } = ConflictResolutionStrategy.PreferForeignKeys;
}