namespace DB2XL.Core.Exceptions;

/// <summary>
/// Base exception for all export-related errors
/// </summary>
public class ExportException : Exception
{
    /// <summary>
    /// The table being processed when the error occurred
    /// </summary>
    public string? TableName { get; init; }
    
    /// <summary>
    /// The row number being processed when the error occurred
    /// </summary>
    public int? RowNumber { get; init; }
    
    /// <summary>
    /// The column being processed when the error occurred
    /// </summary>
    public string? ColumnName { get; init; }
    
    public ExportException(string message) : base(message) { }
    
    public ExportException(string message, Exception innerException) 
        : base(message, innerException) { }
    
    public ExportException(string message, string tableName) : base(message)
    {
        TableName = tableName;
    }
    
    public ExportException(string message, string tableName, int rowNumber) : base(message)
    {
        TableName = tableName;
        RowNumber = rowNumber;
    }
}

/// <summary>
/// Exception thrown when validation fails
/// </summary>
public class ValidationException : ExportException
{
    /// <summary>
    /// List of validation errors
    /// </summary>
    public IReadOnlyList<string> ValidationErrors { get; }
    
    public ValidationException(string message, IEnumerable<string> errors) : base(message)
    {
        ValidationErrors = errors.ToList().AsReadOnly();
    }
}

/// <summary>
/// Exception thrown when data conversion fails
/// </summary>
public class DataConversionException : ExportException
{
    /// <summary>
    /// The original value that failed to convert
    /// </summary>
    public object? OriginalValue { get; init; }
    
    /// <summary>
    /// The target type for the conversion
    /// </summary>
    public Type? TargetType { get; init; }
    
    public DataConversionException(string message, object? originalValue, Type targetType) 
        : base(message)
    {
        OriginalValue = originalValue;
        TargetType = targetType;
    }
}