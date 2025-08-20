using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DB2XL.Query;

/// <summary>
/// Analyzes SQLite query execution plans to identify performance issues
/// </summary>
public sealed class QueryPlanAnalyzer
{
    /// <summary>
    /// Analyzes the execution plan for a selection grammar query
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="selectionGrammar">Query to analyze</param>
    /// <returns>Query plan analysis results</returns>
    public QueryPlanAnalysis AnalyzeQuery(SqliteConnection connection, ISelectionGrammar selectionGrammar)
    {
        var sqlBuilder = new SqlBuilder();
        var query = sqlBuilder.BuildQuery(selectionGrammar);
        
        return AnalyzeQuery(connection, query.Sql, query.Parameters);
    }
    
    /// <summary>
    /// Analyzes the execution plan for a raw SQL query
    /// </summary>
    /// <param name="connection">Database connection</param>
    /// <param name="sql">SQL query to analyze</param>
    /// <param name="parameters">Query parameters</param>
    /// <returns>Query plan analysis results</returns>
    public QueryPlanAnalysis AnalyzeQuery(SqliteConnection connection, string sql, Dictionary<string, object?> parameters)
    {
        var planSteps = GetQueryPlan(connection, sql, parameters);
        var analysis = new QueryPlanAnalysis
        {
            Query = sql,
            Parameters = parameters.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            PlanSteps = planSteps,
            PerformanceIssues = IdentifyPerformanceIssues(planSteps),
            OptimizationSuggestions = GenerateOptimizationSuggestions(planSteps),
            EstimatedComplexity = CalculateComplexity(planSteps)
        };
        
        return analysis;
    }
    
    /// <summary>
    /// Gets the detailed execution plan from SQLite
    /// </summary>
    private List<QueryPlanStep> GetQueryPlan(SqliteConnection connection, string sql, Dictionary<string, object?> parameters)
    {
        var planSteps = new List<QueryPlanStep>();
        
        try
        {
            // Get the query plan using EXPLAIN QUERY PLAN
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"EXPLAIN QUERY PLAN {sql}";
            
            // Bind parameters if any
            foreach (var param in parameters)
            {
                cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
            }
            
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var step = new QueryPlanStep
                {
                    Id = reader.GetInt32(0),
                    Parent = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                    NotUsed = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2),
                    Detail = reader.GetString(3)
                };
                
                // Parse the detail string for additional information
                ParsePlanDetail(step);
                planSteps.Add(step);
            }
        }
        catch (Exception ex)
        {
            planSteps.Add(new QueryPlanStep
            {
                Id = -1,
                Detail = $"Error analyzing query plan: {ex.Message}",
                Operation = "ERROR",
                HasPerformanceIssue = true
            });
        }
        
        return planSteps;
    }
    
    /// <summary>
    /// Parses the plan detail string to extract structured information
    /// </summary>
    private static void ParsePlanDetail(QueryPlanStep step)
    {
        var detail = step.Detail.ToUpperInvariant();
        
        // Extract operation type
        if (detail.Contains("SCAN TABLE"))
        {
            step.Operation = "SCAN";
            step.TableName = ExtractTableName(detail, "SCAN TABLE");
            step.HasIndex = false;
        }
        else if (detail.Contains("SEARCH TABLE"))
        {
            step.Operation = "SEARCH";
            step.TableName = ExtractTableName(detail, "SEARCH TABLE");
            step.HasIndex = detail.Contains("USING INDEX") || detail.Contains("USING COVERING INDEX");
            step.IndexName = ExtractIndexName(detail);
        }
        else if (detail.Contains("USE TEMP B-TREE"))
        {
            step.Operation = "TEMP_SORT";
            step.HasPerformanceIssue = true; // Temp sorting can be expensive
        }
        else if (detail.Contains("EXECUTE LIST SUBQUERY"))
        {
            step.Operation = "SUBQUERY";
        }
        else if (detail.Contains("COMPOUND SUBQUERIES"))
        {
            step.Operation = "COMPOUND";
        }
        else
        {
            step.Operation = "OTHER";
        }
        
        // Check for performance indicators
        step.HasPerformanceIssue = step.HasPerformanceIssue || 
            detail.Contains("SCAN TABLE") && !detail.Contains("USING INDEX") ||
            detail.Contains("USE TEMP B-TREE FOR ORDER BY") ||
            detail.Contains("USE TEMP B-TREE FOR GROUP BY");
    }
    
    /// <summary>
    /// Extracts table name from plan detail
    /// </summary>
    private static string? ExtractTableName(string detail, string operation)
    {
        var index = detail.IndexOf(operation);
        if (index == -1) return null;
        
        var afterOperation = detail.Substring(index + operation.Length).Trim();
        var spaceIndex = afterOperation.IndexOf(' ');
        
        return spaceIndex == -1 ? afterOperation : afterOperation.Substring(0, spaceIndex);
    }
    
    /// <summary>
    /// Extracts index name from plan detail
    /// </summary>
    private static string? ExtractIndexName(string detail)
    {
        var usingIndex = "USING INDEX ";
        var coveringIndex = "USING COVERING INDEX ";
        
        var indexPos = detail.IndexOf(coveringIndex);
        if (indexPos != -1)
        {
            var afterIndex = detail.Substring(indexPos + coveringIndex.Length);
            var spaceIndex = afterIndex.IndexOf(' ');
            return spaceIndex == -1 ? afterIndex : afterIndex.Substring(0, spaceIndex);
        }
        
        indexPos = detail.IndexOf(usingIndex);
        if (indexPos != -1)
        {
            var afterIndex = detail.Substring(indexPos + usingIndex.Length);
            var spaceIndex = afterIndex.IndexOf(' ');
            return spaceIndex == -1 ? afterIndex : afterIndex.Substring(0, spaceIndex);
        }
        
        return null;
    }
    
    /// <summary>
    /// Identifies performance issues in the query plan
    /// </summary>
    private static List<PerformanceIssue> IdentifyPerformanceIssues(List<QueryPlanStep> planSteps)
    {
        var issues = new List<PerformanceIssue>();
        
        foreach (var step in planSteps)
        {
            // Full table scans without indexes
            if (step.Operation == "SCAN" && !step.HasIndex)
            {
                issues.Add(new PerformanceIssue
                {
                    Type = PerformanceIssueType.FullTableScan,
                    Severity = PerformanceIssueSeverity.High,
                    Description = $"Full table scan on '{step.TableName}' - consider adding an index",
                    TableName = step.TableName,
                    StepId = step.Id
                });
            }
            
            // Temporary sorting
            if (step.Operation == "TEMP_SORT")
            {
                issues.Add(new PerformanceIssue
                {
                    Type = PerformanceIssueType.TemporarySorting,
                    Severity = PerformanceIssueSeverity.Medium,
                    Description = "Query requires temporary sorting - consider adding an index for ORDER BY columns",
                    StepId = step.Id
                });
            }
            
            // Missing covering index opportunities
            if (step.Operation == "SEARCH" && step.HasIndex && step.IndexName != null && 
                !step.Detail.ToUpperInvariant().Contains("COVERING"))
            {
                issues.Add(new PerformanceIssue
                {
                    Type = PerformanceIssueType.MissingCoveringIndex,
                    Severity = PerformanceIssueSeverity.Low,
                    Description = $"Index '{step.IndexName}' could be optimized as a covering index",
                    TableName = step.TableName,
                    IndexName = step.IndexName,
                    StepId = step.Id
                });
            }
        }
        
        return issues;
    }
    
    /// <summary>
    /// Generates optimization suggestions based on query plan analysis
    /// </summary>
    private static List<OptimizationSuggestion> GenerateOptimizationSuggestions(List<QueryPlanStep> planSteps)
    {
        var suggestions = new List<OptimizationSuggestion>();
        var tablesScanned = new HashSet<string>();
        
        foreach (var step in planSteps.Where(s => s.Operation == "SCAN" && !s.HasIndex))
        {
            if (step.TableName != null && tablesScanned.Add(step.TableName))
            {
                suggestions.Add(new OptimizationSuggestion
                {
                    Type = OptimizationType.AddIndex,
                    Priority = OptimizationPriority.High,
                    Description = $"Add index to table '{step.TableName}' to avoid full table scan",
                    TableName = step.TableName,
                    EstimatedImpact = "High - Can significantly reduce query execution time",
                    SqlSuggestion = $"-- Analyze WHERE conditions to determine best index columns\n-- CREATE INDEX idx_{step.TableName}_[columns] ON \"{step.TableName}\" ([column_list]);"
                });
            }
        }
        
        // Suggest covering indexes for frequently accessed tables
        var searchSteps = planSteps.Where(s => s.Operation == "SEARCH" && s.HasIndex).ToList();
        foreach (var step in searchSteps)
        {
            if (step.TableName != null && step.IndexName != null)
            {
                suggestions.Add(new OptimizationSuggestion
                {
                    Type = OptimizationType.OptimizeIndex,
                    Priority = OptimizationPriority.Medium,
                    Description = $"Consider creating a covering index for table '{step.TableName}'",
                    TableName = step.TableName,
                    IndexName = step.IndexName,
                    EstimatedImpact = "Medium - Can reduce I/O by including all needed columns in index",
                    SqlSuggestion = $"-- CREATE INDEX idx_{step.TableName}_covering ON \"{step.TableName}\" ([where_columns], [select_columns]);"
                });
            }
        }
        
        // Suggest query rewriting for complex operations
        if (planSteps.Any(s => s.Operation == "TEMP_SORT"))
        {
            suggestions.Add(new OptimizationSuggestion
            {
                Type = OptimizationType.QueryRewrite,
                Priority = OptimizationPriority.Medium,
                Description = "Consider rewriting query to eliminate temporary sorting",
                EstimatedImpact = "Medium - Can reduce memory usage and improve performance",
                SqlSuggestion = "-- Add composite index on ORDER BY columns\n-- Or restructure query to use existing indexes"
            });
        }
        
        return suggestions;
    }
    
    /// <summary>
    /// Calculates query complexity based on plan analysis
    /// </summary>
    private static QueryComplexity CalculateComplexity(List<QueryPlanStep> planSteps)
    {
        var complexity = QueryComplexity.Simple;
        var complexityScore = 0;
        
        foreach (var step in planSteps)
        {
            switch (step.Operation)
            {
                case "SCAN":
                    complexityScore += step.HasIndex ? 2 : 5; // Full scans are expensive
                    break;
                case "SEARCH":
                    complexityScore += 1;
                    break;
                case "TEMP_SORT":
                    complexityScore += 3;
                    break;
                case "SUBQUERY":
                    complexityScore += 4;
                    break;
                case "COMPOUND":
                    complexityScore += 3;
                    break;
                default:
                    complexityScore += 1;
                    break;
            }
        }
        
        // Determine complexity level
        if (complexityScore >= 15)
            complexity = QueryComplexity.VeryComplex;
        else if (complexityScore >= 10)
            complexity = QueryComplexity.Complex;
        else if (complexityScore >= 5)
            complexity = QueryComplexity.Moderate;
        
        return complexity;
    }
}

/// <summary>
/// Result of query plan analysis
/// </summary>
public sealed record QueryPlanAnalysis
{
    /// <summary>
    /// Original SQL query
    /// </summary>
    public string Query { get; init; } = string.Empty;
    
    /// <summary>
    /// Query parameters
    /// </summary>
    public Dictionary<string, object?> Parameters { get; init; } = new();
    
    /// <summary>
    /// Individual steps in the query execution plan
    /// </summary>
    public List<QueryPlanStep> PlanSteps { get; init; } = new();
    
    /// <summary>
    /// Identified performance issues
    /// </summary>
    public List<PerformanceIssue> PerformanceIssues { get; init; } = new();
    
    /// <summary>
    /// Optimization suggestions
    /// </summary>
    public List<OptimizationSuggestion> OptimizationSuggestions { get; init; } = new();
    
    /// <summary>
    /// Overall query complexity assessment
    /// </summary>
    public QueryComplexity EstimatedComplexity { get; init; } = QueryComplexity.Simple;
    
    /// <summary>
    /// Gets a summary of the analysis
    /// </summary>
    public string GetSummary()
    {
        var issues = PerformanceIssues.Count;
        var suggestions = OptimizationSuggestions.Count;
        
        return $"Query complexity: {EstimatedComplexity}, " +
               $"Performance issues: {issues}, " +
               $"Optimization suggestions: {suggestions}";
    }
}

/// <summary>
/// Individual step in query execution plan
/// </summary>
public sealed record QueryPlanStep
{
    /// <summary>
    /// Step identifier
    /// </summary>
    public int Id { get; init; }
    
    /// <summary>
    /// Parent step identifier
    /// </summary>
    public int? Parent { get; init; }
    
    /// <summary>
    /// Not used field from EXPLAIN QUERY PLAN
    /// </summary>
    public int? NotUsed { get; init; }
    
    /// <summary>
    /// Detailed description of the step
    /// </summary>
    public string Detail { get; init; } = string.Empty;
    
    /// <summary>
    /// Parsed operation type
    /// </summary>
    public string Operation { get; set; } = string.Empty;
    
    /// <summary>
    /// Table name involved in this step
    /// </summary>
    public string? TableName { get; set; }
    
    /// <summary>
    /// Index name used (if any)
    /// </summary>
    public string? IndexName { get; set; }
    
    /// <summary>
    /// Whether this step uses an index
    /// </summary>
    public bool HasIndex { get; set; }
    
    /// <summary>
    /// Whether this step has potential performance issues
    /// </summary>
    public bool HasPerformanceIssue { get; set; }
}

/// <summary>
/// Performance issue identified in query plan
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
    public PerformanceIssueSeverity Severity { get; init; }
    
    /// <summary>
    /// Description of the issue
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// Table name affected
    /// </summary>
    public string? TableName { get; init; }
    
    /// <summary>
    /// Index name related to the issue
    /// </summary>
    public string? IndexName { get; init; }
    
    /// <summary>
    /// Query plan step ID
    /// </summary>
    public int StepId { get; init; }
}

/// <summary>
/// Types of performance issues
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PerformanceIssueType
{
    FullTableScan,
    TemporarySorting,
    MissingCoveringIndex,
    SuboptimalJoin,
    UnusedIndex,
    ExpensiveFunction
}

/// <summary>
/// Severity levels for performance issues
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PerformanceIssueSeverity
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Optimization suggestion
/// </summary>
public sealed record OptimizationSuggestion
{
    /// <summary>
    /// Type of optimization
    /// </summary>
    public OptimizationType Type { get; init; }
    
    /// <summary>
    /// Priority of implementing this optimization
    /// </summary>
    public OptimizationPriority Priority { get; init; }
    
    /// <summary>
    /// Description of the suggestion
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// Table name affected
    /// </summary>
    public string? TableName { get; init; }
    
    /// <summary>
    /// Index name related to the suggestion
    /// </summary>
    public string? IndexName { get; init; }
    
    /// <summary>
    /// Estimated impact of implementing this suggestion
    /// </summary>
    public string EstimatedImpact { get; init; } = string.Empty;
    
    /// <summary>
    /// Suggested SQL for implementing the optimization
    /// </summary>
    public string SqlSuggestion { get; init; } = string.Empty;
}

/// <summary>
/// Types of optimizations
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OptimizationType
{
    AddIndex,
    OptimizeIndex,
    QueryRewrite,
    StatisticsUpdate,
    SchemaChange
}

/// <summary>
/// Priority levels for optimizations
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OptimizationPriority
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Query complexity levels
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QueryComplexity
{
    Simple,
    Moderate,
    Complex,
    VeryComplex
}