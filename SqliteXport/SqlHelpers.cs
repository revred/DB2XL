using System.Text;

namespace DB2XL;

internal static class SqlHelpers
{
    internal static string Q(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    internal static string BuildSelectSql(string tableName, IReadOnlyList<Col> columns, OrderInfo orderInfo, bool deterministic)
    {
        var sb = new StringBuilder("SELECT ");
        
        for (int i = 0; i < columns.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Q(columns[i].Name));
        }
        
        sb.Append(" FROM ").Append(Q(tableName));

        if (deterministic && orderInfo.Mode != OrderMode.None)
        {
            sb.Append(" ORDER BY ");
            for (int i = 0; i < orderInfo.Columns.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Q(orderInfo.Columns[i])).Append(" ASC");
            }
        }

        return sb.ToString();
    }
}