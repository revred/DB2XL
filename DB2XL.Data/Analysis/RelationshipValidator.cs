using Microsoft.Data.Sqlite;
using DB2XL.Core.Models;
using DB2XL.Core.Enums;

namespace DB2XL.Data.Analysis;

/// <summary>
/// Service for validating and resolving conflicts in database relationships
/// </summary>
public sealed class RelationshipValidator
{
    /// <summary>
    /// Validates relationships and resolves conflicts
    /// </summary>
    public async Task<IReadOnlyList<GraphEdge>> ValidateAndResolveConflictsAsync(
        SqliteConnection connection,
        IReadOnlyList<GraphEdge> relationships,
        GraphAnalysisOptions options)
    {
        var validatedRelationships = new List<GraphEdge>();
        var relationshipGroups = GroupRelationshipsByEndpoints(relationships);
        
        foreach (var group in relationshipGroups)
        {
            var resolvedRelationship = await ResolveConflictingRelationshipsAsync(connection, group.Value, options);
            if (resolvedRelationship != null)
            {
                validatedRelationships.Add(resolvedRelationship);
            }
        }
        
        return validatedRelationships;
    }
    
    /// <summary>
    /// Groups relationships by their table endpoints to identify conflicts
    /// </summary>
    private Dictionary<string, List<GraphEdge>> GroupRelationshipsByEndpoints(IReadOnlyList<GraphEdge> relationships)
    {
        var groups = new Dictionary<string, List<GraphEdge>>();
        
        foreach (var relationship in relationships)
        {
            var key = CreateEndpointKey(relationship.FromTable, relationship.ToTable);
            
            if (!groups.ContainsKey(key))
            {
                groups[key] = new List<GraphEdge>();
            }
            
            groups[key].Add(relationship);
        }
        
        return groups;
    }
    
    /// <summary>
    /// Resolves conflicting relationships between the same table pair
    /// </summary>
    private async Task<GraphEdge?> ResolveConflictingRelationshipsAsync(
        SqliteConnection connection,
        List<GraphEdge> conflictingRelationships,
        GraphAnalysisOptions options)
    {
        if (conflictingRelationships.Count == 1)
        {
            return await ValidateSingleRelationshipAsync(connection, conflictingRelationships[0], options);
        }
        
        // Apply conflict resolution strategy
        switch (options.ConflictResolutionStrategy)
        {
            case ConflictResolutionStrategy.HighestConfidence:
                return await SelectHighestConfidenceRelationshipAsync(connection, conflictingRelationships, options);
                
            case ConflictResolutionStrategy.PreferForeignKeys:
                return await PreferForeignKeyRelationshipsAsync(connection, conflictingRelationships, options);
                
            case ConflictResolutionStrategy.MostRestrictive:
                return await SelectMostRestrictiveRelationshipAsync(connection, conflictingRelationships, options);
                
            case ConflictResolutionStrategy.KeepAll:
                // For KeepAll, we need to merge compatible relationships or return the best one
                return await MergeCompatibleRelationshipsAsync(connection, conflictingRelationships, options);
                
            default:
                throw new ArgumentException($"Unknown conflict resolution strategy: {options.ConflictResolutionStrategy}");
        }
    }
    
    /// <summary>
    /// Validates a single relationship for correctness
    /// </summary>
    private async Task<GraphEdge?> ValidateSingleRelationshipAsync(
        SqliteConnection connection,
        GraphEdge relationship,
        GraphAnalysisOptions options)
    {
        // Check if both tables exist
        if (!await TableExistsAsync(connection, relationship.FromTable) ||
            !await TableExistsAsync(connection, relationship.ToTable))
        {
            return null; // Invalid relationship - table doesn't exist
        }
        
        // Check if columns exist
        if (!await ColumnsExistAsync(connection, relationship.FromTable, relationship.FromColumns) ||
            !await ColumnsExistAsync(connection, relationship.ToTable, relationship.ToColumns))
        {
            return null; // Invalid relationship - column doesn't exist
        }
        
        // Check minimum confidence threshold
        if (relationship.ConfidenceScore < options.MinimumConfidenceScore)
        {
            return null; // Below threshold
        }
        
        return relationship;
    }
    
    /// <summary>
    /// Selects relationship with highest confidence score
    /// </summary>
    private async Task<GraphEdge?> SelectHighestConfidenceRelationshipAsync(
        SqliteConnection connection,
        List<GraphEdge> relationships,
        GraphAnalysisOptions options)
    {
        var validRelationships = new List<GraphEdge>();
        
        foreach (var relationship in relationships)
        {
            var validated = await ValidateSingleRelationshipAsync(connection, relationship, options);
            if (validated != null)
            {
                validRelationships.Add(validated);
            }
        }
        
        return validRelationships
            .OrderByDescending(r => r.ConfidenceScore)
            .ThenBy(r => GetDiscoveryMethodPriority(r.DiscoveryMethod))
            .FirstOrDefault();
    }
    
    /// <summary>
    /// Prefers foreign key relationships over inferred ones
    /// </summary>
    private async Task<GraphEdge?> PreferForeignKeyRelationshipsAsync(
        SqliteConnection connection,
        List<GraphEdge> relationships,
        GraphAnalysisOptions options)
    {
        var validRelationships = new List<GraphEdge>();
        
        foreach (var relationship in relationships)
        {
            var validated = await ValidateSingleRelationshipAsync(connection, relationship, options);
            if (validated != null)
            {
                validRelationships.Add(validated);
            }
        }
        
        // First prefer foreign keys, then by confidence
        return validRelationships
            .OrderBy(r => GetDiscoveryMethodPriority(r.DiscoveryMethod))
            .ThenByDescending(r => r.ConfidenceScore)
            .FirstOrDefault();
    }
    
    /// <summary>
    /// Selects the most restrictive relationship (tightest constraints)
    /// </summary>
    private async Task<GraphEdge?> SelectMostRestrictiveRelationshipAsync(
        SqliteConnection connection,
        List<GraphEdge> relationships,
        GraphAnalysisOptions options)
    {
        var validRelationships = new List<GraphEdge>();
        
        foreach (var relationship in relationships)
        {
            var validated = await ValidateSingleRelationshipAsync(connection, relationship, options);
            if (validated != null)
            {
                validRelationships.Add(validated);
            }
        }
        
        // For MostRestrictive strategy, prioritize cardinality first, then foreign keys
        return validRelationships
            .OrderBy(r => GetCardinalityRestrictivenessScore(r.Cardinality))
            .ThenBy(r => GetDiscoveryMethodPriority(r.DiscoveryMethod))
            .ThenByDescending(r => r.ConfidenceScore)
            .FirstOrDefault();
    }
    
    /// <summary>
    /// Attempts to merge compatible relationships or selects the best one
    /// </summary>
    private async Task<GraphEdge?> MergeCompatibleRelationshipsAsync(
        SqliteConnection connection,
        List<GraphEdge> relationships,
        GraphAnalysisOptions options)
    {
        var validRelationships = new List<GraphEdge>();
        
        foreach (var relationship in relationships)
        {
            var validated = await ValidateSingleRelationshipAsync(connection, relationship, options);
            if (validated != null)
            {
                validRelationships.Add(validated);
            }
        }
        
        if (validRelationships.Count == 0)
        {
            return null;
        }
        
        if (validRelationships.Count == 1)
        {
            return validRelationships[0];
        }
        
        // Check if relationships are compatible for merging
        var foreignKeyRelationships = validRelationships.Where(r => r.DiscoveryMethod == RelationshipDiscoveryMethod.ForeignKey).ToList();
        if (foreignKeyRelationships.Count > 0)
        {
            // If we have foreign key relationships, prefer them
            return foreignKeyRelationships
                .OrderByDescending(r => r.ConfidenceScore)
                .First();
        }
        
        // For inferred relationships, return the one with highest confidence
        return validRelationships
            .OrderByDescending(r => r.ConfidenceScore)
            .First();
    }
    
    /// <summary>
    /// Creates a unique key for table endpoint pairs
    /// </summary>
    private string CreateEndpointKey(string fromTable, string toTable)
    {
        // Normalize case and create bidirectional key
        var tables = new[] { fromTable.ToLowerInvariant(), toTable.ToLowerInvariant() };
        Array.Sort(tables);
        return $"{tables[0]}|{tables[1]}";
    }
    
    /// <summary>
    /// Gets priority order for discovery methods (lower is better)
    /// </summary>
    private int GetDiscoveryMethodPriority(RelationshipDiscoveryMethod method)
    {
        return method switch
        {
            RelationshipDiscoveryMethod.ForeignKey => 0,      // Highest priority
            RelationshipDiscoveryMethod.NamingPattern => 1,
            RelationshipDiscoveryMethod.StatisticalAnalysis => 2,
            RelationshipDiscoveryMethod.Manual => 3,
            _ => 4
        };
    }
    
    /// <summary>
    /// Gets restrictiveness score for cardinality (lower is more restrictive)
    /// </summary>
    private int GetCardinalityRestrictivenessScore(RelationshipCardinality? cardinality)
    {
        return cardinality switch
        {
            RelationshipCardinality.OneToOne => 0,      // Most restrictive
            RelationshipCardinality.OneToMany => 1,
            RelationshipCardinality.ManyToOne => 2,
            RelationshipCardinality.ManyToMany => 3,   // Least restrictive
            null => 4,
            _ => 4  // Default case for any other values
        };
    }
    
    /// <summary>
    /// Checks if a table exists in the database
    /// </summary>
    private async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT COUNT(*) 
            FROM sqlite_master 
            WHERE type = 'table' AND name = @tableName";
        command.Parameters.AddWithValue("@tableName", tableName);
        
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }
    
    /// <summary>
    /// Checks if all specified columns exist in a table
    /// </summary>
    private async Task<bool> ColumnsExistAsync(SqliteConnection connection, string tableName, IReadOnlyList<string> columnNames)
    {
        if (columnNames.Count == 0)
        {
            return true;
        }
        
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")";
        
        using var reader = await command.ExecuteReaderAsync();
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        while (await reader.ReadAsync())
        {
            existingColumns.Add(reader.GetString(1)); // name column
        }
        
        return columnNames.All(col => existingColumns.Contains(col));
    }
}