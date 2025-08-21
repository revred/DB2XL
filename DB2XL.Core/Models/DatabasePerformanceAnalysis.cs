namespace DB2XL.Core.Models;

/// <summary>
/// Comprehensive database performance analysis results
/// </summary>
public sealed class DatabasePerformanceAnalysis
{
    /// <summary>
    /// Path to the analyzed database
    /// </summary>
    public string DatabasePath { get; init; } = string.Empty;
    
    /// <summary>
    /// When the analysis was performed
    /// </summary>
    public DateTime AnalysisTimestamp { get; init; }
    
    /// <summary>
    /// Analysis options used
    /// </summary>
    public PerformanceAnalysisOptions Options { get; init; } = new();
    
    /// <summary>
    /// Database relationship graph (from Phase 2)
    /// </summary>
    public DatabaseGraph DatabaseGraph { get; init; } = new();
    
    /// <summary>
    /// Performance statistics for each table
    /// </summary>
    public IReadOnlyList<TablePerformanceStatistics> TableStatistics { get; init; } = Array.Empty<TablePerformanceStatistics>();
    
    /// <summary>
    /// Analysis of existing indexes and recommendations for new ones
    /// </summary>
    public IndexAnalysisResult IndexAnalysis { get; init; } = new();
    
    /// <summary>
    /// Analysis of specific queries (if provided)
    /// </summary>
    public IReadOnlyList<QueryExecutionPlan> QueryAnalyses { get; init; } = Array.Empty<QueryExecutionPlan>();
    
    /// <summary>
    /// Overall performance recommendations
    /// </summary>
    public IReadOnlyList<OptimizationRecommendation> Recommendations { get; init; } = Array.Empty<OptimizationRecommendation>();
    
    /// <summary>
    /// Overall database performance score (0.0 - 1.0)
    /// </summary>
    public double OverallScore { get; init; }
}

/// <summary>
/// Configuration options for performance analysis
/// </summary>
public sealed class PerformanceAnalysisOptions
{
    /// <summary>
    /// Whether to analyze column cardinality statistics
    /// </summary>
    public bool AnalyzeColumnCardinality { get; init; } = true;
    
    /// <summary>
    /// Whether to identify missing index opportunities
    /// </summary>
    public bool IdentifyMissingIndexes { get; init; } = true;
    
    /// <summary>
    /// Common queries to analyze for performance
    /// </summary>
    public IReadOnlyList<string> CommonQueries { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Minimum table size to warrant performance analysis
    /// </summary>
    public long MinimumTableSizeForAnalysis { get; init; } = 1000;
    
    /// <summary>
    /// Maximum time to spend on analysis (seconds)
    /// </summary>
    public int MaxAnalysisTimeSeconds { get; init; } = 300;
}

/// <summary>
/// Performance statistics for a single table
/// </summary>
public sealed class TablePerformanceStatistics
{
    /// <summary>
    /// Name of the table
    /// </summary>
    public string TableName { get; init; } = string.Empty;
    
    /// <summary>
    /// Number of rows in the table
    /// </summary>
    public long RowCount { get; init; }
    
    /// <summary>
    /// Number of columns in the table
    /// </summary>
    public int ColumnCount { get; init; }
    
    /// <summary>
    /// Storage characteristics of the table
    /// </summary>
    public TableStorageInfo StorageInfo { get; init; } = new();
    
    /// <summary>
    /// Statistics for individual columns
    /// </summary>
    public IReadOnlyList<ColumnPerformanceStatistics> ColumnStatistics { get; init; } = Array.Empty<ColumnPerformanceStatistics>();
    
    /// <summary>
    /// Performance issues identified for this table
    /// </summary>
    public IReadOnlyList<PerformanceIssue> PerformanceIssues { get; init; } = Array.Empty<PerformanceIssue>();
}

/// <summary>
/// Storage information for a table
/// </summary>
public sealed class TableStorageInfo
{
    /// <summary>
    /// Estimated size of the table in bytes
    /// </summary>
    public long EstimatedSizeBytes { get; init; }
    
    /// <summary>
    /// Estimated number of database pages used
    /// </summary>
    public long EstimatedPageCount { get; init; }
    
    /// <summary>
    /// Average row size in bytes
    /// </summary>
    public double AverageRowSizeBytes => EstimatedSizeBytes > 0 ? EstimatedSizeBytes / Math.Max(1.0, EstimatedPageCount * 4096.0 / 100.0) : 0;
}

/// <summary>
/// Performance statistics for a single column
/// </summary>
public sealed class ColumnPerformanceStatistics
{
    /// <summary>
    /// Name of the column
    /// </summary>
    public string ColumnName { get; init; } = string.Empty;
    
    /// <summary>
    /// Data type of the column
    /// </summary>
    public string DataType { get; init; } = string.Empty;
    
    /// <summary>
    /// Number of distinct values
    /// </summary>
    public long DistinctValueCount { get; init; }
    
    /// <summary>
    /// Number of null values
    /// </summary>
    public long NullCount { get; init; }
    
    /// <summary>
    /// Selectivity of the column (distinct values / total rows)
    /// </summary>
    public double Selectivity { get; init; }
    
    /// <summary>
    /// Whether this column is a good candidate for indexing
    /// </summary>
    public bool IndexCandidate { get; init; }
}

/// <summary>
/// Results of index analysis
/// </summary>
public sealed class IndexAnalysisResult
{
    /// <summary>
    /// Information about existing indexes
    /// </summary>
    public IReadOnlyList<IndexStatistics> ExistingIndexes { get; init; } = Array.Empty<IndexStatistics>();
    
    /// <summary>
    /// Recommendations for missing indexes
    /// </summary>
    public IReadOnlyList<MissingIndexRecommendation> MissingIndexRecommendations { get; init; } = Array.Empty<MissingIndexRecommendation>();
    
    /// <summary>
    /// Overall health of the indexing strategy (0.0 - 1.0)
    /// </summary>
    public double OverallIndexHealth { get; init; }
}

/// <summary>
/// Statistics about an existing index
/// </summary>
public sealed class IndexStatistics
{
    /// <summary>
    /// Name of the index
    /// </summary>
    public string IndexName { get; init; } = string.Empty;
    
    /// <summary>
    /// Table the index belongs to
    /// </summary>
    public string TableName { get; init; } = string.Empty;
    
    /// <summary>
    /// Whether the index enforces uniqueness
    /// </summary>
    public bool IsUnique { get; init; }
    
    /// <summary>
    /// Columns covered by the index
    /// </summary>
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Estimated selectivity of the index
    /// </summary>
    public double EstimatedSelectivity { get; init; }
    
    /// <summary>
    /// How frequently the index is used
    /// </summary>
    public IndexUsageFrequency Usage { get; init; }
}

/// <summary>
/// Frequency of index usage
/// </summary>
public enum IndexUsageFrequency
{
    Unknown,
    Never,
    Rarely,
    Sometimes,
    Frequently,
    Always
}

/// <summary>
/// Recommendation for a missing index
/// </summary>
public sealed class MissingIndexRecommendation
{
    /// <summary>
    /// Table that needs the index
    /// </summary>
    public string TableName { get; init; } = string.Empty;
    
    /// <summary>
    /// Columns that should be included in the index
    /// </summary>
    public IReadOnlyList<string> RecommendedColumns { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Reason for the recommendation
    /// </summary>
    public IndexRecommendationReason Reason { get; init; }
    
    /// <summary>
    /// Priority of this recommendation
    /// </summary>
    public RecommendationPriority Priority { get; init; }
    
    /// <summary>
    /// Estimated benefit of creating this index (0.0 - 1.0)
    /// </summary>
    public double EstimatedBenefit { get; init; }
}

/// <summary>
/// Reasons for recommending an index
/// </summary>
public enum IndexRecommendationReason
{
    ForeignKeyCandidate,
    FrequentWhereClause,
    JoinCondition,
    OrderByClause,
    GroupByClause,
    HighSelectivity,
    TableScanElimination
}