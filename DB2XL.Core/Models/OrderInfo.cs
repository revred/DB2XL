using DB2XL.Core.Enums;

namespace DB2XL.Core.Models;

/// <summary>
/// Information about how to order rows during export
/// </summary>
public sealed record OrderInfo(OrderMode Mode, IReadOnlyList<string> Columns)
{
    /// <summary>
    /// Whether ordering is deterministic
    /// </summary>
    public bool IsDeterministic => Mode != OrderMode.None;
    
    /// <summary>
    /// Creates an OrderInfo for no ordering
    /// </summary>
    public static OrderInfo None() => new(OrderMode.None, Array.Empty<string>());
    
    /// <summary>
    /// Creates an OrderInfo for rowid ordering
    /// </summary>
    public static OrderInfo ByRowId() => new(OrderMode.Rowid, new[] { "rowid" });
    
    /// <summary>
    /// Creates an OrderInfo for primary key ordering
    /// </summary>
    public static OrderInfo ByPrimaryKey(IReadOnlyList<string> pkColumns) => 
        new(OrderMode.PrimaryKey, pkColumns);
}