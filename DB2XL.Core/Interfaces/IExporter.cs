using DB2XL.Core.Models;

namespace DB2XL.Core.Interfaces;

/// <summary>
/// Base interface for all export implementations
/// </summary>
public interface IExporter
{
    /// <summary>
    /// Exports data from a SQLite database to the target format
    /// </summary>
    /// <param name="sourcePath">Path to the SQLite database</param>
    /// <param name="outputPath">Path for the output file</param>
    /// <param name="options">Export options</param>
    /// <returns>Export result with statistics and metadata</returns>
    Task<ExportResult> ExportAsync(string sourcePath, string outputPath, IExportOptions options);
    
    /// <summary>
    /// Validates that the export can be performed with the given options
    /// </summary>
    /// <param name="sourcePath">Path to the SQLite database</param>
    /// <param name="options">Export options</param>
    /// <returns>Validation result with any warnings or errors</returns>
    ValidationResult ValidateExport(string sourcePath, IExportOptions options);
}

/// <summary>
/// Base interface for export options
/// </summary>
public interface IExportOptions
{
    /// <summary>
    /// Command timeout in seconds for database operations
    /// </summary>
    int CommandTimeoutSeconds { get; }
    
    /// <summary>
    /// Filter for table names (supports wildcards)
    /// </summary>
    string? TableNameFilter { get; }
    
    /// <summary>
    /// Whether to include database views
    /// </summary>
    bool IncludeViews { get; }
    
    /// <summary>
    /// Whether to order rows deterministically
    /// </summary>
    bool OrderRowsDeterministically { get; }
}