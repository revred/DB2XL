using System.Text;
using DB2XL.Core.Models;
using DB2XL.Core.Enums;

namespace DB2XL.Data.Query;

/// <summary>
/// Builds SQL queries for data extraction
/// </summary>
public class SqlQueryBuilder
{
    /// <summary>
    /// Quotes a SQL identifier to prevent injection
    /// </summary>
    /// <param name="identifier">The identifier to quote</param>
    /// <returns>Quoted identifier</returns>
    public static string QuoteIdentifier(string identifier) 
        => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// Builds a SELECT query for extracting table data
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <param name="columns">Columns to select</param>
    /// <param name="orderInfo">Ordering information</param>
    /// <param name="useDeterministicOrder">Whether to apply deterministic ordering</param>
    /// <returns>SQL SELECT statement</returns>
    public static string BuildSelectQuery(
        string tableName, 
        IReadOnlyList<ColumnInfo> columns, 
        OrderInfo orderInfo, 
        bool useDeterministicOrder)
    {
        var sb = new StringBuilder("SELECT ");
        
        for (int i = 0; i < columns.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(QuoteIdentifier(columns[i].Name));
        }
        
        sb.Append(" FROM ").Append(QuoteIdentifier(tableName));

        if (useDeterministicOrder && orderInfo.IsDeterministic)
        {
            sb.Append(" ORDER BY ");
            for (int i = 0; i < orderInfo.Columns.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(QuoteIdentifier(orderInfo.Columns[i])).Append(" ASC");
            }
        }

        return sb.ToString();
    }
    
    /// <summary>
    /// Builds a SELECT query with pagination support
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <param name="columns">Columns to select</param>
    /// <param name="orderInfo">Ordering information</param>
    /// <param name="offset">Number of rows to skip</param>
    /// <param name="limit">Maximum number of rows to return</param>
    /// <returns>SQL SELECT statement with LIMIT and OFFSET</returns>
    public static string BuildPaginatedSelectQuery(
        string tableName,
        IReadOnlyList<ColumnInfo> columns,
        OrderInfo orderInfo,
        int offset,
        int limit)
    {
        var baseQuery = BuildSelectQuery(tableName, columns, orderInfo, true);
        return $"{baseQuery} LIMIT {limit} OFFSET {offset}";
    }
    
    /// <summary>
    /// Builds a COUNT query to get the number of rows in a table
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <returns>SQL COUNT statement</returns>
    public static string BuildCountQuery(string tableName)
    {
        return $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)}";
    }
    
    /// <summary>
    /// Builds a query to check if a table exists
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <returns>SQL query to check table existence</returns>
    public static string BuildTableExistsQuery(string tableName)
    {
        return "SELECT 1 FROM sqlite_master WHERE type IN ('table', 'view') AND name = @tableName LIMIT 1";
    }
}