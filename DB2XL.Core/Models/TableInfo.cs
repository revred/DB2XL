namespace DB2XL.Core.Models;

/// <summary>
/// Information about a database table or view
/// </summary>
public sealed record TableInfo(string Name, string Type)
{
    /// <summary>
    /// Whether this is a view
    /// </summary>
    public bool IsView => Type.Equals("view", StringComparison.OrdinalIgnoreCase);
    
    /// <summary>
    /// Whether this is a table
    /// </summary>
    public bool IsTable => Type.Equals("table", StringComparison.OrdinalIgnoreCase);
}