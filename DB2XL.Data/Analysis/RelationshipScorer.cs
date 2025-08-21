using Microsoft.Data.Sqlite;
using DB2XL.Core.Models;
using DB2XL.Core.Enums;

namespace DB2XL.Data.Analysis;

/// <summary>
/// Service for scoring and validating database relationships
/// </summary>
public sealed class RelationshipScorer
{
    /// <summary>
    /// Calculates confidence scores for relationships based on multiple factors
    /// </summary>
    public async Task<IReadOnlyList<GraphEdge>> ScoreRelationshipsAsync(
        SqliteConnection connection,
        IReadOnlyList<GraphEdge> relationships,
        GraphAnalysisOptions options)
    {
        var scoredRelationships = new List<GraphEdge>();
        
        foreach (var relationship in relationships)
        {
            var score = await CalculateConfidenceScoreAsync(connection, relationship, options);
            
            if (score >= options.MinimumConfidenceScore)
            {
                var updatedRelationship = relationship with { ConfidenceScore = score };
                scoredRelationships.Add(updatedRelationship);
            }
        }
        
        return scoredRelationships;
    }
    
    /// <summary>
    /// Calculates confidence score for a single relationship
    /// </summary>
    private async Task<double> CalculateConfidenceScoreAsync(
        SqliteConnection connection,
        GraphEdge relationship,
        GraphAnalysisOptions options)
    {
        double baseScore = relationship.ConfidenceScore;
        double multiplier = 1.0;
        
        // Foreign key relationships get highest base score
        if (relationship.DiscoveryMethod == RelationshipDiscoveryMethod.ForeignKey)
        {
            return Math.Min(baseScore * 1.0, 1.0); // FK relationships maintain their score
        }
        
        // Validate data type compatibility
        var typeCompatibility = await CheckDataTypeCompatibilityAsync(connection, relationship);
        multiplier *= typeCompatibility;
        
        // Check statistical correlation if enabled
        if (options.PerformStatisticalAnalysis)
        {
            var statisticalScore = await CalculateStatisticalCorrelationAsync(connection, relationship);
            multiplier *= statisticalScore;
        }
        
        // Naming pattern quality
        var namingScore = CalculateNamingPatternQuality(relationship);
        multiplier *= namingScore;
        
        // Cardinality validation
        var cardinalityScore = await ValidateCardinalityAsync(connection, relationship);
        multiplier *= cardinalityScore;
        
        return Math.Min(baseScore * multiplier, 0.95); // Cap inferred relationships at 95%
    }
    
    /// <summary>
    /// Checks if column data types are compatible for a relationship
    /// </summary>
    private async Task<double> CheckDataTypeCompatibilityAsync(
        SqliteConnection connection,
        GraphEdge relationship)
    {
        try
        {
            var fromTypes = await GetColumnTypesAsync(connection, relationship.FromTable, relationship.FromColumns);
            var toTypes = await GetColumnTypesAsync(connection, relationship.ToTable, relationship.ToColumns);
            
            if (fromTypes.Count != toTypes.Count)
            {
                return 0.1; // Column count mismatch is a strong negative signal
            }
            
            double compatibility = 0.0;
            for (int i = 0; i < fromTypes.Count; i++)
            {
                compatibility += CalculateTypeCompatibility(fromTypes[i], toTypes[i]);
            }
            
            return compatibility / fromTypes.Count;
        }
        catch
        {
            return 0.5; // Default if we can't determine compatibility
        }
    }
    
    /// <summary>
    /// Gets the data types for specified columns
    /// </summary>
    private async Task<IReadOnlyList<string>> GetColumnTypesAsync(
        SqliteConnection connection,
        string tableName,
        IReadOnlyList<string> columnNames)
    {
        var types = new List<string>();
        
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")";
        
        using var reader = await command.ExecuteReaderAsync();
        var tableColumns = new Dictionary<string, string>();
        
        while (await reader.ReadAsync())
        {
            tableColumns[reader.GetString(1)] = reader.GetString(2); // name, type
        }
        
        foreach (var columnName in columnNames)
        {
            if (tableColumns.TryGetValue(columnName, out var type))
            {
                types.Add(type);
            }
        }
        
        return types;
    }
    
    /// <summary>
    /// Calculates compatibility score between two SQLite data types
    /// </summary>
    private double CalculateTypeCompatibility(string fromType, string toType)
    {
        // Normalize types
        fromType = NormalizeSqliteType(fromType);
        toType = NormalizeSqliteType(toType);
        
        // Exact match
        if (fromType.Equals(toType, StringComparison.OrdinalIgnoreCase))
        {
            return 1.0;
        }
        
        // Integer compatibility
        if (IsIntegerType(fromType) && IsIntegerType(toType))
        {
            return 0.9;
        }
        
        // Numeric compatibility
        if (IsNumericType(fromType) && IsNumericType(toType))
        {
            return 0.8;
        }
        
        // Text compatibility
        if (IsTextType(fromType) && IsTextType(toType))
        {
            return 0.7;
        }
        
        // Mixed numeric/text (possible with SQLite's dynamic typing)
        if ((IsNumericType(fromType) && IsTextType(toType)) ||
            (IsTextType(fromType) && IsNumericType(toType)))
        {
            return 0.3;
        }
        
        return 0.1; // Incompatible types
    }
    
    private string NormalizeSqliteType(string type)
    {
        return type.ToUpperInvariant().Split('(')[0].Trim();
    }
    
    private bool IsIntegerType(string type) =>
        type.Contains("INT") || type == "INTEGER";
    
    private bool IsNumericType(string type) =>
        IsIntegerType(type) || type.Contains("REAL") || type.Contains("FLOAT") || 
        type.Contains("DOUBLE") || type.Contains("NUMERIC") || type.Contains("DECIMAL");
    
    private bool IsTextType(string type) =>
        type.Contains("TEXT") || type.Contains("CHAR") || type.Contains("VARCHAR") ||
        type.Contains("CLOB") || string.IsNullOrEmpty(type);
    
    /// <summary>
    /// Calculates statistical correlation between columns (if analysis is enabled)
    /// </summary>
    private async Task<double> CalculateStatisticalCorrelationAsync(
        SqliteConnection connection,
        GraphEdge relationship)
    {
        try
        {
            // Sample a subset of data to check for referential integrity
            using var command = connection.CreateCommand();
            var fromColumn = relationship.FromColumns[0];
            var toColumn = relationship.ToColumns[0];
            
            command.CommandText = $@"
                SELECT COUNT(*) as total,
                       COUNT(t2.{QuoteIdentifier(toColumn)}) as matching
                FROM (SELECT {QuoteIdentifier(fromColumn)} 
                      FROM {QuoteIdentifier(relationship.FromTable)} 
                      WHERE {QuoteIdentifier(fromColumn)} IS NOT NULL 
                      LIMIT 1000) t1
                LEFT JOIN {QuoteIdentifier(relationship.ToTable)} t2 
                ON t1.{QuoteIdentifier(fromColumn)} = t2.{QuoteIdentifier(toColumn)}";
            
            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var total = reader.GetInt64(0);    // total
                var matching = reader.GetInt64(1); // matching
                
                if (total == 0) return 0.5;
                
                var ratio = (double)matching / total;
                
                // High referential integrity suggests strong relationship
                return ratio > 0.8 ? 1.2 : ratio > 0.5 ? 1.0 : 0.7;
            }
        }
        catch
        {
            // If statistical analysis fails, don't penalize
        }
        
        return 1.0;
    }
    
    /// <summary>
    /// Evaluates the quality of naming pattern matches
    /// </summary>
    private double CalculateNamingPatternQuality(GraphEdge relationship)
    {
        if (relationship.DiscoveryMethod != RelationshipDiscoveryMethod.NamingPattern)
        {
            return 1.0;
        }
        
        var fromColumn = relationship.FromColumns.FirstOrDefault() ?? "";
        var toColumn = relationship.ToColumns.FirstOrDefault() ?? "";
        var toTable = relationship.ToTable;
        
        double score = 1.0;
        
        // Standard foreign key naming patterns
        if (fromColumn.EndsWith("_id", StringComparison.OrdinalIgnoreCase) ||
            fromColumn.EndsWith("Id", StringComparison.OrdinalIgnoreCase))
        {
            score *= 1.2;
        }
        
        // Table name appears in column name
        if (fromColumn.StartsWith(toTable, StringComparison.OrdinalIgnoreCase) ||
            fromColumn.StartsWith(toTable.TrimEnd('s'), StringComparison.OrdinalIgnoreCase))
        {
            score *= 1.3;
        }
        
        // Target column is 'id' (very common primary key)
        if (toColumn.Equals("id", StringComparison.OrdinalIgnoreCase))
        {
            score *= 1.1;
        }
        
        return Math.Min(score, 1.5);
    }
    
    /// <summary>
    /// Validates the assumed cardinality of a relationship
    /// </summary>
    private async Task<double> ValidateCardinalityAsync(
        SqliteConnection connection,
        GraphEdge relationship)
    {
        try
        {
            // Quick cardinality check on a sample
            using var command = connection.CreateCommand();
            var fromColumn = relationship.FromColumns[0];
            var toColumn = relationship.ToColumns[0];
            
            command.CommandText = $@"
                SELECT 
                    COUNT(DISTINCT {QuoteIdentifier(fromColumn)}) as distinct_from,
                    COUNT({QuoteIdentifier(fromColumn)}) as total_from
                FROM {QuoteIdentifier(relationship.FromTable)}
                WHERE {QuoteIdentifier(fromColumn)} IS NOT NULL
                LIMIT 1000";
            
            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var distinctFrom = reader.GetInt64(0); // distinct_from
                var totalFrom = reader.GetInt64(1);    // total_from
                
                if (totalFrom == 0) return 1.0;
                
                var ratio = (double)distinctFrom / totalFrom;
                
                // If most values are unique, it's likely many-to-one (good for FK)
                // If most values are duplicated, it might be one-to-many or many-to-many
                return relationship.Cardinality == RelationshipCardinality.ManyToOne && ratio < 0.8 ? 1.1 : 1.0;
            }
        }
        catch
        {
            // If validation fails, don't penalize
        }
        
        return 1.0;
    }
    
    private string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }
}