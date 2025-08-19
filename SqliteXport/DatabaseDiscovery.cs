using Microsoft.Data.Sqlite;

namespace DB2XL;

internal static class DatabaseDiscovery
{
    internal static List<TableInfo> GetObjects(SqliteConnection connection, string? tableNameLikeFilter, bool includeViews)
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
        command.CommandText = $"PRAGMA table_info({SqlHelpers.Q(tableName)})";

        var columns = new List<Col>();
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

        return columns;
    }

    internal static OrderInfo DetermineOrder(SqliteConnection connection, string tableName, IReadOnlyList<Col> columns)
    {
        var primaryKeyColumns = columns
            .Where(c => c.IsPrimaryKey)
            .OrderBy(c => columns.ToList().IndexOf(c))
            .Select(c => c.Name)
            .ToList();

        if (primaryKeyColumns.Count > 0)
        {
            return new OrderInfo(OrderMode.PrimaryKey, primaryKeyColumns);
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 FROM sqlite_master WHERE name = {SqlHelpers.Q(tableName)} AND sql LIKE '%WITHOUT ROWID%'";
        
        using var reader = command.ExecuteReader();
        if (reader.Read())
        {
            return new OrderInfo(OrderMode.None, Array.Empty<string>());
        }

        return new OrderInfo(OrderMode.Rowid, new[] { "rowid" });
    }
}