namespace DB2XL.Core.Models;

/// <summary>
/// Information about a database column
/// </summary>
public sealed record ColumnInfo(
    string Name, 
    string Type, 
    bool NotNull, 
    object? DefaultValue, 
    bool IsPrimaryKey)
{
    /// <summary>
    /// Whether this column allows NULL values
    /// </summary>
    public bool IsNullable => !NotNull;
    
    /// <summary>
    /// Whether this column has a default value
    /// </summary>
    public bool HasDefault => DefaultValue != null;
}