using Microsoft.Data.Sqlite;
using DB2XL.Core.Models;
using DB2XL.Core.Enums;
using System.Diagnostics;

namespace DB2XL.Data.Analysis;

/// <summary>
/// Main engine for analyzing database relationships and building graph structures
/// </summary>
public sealed class GraphAnalysisEngine
{
    private readonly ForeignKeyDiscoveryService _foreignKeyService;
    private readonly RelationshipScorer _relationshipScorer;
    private readonly RelationshipValidator _relationshipValidator;

    public GraphAnalysisEngine()
    {
        _foreignKeyService = new ForeignKeyDiscoveryService();
        _relationshipScorer = new RelationshipScorer();
        _relationshipValidator = new RelationshipValidator();
    }

    /// <summary>
    /// Analyzes a database and builds a complete relationship graph
    /// </summary>
    public async Task<DatabaseGraph> AnalyzeDatabaseAsync(
        SqliteConnection connection,
        GraphAnalysisOptions? options = null)
    {
        options ??= new GraphAnalysisOptions();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Discover all tables
            var tableNames = await DiscoverTablesAsync(connection, options);
            
            // Build graph nodes
            var nodes = await BuildGraphNodesAsync(connection, tableNames);
            
            // Discover relationships using multiple methods
            var relationships = await DiscoverAllRelationshipsAsync(connection, tableNames, options);
            
            // Score relationships
            var scoredRelationships = await _relationshipScorer.ScoreRelationshipsAsync(
                connection, relationships, options);
            
            // Validate and resolve conflicts
            var validatedRelationships = await _relationshipValidator.ValidateAndResolveConflictsAsync(
                connection, scoredRelationships, options);
            
            // Calculate graph statistics
            var statistics = CalculateGraphStatistics(nodes, validatedRelationships, stopwatch.ElapsedMilliseconds);
            
            // Convert nodes list to dictionary keyed by table name
            var nodesDictionary = nodes.ToDictionary(n => n.TableName, n => n);
            
            return new DatabaseGraph
            {
                Nodes = nodesDictionary,
                Edges = validatedRelationships,
                Statistics = statistics,
                Options = options
            };
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    /// <summary>
    /// Discovers all tables in the database based on options
    /// </summary>
    private async Task<IReadOnlyList<string>> DiscoverTablesAsync(
        SqliteConnection connection,
        GraphAnalysisOptions options)
    {
        var tableNames = new List<string>();
        
        using var command = connection.CreateCommand();
        var typeFilter = options.IncludeViews ? "('table', 'view')" : "('table')";
        
        command.CommandText = $@"
            SELECT name, type
            FROM sqlite_master
            WHERE type IN {typeFilter}
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name";
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var tableName = reader.GetString(0);
            
            // Apply include/exclude filters
            if (ShouldIncludeTable(tableName, options))
            {
                tableNames.Add(tableName);
            }
        }
        
        return tableNames;
    }
    
    /// <summary>
    /// Builds graph nodes with metadata for each table
    /// </summary>
    private async Task<IReadOnlyList<GraphNode>> BuildGraphNodesAsync(
        SqliteConnection connection,
        IReadOnlyList<string> tableNames)
    {
        var nodes = new List<GraphNode>();
        
        foreach (var tableName in tableNames)
        {
            try
            {
                var node = await BuildSingleGraphNodeAsync(connection, tableName);
                nodes.Add(node);
            }
            catch (Exception ex)
            {
                // Log warning but continue with other tables
                Console.WriteLine($"Warning: Failed to build node for table {tableName}: {ex.Message}");
            }
        }
        
        return nodes;
    }
    
    /// <summary>
    /// Builds a single graph node with complete metadata
    /// </summary>
    private async Task<GraphNode> BuildSingleGraphNodeAsync(
        SqliteConnection connection,
        string tableName)
    {
        // Get table info
        var columns = await GetTableColumnsAsync(connection, tableName);
        var rowCount = await GetRowCountAsync(connection, tableName);
        var primaryKey = ExtractPrimaryKeyInfo(columns);
        
        return new GraphNode(tableName, "table")
        {
            RowCount = rowCount,
            Columns = columns,
            PrimaryKey = primaryKey
        };
    }
    
    /// <summary>
    /// Gets column information for a table
    /// </summary>
    private async Task<IReadOnlyList<ColumnInfo>> GetTableColumnsAsync(
        SqliteConnection connection,
        string tableName)
    {
        var columns = new List<ColumnInfo>();
        
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")";
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var column = new ColumnInfo(
                reader.GetString(1),  // name
                reader.GetString(2),  // type
                reader.GetBoolean(3), // notnull
                reader.IsDBNull(4) ? null : reader.GetValue(4), // dflt_value
                reader.GetInt32(5) > 0 // pk
            );
            
            columns.Add(column);
        }
        
        return columns;
    }
    
    /// <summary>
    /// Gets approximate row count for a table
    /// </summary>
    private async Task<long?> GetRowCountAsync(SqliteConnection connection, string tableName)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{tableName.Replace("\"", "\"\"")}\"";
            
            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }
        catch
        {
            // If count fails (e.g., for views), return null
            return null;
        }
    }
    
    /// <summary>
    /// Extracts primary key information from columns
    /// </summary>
    private PrimaryKeyInfo? ExtractPrimaryKeyInfo(IReadOnlyList<ColumnInfo> columns)
    {
        var primaryKeyColumns = columns
            .Where(c => c.IsPrimaryKey)
            .OrderBy(c => c.Name)
            .ToList();
        
        if (primaryKeyColumns.Count == 0)
        {
            return null;
        }
        
        return new PrimaryKeyInfo
        {
            Strategy = PrimaryKeyStrategy.ExplicitPrimaryKey,
            Columns = primaryKeyColumns.Select(c => c.Name).ToList(),
            Description = primaryKeyColumns.Count > 1 ? "Composite primary key" : "Single column primary key",
            IsDeterministic = true
        };
    }
    
    /// <summary>
    /// Discovers relationships using all enabled methods
    /// </summary>
    private async Task<IReadOnlyList<GraphEdge>> DiscoverAllRelationshipsAsync(
        SqliteConnection connection,
        IReadOnlyList<string> tableNames,
        GraphAnalysisOptions options)
    {
        var allRelationships = new List<GraphEdge>();
        
        // Foreign key relationships
        if (options.AnalyzeForeignKeys)
        {
            var foreignKeyRelationships = await _foreignKeyService.DiscoverForeignKeysAsync(
                connection, tableNames);
            allRelationships.AddRange(foreignKeyRelationships);
        }
        
        // Naming pattern relationships
        if (options.InferFromNaming)
        {
            var namingPatternRelationships = await _foreignKeyService.DiscoverByNamingPatternsAsync(
                connection, tableNames, options.ForeignKeyPatterns);
            allRelationships.AddRange(namingPatternRelationships);
        }
        
        return allRelationships;
    }
    
    /// <summary>
    /// Calculates comprehensive graph statistics
    /// </summary>
    private GraphStatistics CalculateGraphStatistics(
        IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<GraphEdge> edges,
        long analysisDurationMs)
    {
        var nodeCount = nodes.Count;
        var edgeCount = edges.Count;
        
        // Count isolated nodes (nodes with no relationships)
        var connectedNodes = new HashSet<string>();
        foreach (var edge in edges)
        {
            connectedNodes.Add(edge.FromTable.ToLowerInvariant());
            connectedNodes.Add(edge.ToTable.ToLowerInvariant());
        }
        var isolatedNodeCount = nodeCount - connectedNodes.Count;
        
        // Group relationships by discovery method
        var relationshipsByMethod = edges
            .GroupBy(e => e.DiscoveryMethod.ToString())
            .ToDictionary(g => g.Key, g => g.Count());
        
        // Group relationships by type
        var relationshipsByType = edges
            .GroupBy(e => e.Type.ToString())
            .ToDictionary(g => g.Key, g => g.Count());
        
        // Calculate average confidence
        var avgConfidence = edges.Count > 0 ? edges.Average(e => e.ConfidenceScore) : 0.0;
        
        // Estimate connected components (simplified - assumes each connected group is one component)
        var connectedComponentCount = Math.Max(1, isolatedNodeCount + 
            (connectedNodes.Count > 0 ? 1 : 0));
        
        return new GraphStatistics
        {
            NodeCount = nodeCount,
            EdgeCount = edgeCount,
            IsolatedNodeCount = isolatedNodeCount,
            ConnectedComponentCount = connectedComponentCount,
            RelationshipsByMethod = relationshipsByMethod,
            RelationshipsByType = relationshipsByType,
            AverageConfidenceScore = avgConfidence,
            AnalysisDurationMs = analysisDurationMs
        };
    }
    
    /// <summary>
    /// Determines whether a table should be included in analysis
    /// </summary>
    private bool ShouldIncludeTable(string tableName, GraphAnalysisOptions options)
    {
        // Check include list first (if specified, only these tables are included)
        if (options.IncludeTables.Count > 0)
        {
            return options.IncludeTables.Any(pattern => 
                IsTableNameMatch(tableName, pattern));
        }
        
        // Check exclude list
        if (options.ExcludeTables.Count > 0)
        {
            return !options.ExcludeTables.Any(pattern => 
                IsTableNameMatch(tableName, pattern));
        }
        
        return true;
    }
    
    /// <summary>
    /// Checks if table name matches a pattern (supports basic wildcards)
    /// </summary>
    private bool IsTableNameMatch(string tableName, string pattern)
    {
        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            // Convert simple wildcard pattern to regex
            var regexPattern = pattern
                .Replace("*", ".*")
                .Replace("?", ".");
            
            return System.Text.RegularExpressions.Regex.IsMatch(
                tableName, $"^{regexPattern}$", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        
        return tableName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}

