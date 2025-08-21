using System.ComponentModel.DataAnnotations;

namespace DB2XL.Core.Models;

/// <summary>
/// Pagination information for limiting and offsetting query results
/// Supports deterministic pagination when combined with proper ordering
/// </summary>
public sealed record PaginationInfo(
    int? Limit = null,
    int? Offset = null)
{
    /// <summary>
    /// Validates pagination parameters
    /// </summary>
    public bool IsValid =>
        (Limit == null || Limit > 0) &&
        (Offset == null || Offset >= 0) &&
        (Offset == null || Limit != null); // Offset requires Limit
    
    /// <summary>
    /// Gets whether pagination is effectively applied
    /// </summary>
    public bool HasPagination => Limit.HasValue || Offset.HasValue;
    
    /// <summary>
    /// Gets the effective limit (defaults to no limit if not specified)
    /// </summary>
    public int? EffectiveLimit => Limit;
    
    /// <summary>
    /// Gets the effective offset (defaults to 0 if not specified)
    /// </summary>
    public int EffectiveOffset => Offset ?? 0;
    
    /// <summary>
    /// Creates pagination for a specific page and page size
    /// </summary>
    /// <param name="pageNumber">1-based page number</param>
    /// <param name="pageSize">Number of records per page</param>
    /// <returns>PaginationInfo with calculated limit and offset</returns>
    public static PaginationInfo ForPage(int pageNumber, int pageSize)
    {
        if (pageNumber < 1)
            throw new ArgumentException("Page number must be >= 1", nameof(pageNumber));
        if (pageSize < 1)
            throw new ArgumentException("Page size must be >= 1", nameof(pageSize));
            
        return new PaginationInfo(
            Limit: pageSize,
            Offset: (pageNumber - 1) * pageSize);
    }
    
    /// <summary>
    /// Creates pagination with only a limit
    /// </summary>
    public static PaginationInfo WithLimit(int limit)
    {
        if (limit < 1)
            throw new ArgumentException("Limit must be >= 1", nameof(limit));
            
        return new PaginationInfo(Limit: limit);
    }
    
    /// <summary>
    /// Creates pagination with limit and offset
    /// </summary>
    public static PaginationInfo WithLimitAndOffset(int limit, int offset)
    {
        if (limit < 1)
            throw new ArgumentException("Limit must be >= 1", nameof(limit));
        if (offset < 0)
            throw new ArgumentException("Offset must be >= 0", nameof(offset));
            
        return new PaginationInfo(Limit: limit, Offset: offset);
    }
    
    /// <summary>
    /// Gets the SQL clause for this pagination
    /// </summary>
    public string ToSqlClause()
    {
        var parts = new List<string>();
        
        if (Limit.HasValue)
            parts.Add($"LIMIT {Limit.Value}");
            
        if (Offset.HasValue && Offset.Value > 0)
            parts.Add($"OFFSET {Offset.Value}");
            
        return string.Join(" ", parts);
    }
    
    /// <summary>
    /// Gets a string representation for debugging
    /// </summary>
    public override string ToString()
    {
        if (!HasPagination)
            return "No pagination";
            
        return Limit.HasValue && Offset.HasValue
            ? $"LIMIT {Limit} OFFSET {Offset}"
            : Limit.HasValue
                ? $"LIMIT {Limit}"
                : $"OFFSET {Offset}";
    }
}

/// <summary>
/// Ordering information for deterministic pagination
/// Required for stable pagination results across runs
/// </summary>
public sealed record OrderByInfo(
    string Column,
    SortDirection Direction = SortDirection.Ascending)
{
    /// <summary>
    /// Validates the ordering specification
    /// </summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(Column);
    
    /// <summary>
    /// Gets the SQL representation of this ordering
    /// </summary>
    public string ToSql() => $"\"{EscapeIdentifier(Column)}\" {DirectionToSql(Direction)}";
    
    /// <summary>
    /// Escapes SQLite identifier by doubling quotes
    /// </summary>
    private static string EscapeIdentifier(string identifier) =>
        identifier.Replace("\"", "\"\"");
    
    /// <summary>
    /// Converts sort direction to SQL
    /// </summary>
    private static string DirectionToSql(SortDirection direction) =>
        direction == SortDirection.Ascending ? "ASC" : "DESC";
    
    /// <summary>
    /// Gets a string representation for debugging
    /// </summary>
    public override string ToString() => $"{Column} {Direction}";
}

/// <summary>
/// Sort direction for ordering
/// </summary>
public enum SortDirection
{
    /// <summary>
    /// Ascending order (1, 2, 3... or A, B, C...)
    /// </summary>
    Ascending,
    
    /// <summary>
    /// Descending order (3, 2, 1... or Z, Y, X...)
    /// </summary>
    Descending
}