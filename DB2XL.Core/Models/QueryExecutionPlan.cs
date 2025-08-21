namespace DB2XL.Core.Models;

/// <summary>
/// Represents a SQLite query execution plan from EXPLAIN QUERY PLAN
/// </summary>
public sealed record QueryExecutionPlan
{
    /// <summary>
    /// The original SQL query
    /// </summary>
    public string Query { get; init; } = string.Empty;
    
    /// <summary>
    /// Execution plan steps in order
    /// </summary>
    public IReadOnlyList<ExecutionStep> Steps { get; init; } = Array.Empty<ExecutionStep>();
    
    /// <summary>
    /// Overall performance metrics
    /// </summary>
    public PerformanceMetrics Metrics { get; init; } = new();
    
    /// <summary>
    /// Identified performance issues
    /// </summary>
    public IReadOnlyList<PerformanceIssue> Issues { get; init; } = Array.Empty<PerformanceIssue>();
    
    /// <summary>
    /// Optimization recommendations
    /// </summary>
    public IReadOnlyList<OptimizationRecommendation> Recommendations { get; init; } = Array.Empty<OptimizationRecommendation>();
}

/// <summary>
/// Individual step in query execution plan
/// </summary>
public sealed record ExecutionStep
{
    /// <summary>
    /// Step identifier from EXPLAIN QUERY PLAN
    /// </summary>
    public int Id { get; init; }
    
    /// <summary>
    /// Parent step identifier (0 if root)
    /// </summary>
    public int Parent { get; init; }
    
    /// <summary>
    /// Detail level (nesting depth)
    /// </summary>
    public int NotUsed { get; init; }
    
    /// <summary>
    /// Step description from SQLite
    /// </summary>
    public string Detail { get; init; } = string.Empty;
    
    /// <summary>
    /// Parsed operation type
    /// </summary>
    public ExecutionOperation Operation { get; init; }
    
    /// <summary>
    /// Tables involved in this step
    /// </summary>
    public IReadOnlyList<string> Tables { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Indexes used in this step
    /// </summary>
    public IReadOnlyList<IndexUsage> IndexUsages { get; init; } = Array.Empty<IndexUsage>();
    
    /// <summary>
    /// Estimated cost of this step
    /// </summary>
    public double EstimatedCost { get; init; }
    
    /// <summary>
    /// Performance characteristics of this step
    /// </summary>
    public StepPerformanceProfile Performance { get; init; } = new();
}

/// <summary>
/// Types of execution operations
/// </summary>
public enum ExecutionOperation
{
    Unknown,
    Scan,
    Search,
    Join,
    Sort,
    Group,
    Union,
    Intersect,
    Except,
    Subquery,
    Aggregate,
    Window,
    Filter
}

/// <summary>
/// Index usage information
/// </summary>
public sealed record IndexUsage
{
    /// <summary>
    /// Name of the index used
    /// </summary>
    public string IndexName { get; init; } = string.Empty;
    
    /// <summary>
    /// Type of index usage
    /// </summary>
    public IndexUsageType UsageType { get; init; }
    
    /// <summary>
    /// Columns covered by the index
    /// </summary>
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Whether the index fully covers the query needs
    /// </summary>
    public bool IsFullyCovering { get; init; }
    
    /// <summary>
    /// Selectivity of the index for this query
    /// </summary>
    public double Selectivity { get; init; }
}

/// <summary>
/// Types of index usage
/// </summary>
public enum IndexUsageType
{
    Unknown,
    PrimaryKey,
    UniqueIndex,
    NonUniqueIndex,
    CoveringIndex,
    PartialIndex,
    FullTableScan
}

/// <summary>
/// Performance characteristics of an execution step
/// </summary>
public sealed record StepPerformanceProfile
{
    /// <summary>
    /// Whether this step performs a table scan
    /// </summary>
    public bool IsTableScan { get; init; }
    
    /// <summary>
    /// Whether this step is likely expensive
    /// </summary>
    public bool IsExpensive { get; init; }
    
    /// <summary>
    /// Complexity score (0-100)
    /// </summary>
    public int ComplexityScore { get; init; }
    
    /// <summary>
    /// Performance impact level
    /// </summary>
    public PerformanceImpact Impact { get; init; }
    
    /// <summary>
    /// Estimated rows processed
    /// </summary>
    public long EstimatedRows { get; init; }
}

/// <summary>
/// Performance impact levels
/// </summary>
public enum PerformanceImpact
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Overall performance metrics for a query
/// </summary>
public sealed record PerformanceMetrics
{
    /// <summary>
    /// Total complexity score
    /// </summary>
    public int ComplexityScore { get; init; }
    
    /// <summary>
    /// Number of table scans
    /// </summary>
    public int TableScanCount { get; init; }
    
    /// <summary>
    /// Number of indexes used
    /// </summary>
    public int IndexUsageCount { get; init; }
    
    /// <summary>
    /// Number of JOIN operations
    /// </summary>
    public int JoinCount { get; init; }
    
    /// <summary>
    /// Estimated total rows processed
    /// </summary>
    public long EstimatedRowsProcessed { get; init; }
    
    /// <summary>
    /// Overall performance grade
    /// </summary>
    public PerformanceGrade Grade { get; init; }
    
    /// <summary>
    /// Performance category
    /// </summary>
    public QueryPerformanceCategory Category { get; init; }
}

/// <summary>
/// Performance grades
/// </summary>
public enum PerformanceGrade
{
    Excellent, // A+
    Good,      // A
    Fair,      // B
    Poor,      // C
    Terrible   // D/F
}

/// <summary>
/// Query performance categories
/// </summary>
public enum QueryPerformanceCategory
{
    Fast,        // < 1ms typical
    Moderate,    // 1-10ms typical  
    Slow,        // 10-100ms typical
    VerySlow,    // 100ms-1s typical
    Critical     // > 1s typical
}

/// <summary>
/// Identified performance issue
/// </summary>
public sealed record PerformanceIssue
{
    /// <summary>
    /// Type of performance issue
    /// </summary>
    public PerformanceIssueType Type { get; init; }
    
    /// <summary>
    /// Severity of the issue
    /// </summary>
    public IssueSeverity Severity { get; init; }
    
    /// <summary>
    /// Description of the issue
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// Affected execution steps
    /// </summary>
    public IReadOnlyList<int> AffectedSteps { get; init; } = Array.Empty<int>();
    
    /// <summary>
    /// Tables involved in the issue
    /// </summary>
    public IReadOnlyList<string> AffectedTables { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Estimated performance impact
    /// </summary>
    public double ImpactScore { get; init; }
}

/// <summary>
/// Types of performance issues
/// </summary>
public enum PerformanceIssueType
{
    TableScan,
    MissingIndex,
    IneffectiveIndex,
    CartesianProduct,
    SuboptimalJoin,
    UnboundedSort,
    LargeTemporaryTable,
    RedundantOperation,
    DeepNesting,
    ComplexSubquery
}

/// <summary>
/// Issue severity levels
/// </summary>
public enum IssueSeverity
{
    Info,
    Warning,
    Major,
    Critical
}

/// <summary>
/// Optimization recommendation
/// </summary>
public sealed record OptimizationRecommendation
{
    /// <summary>
    /// Type of optimization
    /// </summary>
    public OptimizationType Type { get; init; }
    
    /// <summary>
    /// Priority of the recommendation
    /// </summary>
    public RecommendationPriority Priority { get; init; }
    
    /// <summary>
    /// Title of the recommendation
    /// </summary>
    public string Title { get; init; } = string.Empty;
    
    /// <summary>
    /// Detailed description
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// Specific SQL to implement the recommendation (if applicable)
    /// </summary>
    public string? ImplementationSql { get; init; }
    
    /// <summary>
    /// Estimated performance improvement
    /// </summary>
    public double EstimatedImprovement { get; init; }
    
    /// <summary>
    /// Tables that would be affected
    /// </summary>
    public IReadOnlyList<string> AffectedTables { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Columns involved in the optimization
    /// </summary>
    public IReadOnlyList<string> AffectedColumns { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Types of optimizations
/// </summary>
public enum OptimizationType
{
    CreateIndex,
    DropIndex,
    ModifyIndex,
    RewriteQuery,
    AddConstraint,
    PartitionTable,
    NormalizeSchema,
    DenormalizeSchema,
    CacheStrategy,
    QueryHint
}

/// <summary>
/// Recommendation priorities
/// </summary>
public enum RecommendationPriority
{
    Low,
    Medium,
    High,
    Critical
}