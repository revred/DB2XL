using DB2XL.Core.Enums;

namespace DB2XL.Core.Models;

/// <summary>
/// Strategy for primary key identification and ordering
/// </summary>
public enum PrimaryKeyStrategy
{
    /// <summary>
    /// Explicit primary key columns defined on the table
    /// </summary>
    ExplicitPrimaryKey,
    
    /// <summary>
    /// Unique index that serves as a primary key substitute
    /// </summary>
    UniqueIndex,
    
    /// <summary>
    /// SQLite implicit rowid column
    /// </summary>
    ImplicitRowId,
    
    /// <summary>
    /// Synthesized primary key from hash of all columns
    /// </summary>
    SyntheticHash,
    
    /// <summary>
    /// No deterministic ordering available
    /// </summary>
    None
}

/// <summary>
/// Information about discovered primary key
/// </summary>
public sealed record PrimaryKeyInfo
{
    /// <summary>
    /// Strategy used to identify the primary key
    /// </summary>
    public PrimaryKeyStrategy Strategy { get; init; }
    
    /// <summary>
    /// Column names that form the primary key (in order)
    /// </summary>
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Human-readable description of the strategy
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// Whether the ordering is deterministic
    /// </summary>
    public bool IsDeterministic { get; init; }
    
    /// <summary>
    /// Additional metadata about the primary key
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}


/// <summary>
/// Index information from sqlite_master
/// </summary>
public sealed record IndexInfo
{
    public string Name { get; init; } = string.Empty;
    public string TableName { get; init; } = string.Empty;
    public bool IsUnique { get; init; }
    public IReadOnlyList<string> Columns { get; init; } = Array.Empty<string>();
    public string? WhereClause { get; init; }
}