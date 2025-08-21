using System.ComponentModel.DataAnnotations;

namespace DB2XL.Core.Models;

/// <summary>
/// Represents database attachment information for multi-database queries
/// Supports ATTACH DATABASE statements in SQLite for cross-database joins
/// </summary>
public sealed record AttachInfo(
    string Alias,
    string Type,
    string Path)
{
    /// <summary>
    /// Gets the qualified database reference for use in queries
    /// </summary>
    public string QualifiedReference => $"{Alias}.";
    
    /// <summary>
    /// Validates that the attach configuration is properly formed
    /// </summary>
    public bool IsValid => 
        !string.IsNullOrWhiteSpace(Alias) &&
        !string.IsNullOrWhiteSpace(Type) &&
        !string.IsNullOrWhiteSpace(Path) &&
        IsValidSqliteIdentifier(Alias);
    
    /// <summary>
    /// Generates the ATTACH DATABASE SQL statement
    /// </summary>
    public string ToAttachSql() => 
        $"ATTACH DATABASE @attach_{Alias}_path AS \"{EscapeIdentifier(Alias)}\"";
    
    /// <summary>
    /// Gets the parameter name for the database path
    /// </summary>
    public string PathParameterName => $"attach_{Alias}_path";
    
    /// <summary>
    /// Validates SQLite identifier naming rules
    /// </summary>
    private static bool IsValidSqliteIdentifier(string identifier) =>
        !string.IsNullOrEmpty(identifier) &&
        char.IsLetter(identifier[0]) &&
        identifier.All(c => char.IsLetterOrDigit(c) || c == '_') &&
        identifier.Length <= 64; // SQLite limit
    
    /// <summary>
    /// Escapes SQLite identifier by doubling quotes
    /// </summary>
    private static string EscapeIdentifier(string identifier) =>
        identifier.Replace("\"", "\"\"");
    
    /// <summary>
    /// Gets a string representation for debugging
    /// </summary>
    public override string ToString() => 
        $"ATTACH {Type} '{Path}' AS {Alias}";
}