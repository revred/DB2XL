using Microsoft.Data.Sqlite;
using DB2XL.Core.Models;

namespace DB2XL.Data.Analysis;

/// <summary>
/// Comprehensive database performance analysis service
/// </summary>
public sealed class PerformanceAnalysisService
{
    private readonly QueryExecutionPlanAnalyzer _queryAnalyzer;
    private readonly GraphAnalysisEngine _graphEngine;

    public PerformanceAnalysisService()
    {
        _queryAnalyzer = new QueryExecutionPlanAnalyzer();
        _graphEngine = new GraphAnalysisEngine();
    }

    /// <summary>
    /// Performs comprehensive performance analysis of database queries
    /// </summary>
    public async Task<DatabasePerformanceAnalysis> AnalyzeDatabasePerformanceAsync(
        SqliteConnection connection,
        PerformanceAnalysisOptions? options = null)
    {
        options ??= new PerformanceAnalysisOptions();

        // Get database graph for context
        var databaseGraph = await _graphEngine.AnalyzeDatabaseAsync(connection, new GraphAnalysisOptions());
        
        // Analyze table statistics
        var tableStatistics = await AnalyzeTableStatisticsAsync(connection, options);
        
        // Analyze existing indexes
        var indexAnalysis = await AnalyzeIndexesAsync(connection, databaseGraph.Nodes.Keys);
        
        // Analyze common query patterns if provided
        var queryAnalyses = options.CommonQueries.Count > 0 
            ? await AnalyzeQueriesAsync(connection, options.CommonQueries)
            : Array.Empty<QueryExecutionPlan>();

        // Create the analysis object with all properties initialized
        var analysis = new DatabasePerformanceAnalysis
        {
            DatabasePath = connection.DataSource,
            AnalysisTimestamp = DateTime.UtcNow,
            Options = options,
            DatabaseGraph = databaseGraph,
            TableStatistics = tableStatistics,
            IndexAnalysis = indexAnalysis,
            QueryAnalyses = queryAnalyses
        };

        // Generate performance recommendations and calculate score
        var recommendations = GeneratePerformanceRecommendations(analysis);
        var overallScore = CalculateOverallPerformanceScore(analysis);

        // Return final analysis with recommendations and score
        return new DatabasePerformanceAnalysis
        {
            DatabasePath = analysis.DatabasePath,
            AnalysisTimestamp = analysis.AnalysisTimestamp,
            Options = analysis.Options,
            DatabaseGraph = analysis.DatabaseGraph,
            TableStatistics = analysis.TableStatistics,
            IndexAnalysis = analysis.IndexAnalysis,
            QueryAnalyses = analysis.QueryAnalyses,
            Recommendations = recommendations,
            OverallScore = overallScore
        };
    }

    /// <summary>
    /// Analyzes a specific query for performance issues
    /// </summary>
    public async Task<QueryExecutionPlan> AnalyzeQueryPerformanceAsync(
        SqliteConnection connection,
        string query)
    {
        return await _queryAnalyzer.AnalyzeQueryAsync(connection, query);
    }

    /// <summary>
    /// Analyzes table statistics for performance insights
    /// </summary>
    private async Task<IReadOnlyList<TablePerformanceStatistics>> AnalyzeTableStatisticsAsync(
        SqliteConnection connection,
        PerformanceAnalysisOptions options)
    {
        var statistics = new List<TablePerformanceStatistics>();
        var tables = await GetAllTablesAsync(connection);

        foreach (var tableName in tables)
        {
            try
            {
                var stats = await AnalyzeTablePerformanceAsync(connection, tableName, options);
                statistics.Add(stats);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to analyze table {tableName}: {ex.Message}");
            }
        }

        return statistics;
    }

    /// <summary>
    /// Analyzes performance statistics for a single table
    /// </summary>
    private async Task<TablePerformanceStatistics> AnalyzeTablePerformanceAsync(
        SqliteConnection connection,
        string tableName,
        PerformanceAnalysisOptions options)
    {
        // Get basic table information
        var rowCount = await GetTableRowCountAsync(connection, tableName);
        var columnCount = await GetTableColumnCountAsync(connection, tableName);
        
        // Get storage information
        var storageInfo = await GetTableStorageInfoAsync(connection, tableName);
        
        // Analyze column statistics if requested
        var columnStatistics = options.AnalyzeColumnCardinality 
            ? await AnalyzeColumnStatisticsAsync(connection, tableName)
            : Array.Empty<ColumnPerformanceStatistics>();

        // Identify performance issues for this table
        var performanceIssues = await IdentifyTablePerformanceIssuesAsync(connection, tableName, rowCount);

        return new TablePerformanceStatistics
        {
            TableName = tableName,
            RowCount = rowCount,
            ColumnCount = columnCount,
            StorageInfo = storageInfo,
            ColumnStatistics = columnStatistics,
            PerformanceIssues = performanceIssues
        };
    }

    /// <summary>
    /// Gets the row count for a specific table
    /// </summary>
    private async Task<long> GetTableRowCountAsync(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{tableName.Replace("\"", "\"\"")}\"";
        
        var result = await command.ExecuteScalarAsync();
        return result is long count ? count : Convert.ToInt64(result);
    }

    /// <summary>
    /// Gets the column count for a specific table
    /// </summary>
    private async Task<int> GetTableColumnCountAsync(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")";
        
        var columns = 0;
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns++;
        }
        
        return columns;
    }

    /// <summary>
    /// Gets storage information for a table
    /// </summary>
    private async Task<TableStorageInfo> GetTableStorageInfoAsync(SqliteConnection connection, string tableName)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA page_count";
            var totalPages = Convert.ToInt64(await command.ExecuteScalarAsync());

            // Get approximate table size using ANALYZE if available
            command.CommandText = $"SELECT COUNT(*) FROM \"{tableName.Replace("\"", "\"\"")}\"";
            var rowCount = Convert.ToInt64(await command.ExecuteScalarAsync());

            if (rowCount == 0)
            {
                return new TableStorageInfo
                {
                    EstimatedSizeBytes = 0,
                    EstimatedPageCount = 0
                };
            }

            // Rough estimation: assume even distribution across pages
            var estimatedTablePages = Math.Max(1, totalPages / 10); // Conservative estimate
            var estimatedSizeBytes = estimatedTablePages * 4096; // SQLite page size

            return new TableStorageInfo
            {
                EstimatedSizeBytes = estimatedSizeBytes,
                EstimatedPageCount = estimatedTablePages
            };
        }
        catch
        {
            return new TableStorageInfo
            {
                EstimatedSizeBytes = 0,
                EstimatedPageCount = 0
            };
        }
    }

    /// <summary>
    /// Analyzes column statistics for cardinality analysis
    /// </summary>
    private async Task<IReadOnlyList<ColumnPerformanceStatistics>> AnalyzeColumnStatisticsAsync(
        SqliteConnection connection, 
        string tableName)
    {
        var statistics = new List<ColumnPerformanceStatistics>();

        try
        {
            // Get column information
            var columns = await GetTableColumnsAsync(connection, tableName);
            var totalRows = await GetTableRowCountAsync(connection, tableName);

            foreach (var (columnName, dataType) in columns)
            {
                try
                {
                    var distinctCount = await GetColumnDistinctCountAsync(connection, tableName, columnName);
                    var nullCount = await GetColumnNullCountAsync(connection, tableName, columnName);
                    var selectivity = totalRows > 0 ? (double)distinctCount / totalRows : 0;
                    var indexCandidate = selectivity > 0.1 && selectivity < 0.9; // Good selectivity for indexing

                    statistics.Add(new ColumnPerformanceStatistics
                    {
                        ColumnName = columnName,
                        DataType = dataType,
                        DistinctValueCount = distinctCount,
                        NullCount = nullCount,
                        Selectivity = selectivity,
                        IndexCandidate = indexCandidate
                    });
                }
                catch
                {
                    // If individual column analysis fails, add basic info
                    statistics.Add(new ColumnPerformanceStatistics
                    {
                        ColumnName = columnName,
                        DataType = dataType,
                        DistinctValueCount = 0,
                        NullCount = 0,
                        Selectivity = 0,
                        IndexCandidate = false
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to analyze column statistics for {tableName}: {ex.Message}");
        }

        return statistics;
    }

    /// <summary>
    /// Gets column information for a table
    /// </summary>
    private async Task<List<(string Name, string Type)>> GetTableColumnsAsync(SqliteConnection connection, string tableName)
    {
        var columns = new List<(string, string)>();
        
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")";
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1); // name column
            var type = reader.GetString(2); // type column
            columns.Add((name, type));
        }
        
        return columns;
    }

    /// <summary>
    /// Gets distinct value count for a column
    /// </summary>
    private async Task<long> GetColumnDistinctCountAsync(SqliteConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(DISTINCT \"{columnName.Replace("\"", "\"\"")}\") FROM \"{tableName.Replace("\"", "\"\"")}\"";
        
        var result = await command.ExecuteScalarAsync();
        return result is long count ? count : Convert.ToInt64(result);
    }

    /// <summary>
    /// Gets null count for a column
    /// </summary>
    private async Task<long> GetColumnNullCountAsync(SqliteConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{tableName.Replace("\"", "\"\"")}\" WHERE \"{columnName.Replace("\"", "\"\"")}\" IS NULL";
        
        var result = await command.ExecuteScalarAsync();
        return result is long count ? count : Convert.ToInt64(result);
    }

    /// <summary>
    /// Analyzes existing indexes and generates recommendations
    /// </summary>
    private async Task<IndexAnalysisResult> AnalyzeIndexesAsync(SqliteConnection connection, IEnumerable<string> tableNames)
    {
        var existingIndexes = await GetExistingIndexesAsync(connection);
        var missingIndexRecommendations = await GenerateMissingIndexRecommendationsAsync(connection, tableNames.ToList());
        var overallIndexHealth = CalculateIndexHealth(existingIndexes, missingIndexRecommendations);

        return new IndexAnalysisResult
        {
            ExistingIndexes = existingIndexes,
            MissingIndexRecommendations = missingIndexRecommendations,
            OverallIndexHealth = overallIndexHealth
        };
    }

    /// <summary>
    /// Gets information about existing indexes
    /// </summary>
    private async Task<IReadOnlyList<IndexStatistics>> GetExistingIndexesAsync(SqliteConnection connection)
    {
        var indexes = new List<IndexStatistics>();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT name, tbl_name, sql 
            FROM sqlite_master 
            WHERE type = 'index' 
            AND name NOT LIKE 'sqlite_%'
            ORDER BY name";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var indexName = reader.GetString(0); // name column
            var tableName = reader.GetString(1); // tbl_name column  
            var sql = reader.IsDBNull(2) ? "" : reader.GetString(2); // sql column

            var columns = await GetIndexColumnsAsync(connection, indexName);
            var isUnique = sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);

            indexes.Add(new IndexStatistics
            {
                IndexName = indexName,
                TableName = tableName,
                IsUnique = isUnique,
                Columns = columns,
                EstimatedSelectivity = 0.5, // Default - could be enhanced with actual statistics
                Usage = IndexUsageFrequency.Unknown
            });
        }

        return indexes;
    }

    /// <summary>
    /// Gets columns for a specific index
    /// </summary>
    private async Task<IReadOnlyList<string>> GetIndexColumnsAsync(SqliteConnection connection, string indexName)
    {
        var columns = new List<string>();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA index_info(\"{indexName.Replace("\"", "\"\"")}\")";

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var columnName = reader.GetString(2); // name column from PRAGMA index_info
                columns.Add(columnName);
            }
        }
        catch
        {
            // If PRAGMA fails, return empty list
        }

        return columns;
    }

    /// <summary>
    /// Generates recommendations for missing indexes
    /// </summary>
    private async Task<IReadOnlyList<MissingIndexRecommendation>> GenerateMissingIndexRecommendationsAsync(
        SqliteConnection connection, 
        IReadOnlyList<string> tableNames)
    {
        var recommendations = new List<MissingIndexRecommendation>();

        foreach (var tableName in tableNames)
        {
            // Check for foreign key candidates
            var foreignKeys = await GetForeignKeyColumnsAsync(connection, tableName);
            foreach (var fkColumn in foreignKeys)
            {
                var hasIndex = await HasIndexOnColumnAsync(connection, tableName, fkColumn);
                if (!hasIndex)
                {
                    recommendations.Add(new MissingIndexRecommendation
                    {
                        TableName = tableName,
                        RecommendedColumns = new[] { fkColumn },
                        Reason = IndexRecommendationReason.ForeignKeyCandidate,
                        Priority = RecommendationPriority.High,
                        EstimatedBenefit = 0.8
                    });
                }
            }
        }

        return recommendations;
    }

    /// <summary>
    /// Gets foreign key columns for a table
    /// </summary>
    private async Task<List<string>> GetForeignKeyColumnsAsync(SqliteConnection connection, string tableName)
    {
        var foreignKeys = new List<string>();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA foreign_key_list(\"{tableName.Replace("\"", "\"\"")}\")";

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var fromColumn = reader.GetString(3); // from column from PRAGMA foreign_key_list
                foreignKeys.Add(fromColumn);
            }
        }
        catch
        {
            // If PRAGMA fails, return empty list
        }

        return foreignKeys;
    }

    /// <summary>
    /// Checks if a table has an index on a specific column
    /// </summary>
    private async Task<bool> HasIndexOnColumnAsync(SqliteConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*)
            FROM sqlite_master sm
            JOIN pragma_index_list(sm.tbl_name) pil ON sm.name = pil.name
            JOIN pragma_index_info(pil.name) pii ON pil.name = pii.name
            WHERE sm.type = 'index' 
            AND sm.tbl_name = @tableName 
            AND pii.name = @columnName";

        command.Parameters.AddWithValue("@tableName", tableName);
        command.Parameters.AddWithValue("@columnName", columnName);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    /// <summary>
    /// Calculates overall index health score
    /// </summary>
    private double CalculateIndexHealth(
        IReadOnlyList<IndexStatistics> existingIndexes,
        IReadOnlyList<MissingIndexRecommendation> missingRecommendations)
    {
        if (existingIndexes.Count == 0 && missingRecommendations.Count == 0)
            return 0.5; // Neutral if no data

        var totalRecommendations = missingRecommendations.Count;
        var existingCount = existingIndexes.Count;

        // Higher score means better index health
        // Formula: existing indexes boost score, missing high-priority indexes reduce it
        var baseScore = 0.5;
        var indexBonus = Math.Min(0.3, existingCount * 0.1);
        var missingPenalty = Math.Min(0.4, totalRecommendations * 0.1);

        return Math.Max(0.0, Math.Min(1.0, baseScore + indexBonus - missingPenalty));
    }

    /// <summary>
    /// Analyzes multiple queries for performance
    /// </summary>
    private async Task<IReadOnlyList<QueryExecutionPlan>> AnalyzeQueriesAsync(
        SqliteConnection connection,
        IReadOnlyList<string> queries)
    {
        var analyses = new List<QueryExecutionPlan>();

        foreach (var query in queries)
        {
            try
            {
                var analysis = await _queryAnalyzer.AnalyzeQueryAsync(connection, query);
                analyses.Add(analysis);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to analyze query: {ex.Message}");
            }
        }

        return analyses;
    }

    /// <summary>
    /// Identifies performance issues for a specific table
    /// </summary>
    private async Task<IReadOnlyList<PerformanceIssue>> IdentifyTablePerformanceIssuesAsync(
        SqliteConnection connection,
        string tableName,
        long rowCount)
    {
        var issues = new List<PerformanceIssue>();

        // Check for large table without proper indexing
        if (rowCount > 10000)
        {
            var indexCount = await GetTableIndexCountAsync(connection, tableName);
            if (indexCount < 2) // Only primary key
            {
                issues.Add(new PerformanceIssue
                {
                    Type = PerformanceIssueType.MissingIndex,
                    Severity = IssueSeverity.Major,
                    Description = $"Large table '{tableName}' ({rowCount:N0} rows) may benefit from additional indexes",
                    AffectedTables = new[] { tableName },
                    ImpactScore = 0.8
                });
            }
        }

        return issues;
    }

    /// <summary>
    /// Gets the number of indexes for a table
    /// </summary>
    private async Task<int> GetTableIndexCountAsync(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*) 
            FROM sqlite_master 
            WHERE type = 'index' 
            AND tbl_name = @tableName 
            AND name NOT LIKE 'sqlite_%'";

        command.Parameters.AddWithValue("@tableName", tableName);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Gets all table names in the database
    /// </summary>
    private async Task<List<string>> GetAllTablesAsync(SqliteConnection connection)
    {
        var tables = new List<string>();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT name 
            FROM sqlite_master 
            WHERE type = 'table' 
            AND name NOT LIKE 'sqlite_%'
            ORDER BY name";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0)); // name column
        }

        return tables;
    }

    /// <summary>
    /// Generates comprehensive performance recommendations
    /// </summary>
    private IReadOnlyList<OptimizationRecommendation> GeneratePerformanceRecommendations(
        DatabasePerformanceAnalysis analysis)
    {
        var recommendations = new List<OptimizationRecommendation>();

        // Add index recommendations from index analysis
        foreach (var missingIndex in analysis.IndexAnalysis.MissingIndexRecommendations)
        {
            recommendations.Add(new OptimizationRecommendation
            {
                Type = OptimizationType.CreateIndex,
                Priority = missingIndex.Priority,
                Title = $"Create index on {missingIndex.TableName}",
                Description = $"Create index on {missingIndex.TableName}({string.Join(", ", missingIndex.RecommendedColumns)})",
                EstimatedImprovement = missingIndex.EstimatedBenefit,
                ImplementationSql = $"CREATE INDEX idx_{missingIndex.TableName}_{string.Join("_", missingIndex.RecommendedColumns)} ON {missingIndex.TableName} ({string.Join(", ", missingIndex.RecommendedColumns)})",
                AffectedTables = new[] { missingIndex.TableName },
                AffectedColumns = missingIndex.RecommendedColumns.ToList()
            });
        }

        // Add recommendations from query analysis
        foreach (var queryAnalysis in analysis.QueryAnalyses)
        {
            recommendations.AddRange(queryAnalysis.Recommendations);
        }

        return recommendations.OrderByDescending(r => r.Priority).ThenByDescending(r => r.EstimatedImprovement).ToList();
    }

    /// <summary>
    /// Calculates overall database performance score
    /// </summary>
    private double CalculateOverallPerformanceScore(DatabasePerformanceAnalysis analysis)
    {
        var factors = new List<(double weight, double score)>();

        // Index health contributes 40% to overall score
        factors.Add((0.4, analysis.IndexAnalysis.OverallIndexHealth));

        // Query performance contributes 30% (if queries were analyzed)
        if (analysis.QueryAnalyses.Count > 0)
        {
            var avgQueryScore = analysis.QueryAnalyses.Average(q => GradeToScore(q.Metrics.Grade));
            factors.Add((0.3, avgQueryScore));
        }

        // Table structure health contributes 30%
        var tableHealthScore = CalculateTableHealthScore(analysis.TableStatistics);
        factors.Add((0.3, tableHealthScore));

        // Calculate weighted average
        var totalWeight = factors.Sum(f => f.weight);
        var weightedSum = factors.Sum(f => f.weight * f.score);

        return totalWeight > 0 ? weightedSum / totalWeight : 0.5;
    }

    /// <summary>
    /// Converts performance grade to numeric score
    /// </summary>
    private double GradeToScore(PerformanceGrade grade)
    {
        return grade switch
        {
            PerformanceGrade.Excellent => 1.0,
            PerformanceGrade.Good => 0.8,
            PerformanceGrade.Fair => 0.6,
            PerformanceGrade.Poor => 0.4,
            PerformanceGrade.Terrible => 0.2,
            _ => 0.5
        };
    }

    /// <summary>
    /// Calculates table structure health score
    /// </summary>
    private double CalculateTableHealthScore(IReadOnlyList<TablePerformanceStatistics> tableStats)
    {
        if (tableStats.Count == 0) return 0.5;

        var scores = new List<double>();

        foreach (var table in tableStats)
        {
            var tableScore = 1.0;

            // Penalize tables with high-severity performance issues
            var criticalIssues = table.PerformanceIssues.Count(i => i.Severity == IssueSeverity.Critical);
            var majorIssues = table.PerformanceIssues.Count(i => i.Severity == IssueSeverity.Major);
            
            tableScore -= (criticalIssues * 0.3) + (majorIssues * 0.2);
            
            scores.Add(Math.Max(0.0, tableScore));
        }

        return scores.Average();
    }
}