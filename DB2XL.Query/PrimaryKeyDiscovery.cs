using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;

namespace DB2XL.Query;

/// <summary>
/// Strategy for primary key identification and ordering
/// </summary>
public enum PrimaryKeyStrategy
{
    /// <summary>
    /// Explicit primary key columns defined on the table
    /// </summary>
    ExplicitPrimaryKey,
    
    /// <summary>
    /// Unique index that serves as a primary key substitute
    /// </summary>
    UniqueIndex,
    
    /// <summary>
    /// SQLite implicit rowid column
    /// </summary>
    ImplicitRowId,
    
    /// <summary>
    /// Synthesized primary key from hash of all columns
    /// </summary>
    SyntheticHash,
    
    /// <summary>
    /// No deterministic ordering available
    /// </summary>
    None
}

/// <summary>
/// Information about discovered primary key
/// </summary>
public sealed record PrimaryKeyInfo
{
    /// <summary>
    /// Strategy used to identify the primary key
    /// </summary>
    public PrimaryKeyStrategy Strategy { get; init; }
    
    /// <summary>
    /// Column names that form the primary key (in order)
    /// </summary>
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Human-readable description of the strategy
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// Whether the ordering is deterministic
    /// </summary>
    public bool IsDeterministic { get; init; }
    
    /// <summary>
    /// Additional metadata about the primary key
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Column information from PRAGMA table_info
/// </summary>
public sealed record ColumnInfo
{
    public int ColumnId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool NotNull { get; init; }
    public string? DefaultValue { get; init; }
    public int PrimaryKey { get; init; }
}

/// <summary>
/// Index information from sqlite_master
/// </summary>
public sealed record IndexInfo
{
    public string Name { get; init; } = string.Empty;
    public string TableName { get; init; } = string.Empty;
    public bool IsUnique { get; init; }
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
    public string? WhereClause { get; init; }
}

/// <summary>
/// Service for discovering primary keys in SQLite tables for deterministic ordering
/// </summary>
public interface IPrimaryKeyDiscoveryService
{
    /// <summary>
    /// Discovers the primary key strategy for a table
    /// </summary>
    /// <param name="connection">SQLite connection</param>
    /// <param name="tableName">Table name to analyze</param>
    /// <returns>Primary key information</returns>
    PrimaryKeyInfo DiscoverPrimaryKey(SqliteConnection connection, string tableName);
    
    /// <summary>
    /// Gets column information for a table
    /// </summary>
    /// <param name="connection">SQLite connection</param>
    /// <param name="tableName">Table name</param>
    /// <returns>Column information list</returns>
    IReadOnlyList<ColumnInfo> GetColumns(SqliteConnection connection, string tableName);
    
    /// <summary>
    /// Gets index information for a table
    /// </summary>
    /// <param name="connection">SQLite connection</param>
    /// <param name="tableName">Table name</param>
    /// <returns>Index information list</returns>
    IReadOnlyList<IndexInfo> GetIndexes(SqliteConnection connection, string tableName);
    
    /// <summary>
    /// Checks if table is WITHOUT ROWID
    /// </summary>
    /// <param name="connection">SQLite connection</param>
    /// <param name="tableName">Table name</param>
    /// <returns>True if table is WITHOUT ROWID</returns>
    bool IsWithoutRowId(SqliteConnection connection, string tableName);
    
    /// <summary>
    /// Generates deterministic ORDER BY clause from primary key info
    /// </summary>
    /// <param name="primaryKey">Primary key information</param>
    /// <returns>ORDER BY clause (without "ORDER BY" prefix)</returns>
    string GenerateOrderByClause(PrimaryKeyInfo primaryKey);
}

/// <summary>
/// Default implementation of primary key discovery service
/// </summary>
public sealed class PrimaryKeyDiscoveryService : IPrimaryKeyDiscoveryService
{
    /// <summary>
    /// Discovers the primary key strategy for a table following the Filters.md approach
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
    /// Gets column information using PRAGMA table_info
    /// </summary>
    public IReadOnlyList<ColumnInfo> GetColumns(SqliteConnection connection, string tableName)
    {
        var columns = new List<ColumnInfo>();
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")";
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(new ColumnInfo
            {
                ColumnId = reader.GetInt32(0), // cid
                Name = reader.GetString(1),    // name
                Type = reader.GetString(2),    // type
                NotNull = reader.GetBoolean(3), // notnull
                DefaultValue = reader.IsDBNull(4) ? null : reader.GetString(4), // dflt_value
                PrimaryKey = reader.GetInt32(5) // pk
            });
        }
        
        return columns;
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
            var name = reader.GetString(0);  // name
            var sql = reader.IsDBNull(1) ? null : reader.GetString(1); // sql
            
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
    
    /// <summary>
    /// Discovers explicit primary key from PRAGMA table_info
    /// </summary>
    private PrimaryKeyInfo? DiscoverExplicitPrimaryKey(SqliteConnection connection, string tableName)
    {
        var columns = GetColumns(connection, tableName);
        var pkColumns = columns
            .Where(c => c.PrimaryKey > 0)
            .OrderBy(c => c.PrimaryKey)
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
    
    /// <summary>
    /// Discovers unique index that can serve as primary key
    /// </summary>
    private PrimaryKeyInfo? DiscoverUniqueIndexPrimaryKey(SqliteConnection connection, string tableName)
    {
        var indexes = GetIndexes(connection, tableName);
        var uniqueIndexes = indexes.Where(i => i.IsUnique && i.WhereClause == null).ToList();
        
        foreach (var index in uniqueIndexes)
        {
            // Check that all columns in the index are NOT NULL
            var columns = GetColumns(connection, tableName);
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
    
    /// <summary>
    /// Creates synthetic primary key using hash of all columns
    /// </summary>
    private PrimaryKeyInfo DiscoverSyntheticPrimaryKey(SqliteConnection connection, string tableName)
    {
        var columns = GetColumns(connection, tableName);
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
    
    /// <summary>
    /// Parses CREATE INDEX SQL to extract index information
    /// </summary>
    private static IndexInfo? ParseIndexSql(string indexName, string tableName, string sql)
    {
        try
        {
            var isUnique = sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
            
            // Extract column names between parentheses
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
            
            // Check for WHERE clause (partial index)
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
            // If parsing fails, return null
            return null;
        }
    }
}

/// <summary>
/// Utilities for generating synthetic primary keys
/// </summary>
public static class SyntheticPrimaryKeyGenerator
{
    /// <summary>
    /// Generates a deterministic hash from row values for synthetic primary key
    /// </summary>
    /// <param name="columnValues">Column values in order</param>
    /// <returns>SHA256 hash as hexadecimal string</returns>
    public static string GenerateRowHash(IReadOnlyList<object?> columnValues)
    {
        using var sha256 = SHA256.Create();
        var combined = new StringBuilder();
        
        for (int i = 0; i < columnValues.Count; i++)
        {
            if (i > 0)
            {
                combined.Append('\x1F'); // Unit separator
            }
            
            var value = columnValues[i];
            if (value == null)
            {
                combined.Append('\x00'); // Null marker
            }
            else
            {
                combined.Append(value.ToString());
            }
        }
        
        var bytes = Encoding.UTF8.GetBytes(combined.ToString());
        var hash = sha256.ComputeHash(bytes);
        
        return Convert.ToHexString(hash);
    }
}