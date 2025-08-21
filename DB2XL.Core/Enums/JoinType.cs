namespace DB2XL.Core.Enums;

/// <summary>
/// Types of SQL joins supported in queries
/// </summary>
public enum JoinType
{
    /// <summary>
    /// Inner join - returns only matching rows from both tables
    /// </summary>
    Inner,
    
    /// <summary>
    /// Left join - returns all rows from left table, matched rows from right
    /// </summary>
    Left,
    
    /// <summary>
    /// Right join - returns all rows from right table, matched rows from left
    /// </summary>
    Right,
    
    /// <summary>
    /// Full outer join - returns all rows from both tables
    /// </summary>
    Full
}