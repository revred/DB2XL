using Microsoft.Data.Sqlite;
using DB2XL.Core.Models;
using DB2XL.Core.Enums;
using System.Text.RegularExpressions;

namespace DB2XL.Data.Analysis;

/// <summary>
/// Service for discovering foreign key relationships in SQLite databases
/// </summary>
public sealed class ForeignKeyDiscoveryService
{
    /// <summary>
    /// Discovers all foreign key relationships using PRAGMA foreign_key_list
    /// </summary>
    public async Task<IReadOnlyList<GraphEdge>> DiscoverForeignKeysAsync(
        SqliteConnection connection, 
        IEnumerable<string> tableNames)
    {
        var relationships = new List<GraphEdge>();
        
        foreach (var tableName in tableNames)
        {
            try
            {
                var tableRelationships = await AnalyzeForeignKeysForTableAsync(connection, tableName);
                relationships.AddRange(tableRelationships);
            }
            catch (Exception ex)
            {
                // Log warning but continue processing other tables
                // TODO: Add proper logging
                Console.WriteLine($"Warning: Failed to analyze foreign keys for table {tableName}: {ex.Message}");
            }
        }
        
        return relationships;
    }
    
    /// <summary>
    /// Analyzes foreign keys for a specific table
    /// </summary>
    private async Task<IReadOnlyList<GraphEdge>> AnalyzeForeignKeysForTableAsync(
        SqliteConnection connection, 
        string tableName)
    {
        var relationships = new List<GraphEdge>();
        
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list(\"{tableName.Replace("\"", "\"\"")}\")";
        
        using var reader = await command.ExecuteReaderAsync();
        var foreignKeyGroups = new Dictionary<int, List<ForeignKeyInfo>>();
        
        // Group foreign key columns by constraint ID
        while (await reader.ReadAsync())
        {
            var fkInfo = new ForeignKeyInfo
            {
                Id = reader.GetInt32(0),        // id
                Sequence = reader.GetInt32(1),  // seq
                ToTable = reader.GetString(2),  // table
                FromColumn = reader.GetString(3), // from
                ToColumn = reader.GetString(4), // to
                OnUpdate = reader.GetString(5), // on_update
                OnDelete = reader.GetString(6), // on_delete  
                Match = reader.GetString(7)     // match
            };
            
            if (!foreignKeyGroups.ContainsKey(fkInfo.Id))
            {
                foreignKeyGroups[fkInfo.Id] = new List<ForeignKeyInfo>();
            }
            
            foreignKeyGroups[fkInfo.Id].Add(fkInfo);
        }
        
        // Create graph edges from foreign key groups
        foreach (var (constraintId, fkColumns) in foreignKeyGroups)
        {
            // Sort by sequence to maintain column order
            var orderedColumns = fkColumns.OrderBy(fk => fk.Sequence).ToList();
            
            var edge = new GraphEdge(
                tableName,
                orderedColumns.First().ToTable,
                RelationshipType.ForeignKey)
            {
                FromColumns = orderedColumns.Select(fk => fk.FromColumn).ToList(),
                ToColumns = orderedColumns.Select(fk => fk.ToColumn).ToList(),
                ConfidenceScore = 1.0, // Foreign keys have maximum confidence
                DiscoveryMethod = RelationshipDiscoveryMethod.ForeignKey,
                Cardinality = RelationshipCardinality.ManyToOne, // FK typically many-to-one
                Metadata = new Dictionary<string, object?>
                {
                    ["constraint_id"] = constraintId,
                    ["on_update"] = orderedColumns.First().OnUpdate,
                    ["on_delete"] = orderedColumns.First().OnDelete,
                    ["match"] = orderedColumns.First().Match
                }
            };
            
            relationships.Add(edge);
        }
        
        return relationships;
    }
    
    /// <summary>
    /// Discovers relationships based on naming patterns
    /// </summary>
    public async Task<IReadOnlyList<GraphEdge>> DiscoverByNamingPatternsAsync(
        SqliteConnection connection,
        IEnumerable<string> tableNames,
        IReadOnlyList<string> patterns)
    {
        var relationships = new List<GraphEdge>();
        var allColumns = await GetAllTableColumnsAsync(connection, tableNames);
        var tableSet = new HashSet<string>(tableNames, StringComparer.OrdinalIgnoreCase);
        
        foreach (var (tableName, columns) in allColumns)
        {
            foreach (var column in columns)
            {
                var potentialTargets = FindPotentialTargetTables(column.Name, tableSet, patterns);
                
                foreach (var targetTable in potentialTargets)
                {
                    // Check if target table has a column that could be the referenced column
                    var targetColumns = allColumns.GetValueOrDefault(targetTable) ?? Array.Empty<ColumnInfo>();
                    var matchingTargetColumn = FindMatchingTargetColumn(column.Name, targetColumns);
                    
                    if (matchingTargetColumn != null)
                    {
                        var confidence = CalculateNamingPatternConfidence(column.Name, targetTable, matchingTargetColumn.Name);
                        
                        if (confidence >= 0.3) // Minimum threshold for naming pattern matches
                        {
                            var edge = new GraphEdge(
                                tableName,
                                targetTable,
                                RelationshipType.Inferred)
                            {
                                FromColumns = new[] { column.Name },
                                ToColumns = new[] { matchingTargetColumn.Name },
                                ConfidenceScore = confidence,
                                DiscoveryMethod = RelationshipDiscoveryMethod.NamingPattern,
                                Cardinality = RelationshipCardinality.ManyToOne,
                                Metadata = new Dictionary<string, object?>
                                {
                                    ["pattern_match"] = true,
                                    ["source_column_type"] = column.Type,
                                    ["target_column_type"] = matchingTargetColumn.Type
                                }
                            };
                            
                            relationships.Add(edge);
                        }
                    }
                }
            }
        }
        
        return relationships;
    }
    
    private async Task<Dictionary<string, IReadOnlyList<ColumnInfo>>> GetAllTableColumnsAsync(
        SqliteConnection connection, 
        IEnumerable<string> tableNames)
    {
        var result = new Dictionary<string, IReadOnlyList<ColumnInfo>>();
        
        foreach (var tableName in tableNames)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")";
            
            var columns = new List<ColumnInfo>();
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
            
            result[tableName] = columns;
        }
        
        return result;
    }
    
    private IEnumerable<string> FindPotentialTargetTables(
        string columnName, 
        HashSet<string> availableTables, 
        IReadOnlyList<string> patterns)
    {
        var targets = new List<string>();
        
        foreach (var pattern in patterns)
        {
            var regex = CreateRegexFromPattern(pattern);
            var match = regex.Match(columnName);
            
            if (match.Success)
            {
                var tableName = ExtractTableNameFromMatch(match, pattern);
                
                // Try exact match first
                if (availableTables.Contains(tableName))
                {
                    targets.Add(tableName);
                }
                // Try plural/singular variations
                else
                {
                    var variations = GenerateTableNameVariations(tableName);
                    targets.AddRange(variations.Where(availableTables.Contains));
                }
            }
        }
        
        return targets.Distinct();
    }
    
    private ColumnInfo? FindMatchingTargetColumn(string sourceColumnName, IReadOnlyList<ColumnInfo> targetColumns)
    {
        // Primary key columns are most likely targets
        var primaryKeys = targetColumns.Where(c => c.IsPrimaryKey).ToList();
        if (primaryKeys.Count == 1)
        {
            return primaryKeys[0];
        }
        
        // Look for exact name matches (without suffix)
        var baseName = RemoveForeignKeySuffixes(sourceColumnName);
        return targetColumns.FirstOrDefault(c => 
            c.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase) ||
            c.Name.Equals("id", StringComparison.OrdinalIgnoreCase));
    }
    
    private double CalculateNamingPatternConfidence(string sourceColumn, string targetTable, string targetColumn)
    {
        double confidence = 0.5; // Base confidence for naming pattern match
        
        // Higher confidence if target column is primary key
        if (targetColumn.Equals("id", StringComparison.OrdinalIgnoreCase))
        {
            confidence += 0.3;
        }
        
        // Higher confidence for standard naming patterns
        if (sourceColumn.EndsWith("_id", StringComparison.OrdinalIgnoreCase) ||
            sourceColumn.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
        {
            confidence += 0.2;
        }
        
        return Math.Min(confidence, 0.95); // Cap at 95% for inferred relationships
    }
    
    private Regex CreateRegexFromPattern(string pattern)
    {
        var regexPattern = pattern
            .Replace("*", "([a-zA-Z_][a-zA-Z0-9_]*)")
            .Replace("?", "([a-zA-Z0-9])");
        
        return new Regex($"^{regexPattern}$", RegexOptions.IgnoreCase);
    }
    
    private string ExtractTableNameFromMatch(Match match, string originalPattern)
    {
        if (match.Groups.Count > 1)
        {
            return match.Groups[1].Value;
        }
        
        return match.Value;
    }
    
    private IEnumerable<string> GenerateTableNameVariations(string tableName)
    {
        yield return tableName;
        yield return tableName + "s"; // plural
        yield return tableName.TrimEnd('s'); // singular
        yield return char.ToUpper(tableName[0]) + tableName[1..]; // capitalize
        yield return tableName.ToLower(); // lowercase
    }
    
    private string RemoveForeignKeySuffixes(string columnName)
    {
        var suffixes = new[] { "_id", "Id", "_key", "Key", "FK", "fk" };
        
        foreach (var suffix in suffixes)
        {
            if (columnName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return columnName[..^suffix.Length];
            }
        }
        
        return columnName;
    }
}

/// <summary>
/// Information about a foreign key constraint from PRAGMA foreign_key_list
/// </summary>
internal record ForeignKeyInfo
{
    public required int Id { get; init; }
    public required int Sequence { get; init; }
    public required string FromColumn { get; init; }
    public required string ToTable { get; init; }
    public required string ToColumn { get; init; }
    public required string OnUpdate { get; init; }
    public required string OnDelete { get; init; }
    public required string Match { get; init; }
}