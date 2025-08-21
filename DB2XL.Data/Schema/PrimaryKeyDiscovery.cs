using Microsoft.Data.Sqlite;
using DB2XL.Core.Models;
using DB2XL.Core.Utilities;

namespace DB2XL.Data.Schema;

/// <summary>
/// Service for discovering primary keys in SQLite tables for deterministic ordering
/// </summary>
public interface IPrimaryKeyDiscoveryService
{
    /// <summary>
    /// Discovers the primary key strategy for a table
    /// </summary>
    PrimaryKeyInfo DiscoverPrimaryKey(SqliteConnection connection, string tableName);
    
    /// <summary>
    /// Gets index information for a table
    /// </summary>
    IReadOnlyList<IndexInfo> GetIndexes(SqliteConnection connection, string tableName);
    
    /// <summary>
    /// Checks if table is WITHOUT ROWID
    /// </summary>
    bool IsWithoutRowId(SqliteConnection connection, string tableName);
    
    /// <summary>
    /// Generates deterministic ORDER BY clause from primary key info
    /// </summary>
    string GenerateOrderByClause(PrimaryKeyInfo primaryKey);
}

/// <summary>
/// Default implementation of primary key discovery service
/// </summary>
public sealed class PrimaryKeyDiscoveryService : IPrimaryKeyDiscoveryService
{
    /// <summary>
    /// Discovers the primary key strategy for a table
    /// </summary>
    public PrimaryKeyInfo DiscoverPrimaryKey(SqliteConnection connection, string tableName)
    {
        // Strategy 1: Explicit primary key from PRAGMA table_info
        var explicitPk = DiscoverExplicitPrimaryKey(connection, tableName);
        if (explicitPk != null)
        {
            return explicitPk;
        }
        
        // Strategy 2: Unique index as primary key substitute
        var uniqueIndexPk = DiscoverUniqueIndexPrimaryKey(connection, tableName);
        if (uniqueIndexPk != null)
        {
            return uniqueIndexPk;
        }
        
        // Strategy 3: Check if table is WITHOUT ROWID
        if (IsWithoutRowId(connection, tableName))
        {
            // WITHOUT ROWID with no explicit PK or unique index - use synthetic hash
            return DiscoverSyntheticPrimaryKey(connection, tableName);
        }
        
        // Strategy 4: Use implicit rowid (default for most SQLite tables)
        return new PrimaryKeyInfo
        {
            Strategy = PrimaryKeyStrategy.ImplicitRowId,
            Columns = new[] { "rowid" },
            Description = "SQLite implicit rowid column",
            IsDeterministic = true,
            Metadata = new Dictionary<string, object>
            {
                ["implicit"] = true,
                ["stable"] = true
            }
        };
    }
    
    /// <summary>
    /// Gets index information from sqlite_master
    /// </summary>
    public IReadOnlyList<IndexInfo> GetIndexes(SqliteConnection connection, string tableName)
    {
        var indexes = new List<IndexInfo>();
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT name, sql, tbl_name 
            FROM sqlite_master 
            WHERE type = 'index' 
              AND tbl_name = @tableName 
              AND name NOT LIKE 'sqlite_autoindex_%'
            ORDER BY name";
        cmd.Parameters.AddWithValue("@tableName", tableName);
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            var sql = reader.IsDBNull(1) ? null : reader.GetString(1);
            
            if (sql != null)
            {
                var indexInfo = ParseIndexSql(name, tableName, sql);
                if (indexInfo != null)
                {
                    indexes.Add(indexInfo);
                }
            }
        }
        
        return indexes;
    }
    
    /// <summary>
    /// Checks if table is defined WITHOUT ROWID
    /// </summary>
    public bool IsWithoutRowId(SqliteConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT sql 
            FROM sqlite_master 
            WHERE type = 'table' 
              AND name = @tableName";
        cmd.Parameters.AddWithValue("@tableName", tableName);
        
        var sql = cmd.ExecuteScalar()?.ToString();
        return sql?.Contains("WITHOUT ROWID", StringComparison.OrdinalIgnoreCase) == true;
    }
    
    /// <summary>
    /// Generates ORDER BY clause from primary key information
    /// </summary>
    public string GenerateOrderByClause(PrimaryKeyInfo primaryKey)
    {
        if (!primaryKey.IsDeterministic || primaryKey.Columns.Count == 0)
        {
            return string.Empty;
        }
        
        var quotedColumns = primaryKey.Columns.Select(col => $"\"{col.Replace("\"", "\"\"")}\" ASC");
        return string.Join(", ", quotedColumns);
    }
    
    private PrimaryKeyInfo? DiscoverExplicitPrimaryKey(SqliteConnection connection, string tableName)
    {
        var columns = SqliteSchemaReader.GetTableColumns(connection, tableName);
        var pkColumns = columns
            .Where(c => c.IsPrimaryKey)
            .ToList();
        
        if (pkColumns.Count == 0)
        {
            return null;
        }
        
        return new PrimaryKeyInfo
        {
            Strategy = PrimaryKeyStrategy.ExplicitPrimaryKey,
            Columns = pkColumns.Select(c => c.Name).ToArray(),
            Description = pkColumns.Count == 1 
                ? $"Single column primary key: {pkColumns[0].Name}"
                : $"Composite primary key: [{string.Join(", ", pkColumns.Select(c => c.Name))}]",
            IsDeterministic = true,
            Metadata = new Dictionary<string, object>
            {
                ["columnCount"] = pkColumns.Count,
                ["composite"] = pkColumns.Count > 1
            }
        };
    }
    
    private PrimaryKeyInfo? DiscoverUniqueIndexPrimaryKey(SqliteConnection connection, string tableName)
    {
        var indexes = GetIndexes(connection, tableName);
        var uniqueIndexes = indexes.Where(i => i.IsUnique && i.WhereClause == null).ToList();
        
        foreach (var index in uniqueIndexes)
        {
            var columns = SqliteSchemaReader.GetTableColumns(connection, tableName);
            var indexColumns = index.Columns.Select(colName => columns.FirstOrDefault(c => c.Name == colName)).ToList();
            
            if (indexColumns.All(c => c?.NotNull == true))
            {
                return new PrimaryKeyInfo
                {
                    Strategy = PrimaryKeyStrategy.UniqueIndex,
                    Columns = index.Columns,
                    Description = $"Unique index as PK: {index.Name} on [{string.Join(", ", index.Columns)}]",
                    IsDeterministic = true,
                    Metadata = new Dictionary<string, object>
                    {
                        ["indexName"] = index.Name,
                        ["allNotNull"] = true
                    }
                };
            }
        }
        
        return null;
    }
    
    private PrimaryKeyInfo DiscoverSyntheticPrimaryKey(SqliteConnection connection, string tableName)
    {
        var columns = SqliteSchemaReader.GetTableColumns(connection, tableName);
        var allColumnNames = columns.Select(c => c.Name).ToArray();
        
        return new PrimaryKeyInfo
        {
            Strategy = PrimaryKeyStrategy.SyntheticHash,
            Columns = new[] { "_synthetic_pk" },
            Description = $"Synthetic hash PK from {allColumnNames.Length} columns",
            IsDeterministic = true,
            Metadata = new Dictionary<string, object>
            {
                ["sourceColumns"] = allColumnNames,
                ["algorithm"] = "SHA256",
                ["deterministic"] = true
            }
        };
    }
    
    private static IndexInfo? ParseIndexSql(string indexName, string tableName, string sql)
    {
        try
        {
            var isUnique = sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
            
            var openParen = sql.IndexOf('(');
            var closeParen = sql.LastIndexOf(')');
            
            if (openParen == -1 || closeParen == -1 || closeParen <= openParen)
            {
                return null;
            }
            
            var columnsPart = sql.Substring(openParen + 1, closeParen - openParen - 1);
            var columns = columnsPart
                .Split(',')
                .Select(col => col.Trim().Trim('"'))
                .Where(col => !string.IsNullOrEmpty(col))
                .ToArray();
            
            string? whereClause = null;
            var whereIndex = sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
            if (whereIndex > closeParen)
            {
                whereClause = sql.Substring(whereIndex + 5).Trim();
            }
            
            return new IndexInfo
            {
                Name = indexName,
                TableName = tableName,
                IsUnique = isUnique,
                Columns = columns,
                WhereClause = whereClause
            };
        }
        catch
        {
            return null;
        }
    }
}

