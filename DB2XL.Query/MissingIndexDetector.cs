using Microsoft.Data.Sqlite;

namespace DB2XL.Query;

/// <summary>
/// Detects missing indexes that would improve query performance
/// </summary>
public sealed class MissingIndexDetector
{
    private readonly QueryPlanAnalyzer _planAnalyzer;
    private readonly PrimaryKeyDiscoveryService _pkService;
    
    public MissingIndexDetector(
        QueryPlanAnalyzer? planAnalyzer = null,
        PrimaryKeyDiscoveryService? pkService = null)
    {
        _planAnalyzer = planAnalyzer ?? new QueryPlanAnalyzer();
        _pkService = pkService ?? new PrimaryKeyDiscoveryService();
    }
    
    /// <summary>
    /// Analyzes a SelectionGrammar and suggests missing indexes
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="selectionGrammar">Query to analyze</param>
    /// <returns>Missing index recommendations</returns>
    public MissingIndexAnalysis AnalyzeQuery(SqliteConnection connection, ISelectionGrammar selectionGrammar)
    {
        // First get the query plan analysis
        var planAnalysis = _planAnalyzer.AnalyzeQuery(connection, selectionGrammar);
        
        // Analyze the selection grammar for potential indexes
        var missingIndexes = new List<MissingIndexRecommendation>();
        
        // Analyze WHERE clause for filtering indexes
        if (selectionGrammar.Where != null)
        {
            var whereIndexes = AnalyzeWhereClause(connection, selectionGrammar.Table, selectionGrammar.Where);
            missingIndexes.AddRange(whereIndexes);
        }
        
        // Analyze ORDER BY clause for sorting indexes
        if (selectionGrammar.OrderBy != null && selectionGrammar.OrderBy.Count > 0)
        {
            var orderIndexes = AnalyzeOrderByClause(connection, selectionGrammar.Table, selectionGrammar.OrderBy);
            missingIndexes.AddRange(orderIndexes);
        }
        
        // Combine WHERE and ORDER BY for composite indexes
        if (selectionGrammar.Where != null && selectionGrammar.OrderBy != null && selectionGrammar.OrderBy.Count > 0)
        {
            var compositeIndexes = AnalyzeCompositeIndexOpportunities(connection, selectionGrammar.Table, selectionGrammar.Where, selectionGrammar.OrderBy);
            missingIndexes.AddRange(compositeIndexes);
        }
        
        // Filter out recommendations for existing indexes
        var existingIndexes = GetExistingIndexes(connection, selectionGrammar.Table);
        var filteredRecommendations = FilterExistingIndexes(missingIndexes, existingIndexes);
        
        return new MissingIndexAnalysis
        {
            TableName = selectionGrammar.Table,
            Query = planAnalysis.Query,
            QueryComplexity = planAnalysis.EstimatedComplexity,
            ExistingIndexes = existingIndexes,
            MissingIndexRecommendations = filteredRecommendations,
            PerformanceImpact = CalculatePerformanceImpact(planAnalysis, filteredRecommendations)
        };
    }
    
    /// <summary>
    /// Analyzes WHERE clause to suggest filtering indexes
    /// </summary>
    private List<MissingIndexRecommendation> AnalyzeWhereClause(SqliteConnection connection, string tableName, IWhereExpression whereExpression)
    {
        var recommendations = new List<MissingIndexRecommendation>();
        var columns = ExtractColumnsFromWhere(whereExpression);
        
        foreach (var column in columns)
        {
            recommendations.Add(new MissingIndexRecommendation
            {
                IndexType = IndexType.Filter,
                TableName = tableName,
                Columns = new[] { column },
                IndexName = GenerateIndexName(tableName, new[] { column }, "filter"),
                Reason = $"WHERE clause filters on column '{column}'",
                EstimatedSelectivity = EstimateSelectivity(connection, tableName, column),
                Priority = IndexPriority.High,
                CreateSql = GenerateCreateIndexSql(tableName, new[] { column }, "filter")
            });
        }
        
        return recommendations;
    }
    
    /// <summary>
    /// Analyzes ORDER BY clause to suggest sorting indexes
    /// </summary>
    private List<MissingIndexRecommendation> AnalyzeOrderByClause(SqliteConnection connection, string tableName, IReadOnlyList<IOrderByClause> orderBy)
    {
        var recommendations = new List<MissingIndexRecommendation>();
        var orderColumns = orderBy.Select(o => o.Column).ToArray();
        
        if (orderColumns.Length > 0)
        {
            recommendations.Add(new MissingIndexRecommendation
            {
                IndexType = IndexType.Sort,
                TableName = tableName,
                Columns = orderColumns,
                IndexName = GenerateIndexName(tableName, orderColumns, "sort"),
                Reason = $"ORDER BY clause sorts on columns: {string.Join(", ", orderColumns)}",
                EstimatedSelectivity = 1.0, // Sorting indexes help regardless of selectivity
                Priority = IndexPriority.Medium,
                CreateSql = GenerateCreateIndexSql(tableName, orderColumns, "sort")
            });
        }
        
        return recommendations;
    }
    
    /// <summary>
    /// Analyzes opportunities for composite indexes (WHERE + ORDER BY)
    /// </summary>
    private List<MissingIndexRecommendation> AnalyzeCompositeIndexOpportunities(
        SqliteConnection connection, 
        string tableName, 
        IWhereExpression whereExpression, 
        IReadOnlyList<IOrderByClause> orderBy)
    {
        var recommendations = new List<MissingIndexRecommendation>();
        
        var whereColumns = ExtractColumnsFromWhere(whereExpression).ToArray();
        var orderColumns = orderBy.Select(o => o.Column).ToArray();
        
        // Create composite index: WHERE columns first (most selective), then ORDER BY columns
        var compositeColumns = whereColumns.Concat(orderColumns).Distinct().ToArray();
        
        if (compositeColumns.Length > 1 && compositeColumns.Length <= 5) // Reasonable index size
        {
            recommendations.Add(new MissingIndexRecommendation
            {
                IndexType = IndexType.Composite,
                TableName = tableName,
                Columns = compositeColumns,
                IndexName = GenerateIndexName(tableName, compositeColumns, "composite"),
                Reason = $"Composite index can handle both filtering and sorting efficiently",
                EstimatedSelectivity = EstimateCompositeSelectivity(connection, tableName, whereColumns),
                Priority = IndexPriority.High,
                CreateSql = GenerateCreateIndexSql(tableName, compositeColumns, "composite")
            });
        }
        
        return recommendations;
    }
    
    /// <summary>
    /// Extracts column names from WHERE expressions
    /// </summary>
    private static IEnumerable<string> ExtractColumnsFromWhere(IWhereExpression whereExpression)
    {
        return whereExpression switch
        {
            ComparisonExpression comp => new[] { comp.Column },
            AndExpression and => and.Expressions.SelectMany(ExtractColumnsFromWhere),
            OrExpression or => or.Expressions.SelectMany(ExtractColumnsFromWhere),
            NotExpression not => ExtractColumnsFromWhere(not.Expression),
            _ => Array.Empty<string>()
        };
    }
    
    /// <summary>
    /// Gets existing indexes for a table
    /// </summary>
    private List<ExistingIndex> GetExistingIndexes(SqliteConnection connection, string tableName)
    {
        var indexes = new List<ExistingIndex>();
        
        try
        {
            // Get index list
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA index_list(@table)";
            cmd.Parameters.AddWithValue("@table", tableName);
            
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var indexName = reader.GetString(1); // name column
                var isUnique = reader.GetBoolean(2); // unique column
                
                // Skip auto-indexes and primary key indexes
                if (indexName.StartsWith("sqlite_autoindex_")) continue;
                
                // Get index columns
                var columns = GetIndexColumns(connection, indexName);
                
                indexes.Add(new ExistingIndex
                {
                    Name = indexName,
                    TableName = tableName,
                    Columns = columns,
                    IsUnique = isUnique,
                    IsPrimaryKey = indexName.Contains("_pk_") || indexName.Equals($"pk_{tableName}", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        catch (Exception)
        {
            // If we can't get index info, return empty list
        }
        
        return indexes;
    }
    
    /// <summary>
    /// Gets columns for a specific index
    /// </summary>
    private static List<string> GetIndexColumns(SqliteConnection connection, string indexName)
    {
        var columns = new List<string>();
        
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA index_info(@index)";
            cmd.Parameters.AddWithValue("@index", indexName);
            
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(2)); // name column
            }
        }
        catch (Exception)
        {
            // If we can't get column info, return empty list
        }
        
        return columns;
    }
    
    /// <summary>
    /// Filters out recommendations for indexes that already exist
    /// </summary>
    private static List<MissingIndexRecommendation> FilterExistingIndexes(
        List<MissingIndexRecommendation> recommendations, 
        List<ExistingIndex> existingIndexes)
    {
        var filtered = new List<MissingIndexRecommendation>();
        
        foreach (var recommendation in recommendations)
        {
            var isRedundant = false;
            
            foreach (var existing in existingIndexes)
            {
                // Check if existing index covers the recommended columns
                if (IndexCoversColumns(existing.Columns, recommendation.Columns))
                {
                    isRedundant = true;
                    break;
                }
            }
            
            if (!isRedundant)
            {
                filtered.Add(recommendation);
            }
        }
        
        return filtered;
    }
    
    /// <summary>
    /// Checks if an existing index covers the recommended columns
    /// </summary>
    private static bool IndexCoversColumns(IReadOnlyList<string> existingColumns, IReadOnlyList<string> neededColumns)
    {
        // For a simple filter index, existing index must start with needed columns
        if (neededColumns.Count == 1)
        {
            return existingColumns.Count > 0 && 
                   string.Equals(existingColumns[0], neededColumns[0], StringComparison.OrdinalIgnoreCase);
        }
        
        // For composite indexes, existing must contain all needed columns in proper order
        if (existingColumns.Count < neededColumns.Count) return false;
        
        for (int i = 0; i < neededColumns.Count; i++)
        {
            if (!string.Equals(existingColumns[i], neededColumns[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        
        return true;
    }
    
    /// <summary>
    /// Estimates selectivity of a column for indexing decisions
    /// </summary>
    private static double EstimateSelectivity(SqliteConnection connection, string tableName, string columnName)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $@"
                SELECT 
                    COUNT(DISTINCT ""{columnName}"") * 1.0 / COUNT(*) as selectivity 
                FROM ""{tableName}""";
            
            var result = cmd.ExecuteScalar();
            return result is double selectivity ? selectivity : 0.5; // Default moderate selectivity
        }
        catch (Exception)
        {
            return 0.5; // Default if we can't calculate
        }
    }
    
    /// <summary>
    /// Estimates selectivity for composite indexes
    /// </summary>
    private static double EstimateCompositeSelectivity(SqliteConnection connection, string tableName, string[] columns)
    {
        if (columns.Length == 0) return 1.0;
        
        // For composite indexes, multiply individual selectivities (simplified model)
        double combinedSelectivity = 1.0;
        
        foreach (var column in columns)
        {
            var selectivity = EstimateSelectivity(connection, tableName, column);
            combinedSelectivity *= selectivity;
        }
        
        return Math.Max(combinedSelectivity, 0.001); // Minimum selectivity to avoid zero
    }
    
    /// <summary>
    /// Generates appropriate index name
    /// </summary>
    private static string GenerateIndexName(string tableName, string[] columns, string indexType)
    {
        var columnPart = string.Join("_", columns.Take(3)); // Limit to 3 columns for name
        return $"idx_{tableName}_{columnPart}_{indexType}".ToLowerInvariant();
    }
    
    /// <summary>
    /// Generates CREATE INDEX SQL statement
    /// </summary>
    private static string GenerateCreateIndexSql(string tableName, string[] columns, string indexType)
    {
        var indexName = GenerateIndexName(tableName, columns, indexType);
        var columnList = string.Join(", ", columns.Select(c => $"\"{c}\""));
        
        return $"CREATE INDEX \"{indexName}\" ON \"{tableName}\" ({columnList});";
    }
    
    /// <summary>
    /// Calculates overall performance impact of implementing recommended indexes
    /// </summary>
    private static PerformanceImpact CalculatePerformanceImpact(QueryPlanAnalysis planAnalysis, List<MissingIndexRecommendation> recommendations)
    {
        var impact = PerformanceImpact.Low;
        
        // High impact if we have full table scans that can be eliminated
        var hasFullScans = planAnalysis.PerformanceIssues.Any(i => i.Type == PerformanceIssueType.FullTableScan);
        var hasHighPriorityIndexes = recommendations.Any(r => r.Priority == IndexPriority.High);
        
        if (hasFullScans && hasHighPriorityIndexes)
        {
            impact = PerformanceImpact.High;
        }
        else if (hasFullScans || hasHighPriorityIndexes)
        {
            impact = PerformanceImpact.Medium;
        }
        else if (recommendations.Count > 0)
        {
            impact = PerformanceImpact.Low;
        }
        
        return impact;
    }
}

/// <summary>
/// Result of missing index analysis
/// </summary>
public sealed record MissingIndexAnalysis
{
    /// <summary>
    /// Table name being analyzed
    /// </summary>
    public string TableName { get; init; } = string.Empty;
    
    /// <summary>
    /// SQL query being analyzed
    /// </summary>
    public string Query { get; init; } = string.Empty;
    
    /// <summary>
    /// Complexity of the query
    /// </summary>
    public QueryComplexity QueryComplexity { get; init; }
    
    /// <summary>
    /// Existing indexes on the table
    /// </summary>
    public List<ExistingIndex> ExistingIndexes { get; init; } = new();
    
    /// <summary>
    /// Recommended missing indexes
    /// </summary>
    public List<MissingIndexRecommendation> MissingIndexRecommendations { get; init; } = new();
    
    /// <summary>
    /// Estimated performance impact of implementing recommendations
    /// </summary>
    public PerformanceImpact PerformanceImpact { get; init; }
    
    /// <summary>
    /// Gets a summary of the analysis
    /// </summary>
    public string GetSummary()
    {
        return $"Table: {TableName}, Existing indexes: {ExistingIndexes.Count}, " +
               $"Missing indexes: {MissingIndexRecommendations.Count}, " +
               $"Impact: {PerformanceImpact}";
    }
}

/// <summary>
/// Recommendation for a missing index
/// </summary>
public sealed record MissingIndexRecommendation
{
    /// <summary>
    /// Type of index
    /// </summary>
    public IndexType IndexType { get; init; }
    
    /// <summary>
    /// Table name
    /// </summary>
    public string TableName { get; init; } = string.Empty;
    
    /// <summary>
    /// Columns to include in the index
    /// </summary>
    public string[] Columns { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Suggested index name
    /// </summary>
    public string IndexName { get; init; } = string.Empty;
    
    /// <summary>
    /// Reason why this index is recommended
    /// </summary>
    public string Reason { get; init; } = string.Empty;
    
    /// <summary>
    /// Estimated selectivity (0.0 - 1.0, higher is more selective)
    /// </summary>
    public double EstimatedSelectivity { get; init; }
    
    /// <summary>
    /// Priority of creating this index
    /// </summary>
    public IndexPriority Priority { get; init; }
    
    /// <summary>
    /// SQL statement to create the index
    /// </summary>
    public string CreateSql { get; init; } = string.Empty;
}

/// <summary>
/// Existing index information
/// </summary>
public sealed record ExistingIndex
{
    /// <summary>
    /// Index name
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// Table name
    /// </summary>
    public string TableName { get; init; } = string.Empty;
    
    /// <summary>
    /// Columns in the index
    /// </summary>
    public List<string> Columns { get; init; } = new();
    
    /// <summary>
    /// Whether the index enforces uniqueness
    /// </summary>
    public bool IsUnique { get; init; }
    
    /// <summary>
    /// Whether this is a primary key index
    /// </summary>
    public bool IsPrimaryKey { get; init; }
}

/// <summary>
/// Types of indexes
/// </summary>
public enum IndexType
{
    Filter,
    Sort,
    Composite,
    Covering,
    Unique
}

/// <summary>
/// Priority levels for index recommendations
/// </summary>
public enum IndexPriority
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Performance impact levels
/// </summary>
public enum PerformanceImpact
{
    None,
    Low,
    Medium,
    High,
    Critical
}