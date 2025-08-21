using Microsoft.Data.Sqlite;
using DB2XL.Core.Models;
using DB2XL.Core.Enums;

namespace DB2XL.Data.Schema;

/// <summary>
/// Reads schema information from SQLite databases
/// </summary>
public class SqliteSchemaReader
{
    /// <summary>
    /// Gets all tables and views from the database
    /// </summary>
    /// <param name="connection">Open database connection</param>
    /// <param name="tableNameFilter">Optional LIKE filter for table names</param>
    /// <param name="includeViews">Whether to include views in the results</param>
    /// <returns>List of tables and views</returns>
    public static List<TableInfo> GetDatabaseObjects(SqliteConnection connection, string? tableNameFilter, bool includeViews)
    {
        using var command = connection.CreateCommand();
        
        var whereClause = "WHERE type IN ('table'" + (includeViews ? ", 'view'" : "") + ") AND name NOT LIKE 'sqlite_%'";
        
        if (!string.IsNullOrWhiteSpace(tableNameFilter))
        {
            whereClause += " AND name LIKE @filter";
            command.Parameters.AddWithValue("@filter", tableNameFilter);
        }

        command.CommandText = $@"
            SELECT name, type 
            FROM sqlite_master 
            {whereClause}
            ORDER BY name";

        var tables = new List<TableInfo>();
        using var reader = command.ExecuteReader();
        
        while (reader.Read())
        {
            var name = reader.GetString(0);
            var type = reader.GetString(1);
            tables.Add(new TableInfo(name, type));
        }

        return tables;
    }

    /// <summary>
    /// Gets column information for a table or view
    /// </summary>
    /// <param name="connection">Open database connection</param>
    /// <param name="tableName">Name of the table or view</param>
    /// <returns>List of column information</returns>
    public static List<ColumnInfo> GetTableColumns(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        
        // Check if it's a view or table
        command.CommandText = "SELECT type FROM sqlite_master WHERE name = @name";
        command.Parameters.AddWithValue("@name", tableName);
        var objectType = command.ExecuteScalar()?.ToString() ?? "table";
        
        var columns = new List<ColumnInfo>();
        
        if (objectType == "view")
        {
            // For views, get column info from a LIMIT 0 query
            command.CommandText = $"SELECT * FROM {QuoteIdentifier(tableName)} LIMIT 0";
            command.Parameters.Clear();
            using var reader = command.ExecuteReader();
            
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                var type = reader.GetDataTypeName(i);
                columns.Add(new ColumnInfo(name, type, false, null, false));
            }
        }
        else
        {
            // For tables, use PRAGMA table_info
            command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)})";
            command.Parameters.Clear();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var name = reader.GetString(1);
                var type = reader.GetString(2);
                var notNull = reader.GetInt32(3) != 0;
                var defaultValue = reader.IsDBNull(4) ? null : reader.GetValue(4);
                var isPrimaryKey = reader.GetInt32(5) > 0;

                columns.Add(new ColumnInfo(name, type, notNull, defaultValue, isPrimaryKey));
            }
        }

        return columns;
    }

    /// <summary>
    /// Determines the optimal ordering strategy for a table
    /// </summary>
    /// <param name="connection">Open database connection</param>
    /// <param name="tableName">Name of the table</param>
    /// <param name="columns">Column information for the table</param>
    /// <returns>Ordering information</returns>
    public static OrderInfo DetermineTableOrdering(SqliteConnection connection, string tableName, IReadOnlyList<ColumnInfo> columns)
    {
        // Check if it's a view first
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type FROM sqlite_master WHERE name = @name";
        command.Parameters.AddWithValue("@name", tableName);
        var objectType = command.ExecuteScalar()?.ToString() ?? "table";
        
        if (objectType == "view")
        {
            // Views don't have rowid or primary keys, can't order deterministically
            return OrderInfo.None();
        }
        
        // For tables, check for primary key columns
        var primaryKeyColumns = columns
            .Where(c => c.IsPrimaryKey)
            .OrderBy(c => columns.ToList().IndexOf(c))
            .Select(c => c.Name)
            .ToList();

        if (primaryKeyColumns.Count > 0)
        {
            return OrderInfo.ByPrimaryKey(primaryKeyColumns);
        }

        // Check if it's a WITHOUT ROWID table
        command.CommandText = $"SELECT 1 FROM sqlite_master WHERE name = @tableName AND sql LIKE '%WITHOUT ROWID%'";
        command.Parameters.Clear();
        command.Parameters.AddWithValue("@tableName", tableName);
        
        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return OrderInfo.None();
        }

        // Regular table with rowid
        return OrderInfo.ByRowId();
    }
    
    /// <summary>
    /// Quotes a SQL identifier to prevent injection
    /// </summary>
    /// <param name="identifier">The identifier to quote</param>
    /// <returns>Quoted identifier</returns>
    public static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
}