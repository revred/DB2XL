namespace DB2XL.Core.Models;

/// <summary>
/// Represents a table node in the database relationship graph
/// </summary>
public sealed record GraphNode(
    string TableName,
    string NodeType = "table")
{
    /// <summary>
    /// Number of rows in the table (if known)
    /// </summary>
    public long? RowCount { get; init; }
    
    /// <summary>
    /// Columns in this table
    /// </summary>
    public IReadOnlyList<ColumnInfo> Columns { get; init; } = Array.Empty<ColumnInfo>();
    
    /// <summary>
    /// Primary key information
    /// </summary>
    public PrimaryKeyInfo? PrimaryKey { get; init; }
    
    /// <summary>
    /// Additional metadata for analysis
    /// </summary>
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = 
        new Dictionary<string, object?>();
    
    /// <summary>
    /// Indicates if this is a view rather than a table
    /// </summary>
    public bool IsView => NodeType.Equals("view", StringComparison.OrdinalIgnoreCase);
    
    /// <summary>
    /// Gets the display name for this node
    /// </summary>
    public string DisplayName => TableName;
}