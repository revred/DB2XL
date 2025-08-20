using Microsoft.Data.Sqlite;

namespace DB2XL;

public static class DatabaseDiscovery
{
    public static List<TableInfo> GetObjects(SqliteConnection connection, string? tableNameLikeFilter, bool includeViews)
    {
        using var command = connection.CreateCommand();
        
        var whereClause = "WHERE type IN ('table'" + (includeViews ? ", 'view'" : "") + ") AND name NOT LIKE 'sqlite_%'";
        
        if (!string.IsNullOrWhiteSpace(tableNameLikeFilter))
        {
            whereClause += " AND name LIKE @filter";
            command.Parameters.AddWithValue("@filter", tableNameLikeFilter);
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
            var name = reader.GetString(0);  // name column
            var type = reader.GetString(1);  // type column
            tables.Add(new TableInfo(name, type));
        }

        return tables;
    }

    internal static List<Col> GetColumns(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        
        // Check if it's a view or table
        command.CommandText = "SELECT type FROM sqlite_master WHERE name = @name";
        command.Parameters.AddWithValue("@name", tableName);
        var objectType = command.ExecuteScalar()?.ToString() ?? "table";
        
        var columns = new List<Col>();
        
        if (objectType == "view")
        {
            // For views, get column info from a LIMIT 0 query
            command.CommandText = $"SELECT * FROM {SqlHelpers.Q(tableName)} LIMIT 0";
            command.Parameters.Clear();
            using var reader = command.ExecuteReader();
            
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var name = reader.GetName(i);
                var type = reader.GetDataTypeName(i);
                columns.Add(new Col(name, type, false, null, false));
            }
        }
        else
        {
            // For tables, use PRAGMA table_info
            command.CommandText = $"PRAGMA table_info({SqlHelpers.Q(tableName)})";
            command.Parameters.Clear();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var name = reader.GetString(1);      // name column
                var type = reader.GetString(2);      // type column
                var notNull = reader.GetInt32(3) != 0;  // notnull column
                var defaultValue = reader.IsDBNull(4) ? null : reader.GetValue(4);  // dflt_value column
                var isPrimaryKey = reader.GetInt32(5) > 0;  // pk column

                columns.Add(new Col(name, type, notNull, defaultValue, isPrimaryKey));
            }
        }

        return columns;
    }

    internal static OrderInfo DetermineOrder(SqliteConnection connection, string tableName, IReadOnlyList<Col> columns)
    {
        // Check if it's a view first
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT type FROM sqlite_master WHERE name = @name";
        command.Parameters.AddWithValue("@name", tableName);
        var objectType = command.ExecuteScalar()?.ToString() ?? "table";
        
        if (objectType == "view")
        {
            // Views don't have rowid or primary keys, can't order deterministically
            return new OrderInfo(OrderMode.None, Array.Empty<string>());
        }
        
        // For tables, check for primary key columns
        var primaryKeyColumns = columns
            .Where(c => c.IsPrimaryKey)
            .OrderBy(c => columns.ToList().IndexOf(c))
            .Select(c => c.Name)
            .ToList();

        if (primaryKeyColumns.Count > 0)
        {
            return new OrderInfo(OrderMode.PrimaryKey, primaryKeyColumns);
        }

        // Check if it's a WITHOUT ROWID table
        command.CommandText = $"SELECT 1 FROM sqlite_master WHERE name = @tableName AND sql LIKE '%WITHOUT ROWID%'";
        command.Parameters.Clear();
        command.Parameters.AddWithValue("@tableName", tableName);
        
        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return new OrderInfo(OrderMode.None, Array.Empty<string>());
        }

        // Regular table with rowid
        return new OrderInfo(OrderMode.Rowid, new[] { "rowid" });
    }
}