namespace DB2XL.Core.Models;

/// <summary>
/// Result of an export operation
/// </summary>
public class ExportResult
{
    /// <summary>
    /// Whether the export completed successfully
    /// </summary>
    public bool Success { get; init; }
    
    /// <summary>
    /// Path to the generated output file
    /// </summary>
    public string OutputPath { get; init; } = string.Empty;
    
    /// <summary>
    /// Total number of tables exported
    /// </summary>
    public int TablesExported { get; init; }
    
    /// <summary>
    /// Total number of rows exported across all tables
    /// </summary>
    public long TotalRowsExported { get; init; }
    
    /// <summary>
    /// Time taken for the export operation
    /// </summary>
    public TimeSpan Duration { get; init; }
    
    /// <summary>
    /// Size of the output file in bytes
    /// </summary>
    public long OutputSizeBytes { get; init; }
    
    /// <summary>
    /// Details for each exported table
    /// </summary>
    public List<TableExportResult> TableResults { get; init; } = new();
    
    /// <summary>
    /// Any warnings generated during export
    /// </summary>
    public List<string> Warnings { get; init; } = new();
    
    /// <summary>
    /// Error message if the export failed
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Export result for a single table
/// </summary>
public class TableExportResult
{
    /// <summary>
    /// Name of the table
    /// </summary>
    public string TableName { get; init; } = string.Empty;
    
    /// <summary>
    /// Number of rows exported
    /// </summary>
    public long RowCount { get; init; }
    
    /// <summary>
    /// Number of columns exported
    /// </summary>
    public int ColumnCount { get; init; }
    
    /// <summary>
    /// SHA256 checksum of the exported data
    /// </summary>
    public string? Checksum { get; init; }
    
    /// <summary>
    /// Whether the table was split across multiple sheets/files
    /// </summary>
    public bool WasSplit { get; init; }
    
    /// <summary>
    /// Number of parts if the table was split
    /// </summary>
    public int SplitParts { get; init; }
}

/// <summary>
/// Result of validation before export
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Whether the export can proceed
    /// </summary>
    public bool IsValid { get; init; }
    
    /// <summary>
    /// Validation errors that prevent export
    /// </summary>
    public List<string> Errors { get; init; } = new();
    
    /// <summary>
    /// Validation warnings that don't prevent export
    /// </summary>
    public List<string> Warnings { get; init; } = new();
    
    /// <summary>
    /// Estimated size of the output in bytes
    /// </summary>
    public long? EstimatedOutputSize { get; init; }
    
    /// <summary>
    /// Tables that will be exported
    /// </summary>
    public List<string> TablesFound { get; init; } = new();
}