using Microsoft.Data.Sqlite;
using DB2XL.Core.Models;
using System.Text.RegularExpressions;

namespace DB2XL.Data.Analysis;

/// <summary>
/// Service for analyzing SQLite query execution plans and identifying performance issues
/// </summary>
public sealed class QueryExecutionPlanAnalyzer
{
    private readonly Dictionary<string, ExecutionOperation> _operationPatterns;
    
    public QueryExecutionPlanAnalyzer()
    {
        _operationPatterns = InitializeOperationPatterns();
    }
    
    /// <summary>
    /// Analyzes a SQL query and returns performance analysis
    /// </summary>
    public async Task<QueryExecutionPlan> AnalyzeQueryAsync(
        SqliteConnection connection, 
        string query)
    {
        var rawSteps = await GetExecutionPlanAsync(connection, query);
        var parsedSteps = ParseExecutionSteps(rawSteps);
        var metrics = CalculatePerformanceMetrics(parsedSteps);
        var issues = IdentifyPerformanceIssues(parsedSteps, metrics);
        var recommendations = GenerateOptimizationRecommendations(parsedSteps, issues, metrics);
        
        return new QueryExecutionPlan
        {
            Query = query,
            Steps = parsedSteps,
            Metrics = metrics,
            Issues = issues,
            Recommendations = recommendations
        };
    }
    
    /// <summary>
    /// Gets raw execution plan from SQLite using EXPLAIN QUERY PLAN
    /// </summary>
    private async Task<List<RawExecutionStep>> GetExecutionPlanAsync(
        SqliteConnection connection, 
        string query)
    {
        var steps = new List<RawExecutionStep>();
        
        using var command = connection.CreateCommand();
        command.CommandText = $"EXPLAIN QUERY PLAN {query}";
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            steps.Add(new RawExecutionStep
            {
                Id = reader.GetInt32(0),      // id
                Parent = reader.GetInt32(1),  // parent
                NotUsed = reader.GetInt32(2), // notused
                Detail = reader.GetString(3)  // detail
            });
        }
        
        return steps;
    }
    
    /// <summary>
    /// Parses raw execution steps into structured execution steps
    /// </summary>
    private IReadOnlyList<ExecutionStep> ParseExecutionSteps(List<RawExecutionStep> rawSteps)
    {
        var steps = new List<ExecutionStep>();
        
        foreach (var rawStep in rawSteps)
        {
            var operation = DetermineOperation(rawStep.Detail);
            var tables = ExtractTables(rawStep.Detail);
            var indexUsages = ExtractIndexUsages(rawStep.Detail, tables);
            var cost = EstimateCost(rawStep.Detail, operation);
            var performance = AnalyzeStepPerformance(rawStep.Detail, operation, indexUsages);
            
            var step = new ExecutionStep
            {
                Id = rawStep.Id,
                Parent = rawStep.Parent,
                NotUsed = rawStep.NotUsed,
                Detail = rawStep.Detail,
                Operation = operation,
                Tables = tables,
                IndexUsages = indexUsages,
                EstimatedCost = cost,
                Performance = performance
            };
            
            steps.Add(step);
        }
        
        return steps;
    }
    
    /// <summary>
    /// Determines the execution operation type from the detail string
    /// </summary>
    private ExecutionOperation DetermineOperation(string detail)
    {
        var lowerDetail = detail.ToLowerInvariant();
        
        foreach (var (pattern, operation) in _operationPatterns)
        {
            if (lowerDetail.Contains(pattern))
            {
                return operation;
            }
        }
        
        return ExecutionOperation.Unknown;
    }
    
    /// <summary>
    /// Extracts table names from execution step detail
    /// </summary>
    private IReadOnlyList<string> ExtractTables(string detail)
    {
        var tables = new List<string>();
        
        // Match patterns like "SCAN TABLE tablename", "SEARCH TABLE tablename", "SCAN tablename" etc.
        var tableMatches = Regex.Matches(detail, @"(?:SCAN|SEARCH)(?:\s+TABLE)?\s+(\w+)", RegexOptions.IgnoreCase);
        foreach (Match match in tableMatches)
        {
            tables.Add(match.Groups[1].Value);
        }
        
        // Match patterns like "JOIN (tablename AS alias)"
        var joinMatches = Regex.Matches(detail, @"\((\w+)\s+AS\s+\w+\)", RegexOptions.IgnoreCase);
        foreach (Match match in joinMatches)
        {
            if (!tables.Contains(match.Groups[1].Value))
            {
                tables.Add(match.Groups[1].Value);
            }
        }
        
        return tables;
    }
    
    /// <summary>
    /// Extracts index usage information from execution step detail
    /// </summary>
    private IReadOnlyList<IndexUsage> ExtractIndexUsages(string detail, IReadOnlyList<string> tables)
    {
        var usages = new List<IndexUsage>();
        
        // Check for index usage patterns
        if (detail.Contains("USING INDEX", StringComparison.OrdinalIgnoreCase))
        {
            var indexMatch = Regex.Match(detail, @"USING\s+INDEX\s+(\w+)", RegexOptions.IgnoreCase);
            if (indexMatch.Success)
            {
                var indexName = indexMatch.Groups[1].Value;
                var usageType = DetermineIndexUsageType(indexName, detail);
                
                usages.Add(new IndexUsage
                {
                    IndexName = indexName,
                    UsageType = usageType,
                    IsFullyCovering = detail.Contains("COVERING", StringComparison.OrdinalIgnoreCase),
                    Selectivity = EstimateSelectivity(detail)
                });
            }
        }
        else if (detail.Contains("USING INTEGER PRIMARY KEY", StringComparison.OrdinalIgnoreCase))
        {
            usages.Add(new IndexUsage
            {
                IndexName = "PRIMARY KEY",
                UsageType = IndexUsageType.PrimaryKey,
                IsFullyCovering = true,
                Selectivity = 1.0
            });
        }
        else if ((detail.Contains("SCAN TABLE", StringComparison.OrdinalIgnoreCase) ||
                  detail.StartsWith("SCAN ", StringComparison.OrdinalIgnoreCase)) && 
                 !detail.Contains("USING", StringComparison.OrdinalIgnoreCase))
        {
            // Full table scan
            var tableName = tables.FirstOrDefault() ?? "unknown";
            usages.Add(new IndexUsage
            {
                IndexName = $"{tableName}_full_scan",
                UsageType = IndexUsageType.FullTableScan,
                IsFullyCovering = false,
                Selectivity = 0.0
            });
        }
        
        return usages;
    }
    
    /// <summary>
    /// Estimates the cost of an execution step
    /// </summary>
    private double EstimateCost(string detail, ExecutionOperation operation)
    {
        double baseCost = operation switch
        {
            ExecutionOperation.Scan => 100.0,
            ExecutionOperation.Search => 10.0,
            ExecutionOperation.Join => 50.0,
            ExecutionOperation.Sort => 75.0,
            ExecutionOperation.Group => 25.0,
            ExecutionOperation.Subquery => 200.0,
            _ => 20.0
        };
        
        // Adjust based on detail specifics
        if (detail.Contains("USING INTEGER PRIMARY KEY", StringComparison.OrdinalIgnoreCase))
            baseCost *= 0.1; // Primary key lookups are very fast
        else if (detail.Contains("USING INDEX", StringComparison.OrdinalIgnoreCase))
            baseCost *= 0.3; // Index usage reduces cost significantly
        else if (detail.Contains("SCAN TABLE", StringComparison.OrdinalIgnoreCase))
            baseCost *= 2.0; // Table scans are expensive
        
        return baseCost;
    }
    
    /// <summary>
    /// Analyzes performance characteristics of a step
    /// </summary>
    private StepPerformanceProfile AnalyzeStepPerformance(
        string detail, 
        ExecutionOperation operation, 
        IReadOnlyList<IndexUsage> indexUsages)
    {
        var isTableScan = indexUsages.Any(u => u.UsageType == IndexUsageType.FullTableScan);
        var hasIndexUsage = indexUsages.Any(u => u.UsageType != IndexUsageType.FullTableScan);
        
        var complexityScore = CalculateComplexityScore(operation, isTableScan, hasIndexUsage);
        var impact = DeterminePerformanceImpact(complexityScore, isTableScan);
        var estimatedRows = EstimateRowsProcessed(detail, operation, isTableScan);
        
        return new StepPerformanceProfile
        {
            IsTableScan = isTableScan,
            IsExpensive = complexityScore > 70 || isTableScan,
            ComplexityScore = complexityScore,
            Impact = impact,
            EstimatedRows = estimatedRows
        };
    }
    
    /// <summary>
    /// Calculates complexity score for a step
    /// </summary>
    private int CalculateComplexityScore(ExecutionOperation operation, bool isTableScan, bool hasIndexUsage)
    {
        int baseScore = operation switch
        {
            ExecutionOperation.Scan => isTableScan ? 90 : 30,
            ExecutionOperation.Search => hasIndexUsage ? 20 : 70,
            ExecutionOperation.Join => 60,
            ExecutionOperation.Sort => 50,
            ExecutionOperation.Group => 40,
            ExecutionOperation.Subquery => 80,
            _ => 30
        };
        
        if (isTableScan) baseScore += 30;
        if (hasIndexUsage) baseScore -= 20;
        
        return Math.Clamp(baseScore, 0, 100);
    }
    
    /// <summary>
    /// Determines performance impact level
    /// </summary>
    private PerformanceImpact DeterminePerformanceImpact(int complexityScore, bool isTableScan)
    {
        if (complexityScore >= 90 || isTableScan) return PerformanceImpact.Critical;
        if (complexityScore >= 70) return PerformanceImpact.High;
        if (complexityScore >= 40) return PerformanceImpact.Medium;
        return PerformanceImpact.Low;
    }
    
    /// <summary>
    /// Estimates rows processed by a step
    /// </summary>
    private long EstimateRowsProcessed(string detail, ExecutionOperation operation, bool isTableScan)
    {
        // This is a simplified estimation - in practice would use table statistics
        return operation switch
        {
            ExecutionOperation.Scan when isTableScan => 10000, // Assume large table
            ExecutionOperation.Scan => 100,
            ExecutionOperation.Search => 10,
            ExecutionOperation.Join => 1000,
            _ => 100
        };
    }
    
    /// <summary>
    /// Calculates overall performance metrics
    /// </summary>
    private PerformanceMetrics CalculatePerformanceMetrics(IReadOnlyList<ExecutionStep> steps)
    {
        var tableScanCount = steps.Count(s => s.Performance.IsTableScan);
        var indexUsageCount = steps.SelectMany(s => s.IndexUsages).Count(u => u.UsageType != IndexUsageType.FullTableScan);
        var joinCount = steps.Count(s => s.Operation == ExecutionOperation.Join);
        var totalComplexity = steps.Sum(s => s.Performance.ComplexityScore);
        var totalRows = steps.Sum(s => s.Performance.EstimatedRows);
        
        var grade = CalculatePerformanceGrade(totalComplexity, tableScanCount, indexUsageCount);
        var category = DeterminePerformanceCategory(grade, tableScanCount, totalRows);
        
        return new PerformanceMetrics
        {
            ComplexityScore = totalComplexity,
            TableScanCount = tableScanCount,
            IndexUsageCount = indexUsageCount,
            JoinCount = joinCount,
            EstimatedRowsProcessed = totalRows,
            Grade = grade,
            Category = category
        };
    }
    
    /// <summary>
    /// Calculates performance grade
    /// </summary>
    private PerformanceGrade CalculatePerformanceGrade(int totalComplexity, int tableScanCount, int indexUsageCount)
    {
        if (tableScanCount > 2 || totalComplexity > 300) return PerformanceGrade.Terrible;
        if (tableScanCount > 0 || totalComplexity > 200) return PerformanceGrade.Poor;
        if (totalComplexity > 100) return PerformanceGrade.Fair;
        if (indexUsageCount > 0 && totalComplexity <= 50) return PerformanceGrade.Excellent;
        return PerformanceGrade.Good;
    }
    
    /// <summary>
    /// Determines performance category
    /// </summary>
    private QueryPerformanceCategory DeterminePerformanceCategory(PerformanceGrade grade, int tableScanCount, long totalRows)
    {
        return grade switch
        {
            PerformanceGrade.Excellent => QueryPerformanceCategory.Fast,
            PerformanceGrade.Good => QueryPerformanceCategory.Moderate,
            PerformanceGrade.Fair => QueryPerformanceCategory.Moderate,
            PerformanceGrade.Poor => QueryPerformanceCategory.Slow,
            PerformanceGrade.Terrible => tableScanCount > 1 ? QueryPerformanceCategory.Critical : QueryPerformanceCategory.VerySlow,
            _ => QueryPerformanceCategory.Moderate
        };
    }
    
    /// <summary>
    /// Identifies performance issues in the execution plan
    /// </summary>
    private IReadOnlyList<PerformanceIssue> IdentifyPerformanceIssues(
        IReadOnlyList<ExecutionStep> steps, 
        PerformanceMetrics metrics)
    {
        var issues = new List<PerformanceIssue>();
        
        // Check for table scans
        var tableScanSteps = steps.Where(s => s.Performance.IsTableScan).ToList();
        var severity = tableScanSteps.Count > 2 ? IssueSeverity.Critical : IssueSeverity.Major;
        
        foreach (var step in tableScanSteps)
        {
            issues.Add(new PerformanceIssue
            {
                Type = PerformanceIssueType.TableScan,
                Severity = severity,
                Description = $"Full table scan detected on {string.Join(", ", step.Tables)}",
                AffectedSteps = new[] { step.Id },
                AffectedTables = step.Tables.ToList(),
                ImpactScore = step.Performance.ComplexityScore / 100.0
            });
        }
        
        // Check for missing indexes
        var searchSteps = steps.Where(s => s.Operation == ExecutionOperation.Search && 
                                     s.IndexUsages.Any(u => u.UsageType == IndexUsageType.FullTableScan)).ToList();
        foreach (var step in searchSteps)
        {
            issues.Add(new PerformanceIssue
            {
                Type = PerformanceIssueType.MissingIndex,
                Severity = IssueSeverity.Warning,
                Description = $"Search operation without suitable index on {string.Join(", ", step.Tables)}",
                AffectedSteps = new[] { step.Id },
                AffectedTables = step.Tables.ToList(),
                ImpactScore = 0.6
            });
        }
        
        // Check for potential cartesian products
        if (metrics.JoinCount > 1 && metrics.TableScanCount > 0)
        {
            issues.Add(new PerformanceIssue
            {
                Type = PerformanceIssueType.CartesianProduct,
                Severity = IssueSeverity.Critical,
                Description = "Potential cartesian product detected in multi-table JOIN",
                AffectedSteps = steps.Where(s => s.Operation == ExecutionOperation.Join).Select(s => s.Id).ToList(),
                AffectedTables = steps.SelectMany(s => s.Tables).Distinct().ToList(),
                ImpactScore = 1.0
            });
        }
        
        return issues;
    }
    
    /// <summary>
    /// Generates optimization recommendations
    /// </summary>
    private IReadOnlyList<OptimizationRecommendation> GenerateOptimizationRecommendations(
        IReadOnlyList<ExecutionStep> steps,
        IReadOnlyList<PerformanceIssue> issues,
        PerformanceMetrics metrics)
    {
        var recommendations = new List<OptimizationRecommendation>();
        
        // Recommend indexes for table scans
        foreach (var issue in issues.Where(i => i.Type == PerformanceIssueType.TableScan))
        {
            foreach (var table in issue.AffectedTables)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Type = OptimizationType.CreateIndex,
                    Priority = RecommendationPriority.High,
                    Title = $"Create index for table {table}",
                    Description = $"Add an index on frequently queried columns of table '{table}' to avoid full table scans",
                    ImplementationSql = $"-- Example: CREATE INDEX idx_{table}_common ON {table} (column1, column2);",
                    EstimatedImprovement = 0.7,
                    AffectedTables = new[] { table }
                });
            }
        }
        
        // Recommend query rewriting for complex subqueries
        var complexSteps = steps.Where(s => s.Performance.ComplexityScore > 80).ToList();
        foreach (var step in complexSteps)
        {
            if (step.Operation == ExecutionOperation.Subquery)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Type = OptimizationType.RewriteQuery,
                    Priority = RecommendationPriority.Medium,
                    Title = "Consider rewriting complex subquery",
                    Description = "Complex subquery detected. Consider rewriting as JOIN or using WITH clause",
                    EstimatedImprovement = 0.4,
                    AffectedTables = step.Tables.ToList()
                });
            }
        }
        
        return recommendations;
    }
    
    /// <summary>
    /// Initializes operation patterns for parsing
    /// </summary>
    private Dictionary<string, ExecutionOperation> InitializeOperationPatterns()
    {
        return new Dictionary<string, ExecutionOperation>
        {
            ["scan table"] = ExecutionOperation.Scan,
            ["scan"] = ExecutionOperation.Scan,  // Match simple "SCAN" patterns
            ["search table"] = ExecutionOperation.Search,
            ["use temp b-tree for join"] = ExecutionOperation.Join,
            ["use temp b-tree for order by"] = ExecutionOperation.Sort,
            ["use temp b-tree for group by"] = ExecutionOperation.Group,
            ["use temp b-tree for distinct"] = ExecutionOperation.Group,
            ["composite subqueries"] = ExecutionOperation.Subquery,
            ["execute scalar subquery"] = ExecutionOperation.Subquery,
            ["aggregate"] = ExecutionOperation.Aggregate,
            ["scalar subquery"] = ExecutionOperation.Aggregate, // COUNT(*) often shows as scalar subquery
            ["window"] = ExecutionOperation.Window,
            ["compound subqueries"] = ExecutionOperation.Union,
            ["join"] = ExecutionOperation.Join  // Match any JOIN patterns
        };
    }
    
    /// <summary>
    /// Determines index usage type
    /// </summary>
    private IndexUsageType DetermineIndexUsageType(string indexName, string detail)
    {
        if (indexName.Contains("primary", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("primary key", StringComparison.OrdinalIgnoreCase))
            return IndexUsageType.PrimaryKey;
        
        if (indexName.Contains("unique", StringComparison.OrdinalIgnoreCase))
            return IndexUsageType.UniqueIndex;
        
        if (detail.Contains("covering", StringComparison.OrdinalIgnoreCase))
            return IndexUsageType.CoveringIndex;
        
        return IndexUsageType.NonUniqueIndex;
    }
    
    /// <summary>
    /// Estimates selectivity of an index usage
    /// </summary>
    private double EstimateSelectivity(string detail)
    {
        // This is a simplified estimation
        if (detail.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase)) return 1.0;
        if (detail.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)) return 0.9;
        if (detail.Contains("=", StringComparison.OrdinalIgnoreCase)) return 0.1;
        if (detail.Contains("RANGE", StringComparison.OrdinalIgnoreCase)) return 0.3;
        return 0.5;
    }
}

/// <summary>
/// Raw execution step from EXPLAIN QUERY PLAN
/// </summary>
internal record RawExecutionStep
{
    public int Id { get; init; }
    public int Parent { get; init; }
    public int NotUsed { get; init; }
    public string Detail { get; init; } = string.Empty;
}