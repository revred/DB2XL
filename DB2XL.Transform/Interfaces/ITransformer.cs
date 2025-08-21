namespace DB2XL.Transform.Interfaces;

/// <summary>
/// SQLite type affinity for transformer context
/// </summary>
public enum SqliteAffinity
{
    Integer,
    Real,
    Text,
    Blob,
    Null
}

/// <summary>
/// Context information for cell-level transformations
/// </summary>
/// <param name="Table">Table name</param>
/// <param name="Column">Column name</param>
/// <param name="RowIndex">Zero-based row index</param>
/// <param name="Affinity">SQLite type affinity</param>
public sealed record CellContext(string Table, string Column, int RowIndex, SqliteAffinity Affinity);

/// <summary>
/// Context information for row-level transformations
/// </summary>
/// <param name="Table">Table name</param>
/// <param name="RowIndex">Zero-based row index</param>
public sealed record RowContext(string Table, int RowIndex);

/// <summary>
/// Core interface for cell-level data transformations
/// </summary>
public interface ICellTransformer
{
    /// <summary>
    /// Determines if this transformer can be applied to the given context
    /// </summary>
    /// <param name="ctx">Cell context information</param>
    /// <returns>True if transformer should be applied</returns>
    bool CanApply(CellContext ctx);

    /// <summary>
    /// Transforms a raw cell value to human-readable text
    /// </summary>
    /// <param name="ctx">Cell context information</param>
    /// <param name="raw">Raw value as string (may be null)</param>
    /// <returns>Transformed text representation</returns>
    string? Transform(CellContext ctx, string? raw);
}

/// <summary>
/// Interface for row-level transformations that can add/modify multiple columns
/// </summary>
public interface IRowTransformer
{
    /// <summary>
    /// Determines if this transformer can be applied to the given row
    /// </summary>
    /// <param name="ctx">Row context information</param>
    /// <returns>True if transformer should be applied</returns>
    bool CanApply(RowContext ctx);

    /// <summary>
    /// Transforms an entire row, potentially adding new columns
    /// </summary>
    /// <param name="ctx">Row context information</param>
    /// <param name="rawRow">Raw row data as column name -> value pairs</param>
    /// <returns>Transformed row data with potential new columns</returns>
    IReadOnlyDictionary<string, string?> Transform(RowContext ctx, IReadOnlyDictionary<string, string?> rawRow);
}

/// <summary>
/// Specialized cell transformer that operates on a specific column
/// </summary>
public interface IColumnTransformer : ICellTransformer
{
    /// <summary>
    /// The column name this transformer targets
    /// </summary>
    string ColumnName { get; }
}

/// <summary>
/// Exception thrown when transformer operations fail
/// </summary>
public class TransformerException : Exception
{
    public string TransformerName { get; }
    public CellContext? CellContext { get; }

    public TransformerException(string transformerName, string message) 
        : base(message)
    {
        TransformerName = transformerName;
    }

    public TransformerException(string transformerName, string message, Exception innerException) 
        : base(message, innerException)
    {
        TransformerName = transformerName;
    }

    public TransformerException(string transformerName, CellContext cellContext, string message) 
        : base(message)
    {
        TransformerName = transformerName;
        CellContext = cellContext;
    }

    public TransformerException(string transformerName, CellContext cellContext, string message, Exception innerException) 
        : base(message, innerException)
    {
        TransformerName = transformerName;
        CellContext = cellContext;
    }
}

/// <summary>
/// Base class for simple cell transformers with common functionality
/// </summary>
public abstract class CellTransformerBase : ICellTransformer
{
    protected readonly IDictionary<string, string> Configuration;

    protected CellTransformerBase(IDictionary<string, string> configuration)
    {
        Configuration = configuration ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// Default implementation - can be overridden for more complex logic
    /// </summary>
    public virtual bool CanApply(CellContext ctx) => true;

    /// <summary>
    /// Abstract method for implementing the transformation logic
    /// </summary>
    public abstract string? Transform(CellContext ctx, string? raw);

    /// <summary>
    /// Helper to get configuration value with optional default
    /// </summary>
    protected string GetConfig(string key, string defaultValue = "")
    {
        return Configuration.TryGetValue(key, out var value) ? value : defaultValue;
    }

    /// <summary>
    /// Helper to get boolean configuration value
    /// </summary>
    protected bool GetConfigBool(string key, bool defaultValue = false)
    {
        return Configuration.TryGetValue(key, out var value) && bool.TryParse(value, out var result) ? result : defaultValue;
    }

    /// <summary>
    /// Helper to get integer configuration value
    /// </summary>
    protected int GetConfigInt(string key, int defaultValue = 0)
    {
        return Configuration.TryGetValue(key, out var value) && int.TryParse(value, out var result) ? result : defaultValue;
    }
}