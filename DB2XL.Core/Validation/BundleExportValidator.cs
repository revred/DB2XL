using DB2XL.Core.Models;
using System.Text.RegularExpressions;

namespace DB2XL.Core.Validation;

/// <summary>
/// Validates bundle export options for correctness and security.
/// Ensures all required configuration is present and paths are safe.
/// </summary>
public sealed class BundleExportValidator
{
    private static readonly Regex InvalidPathCharsRegex = new(@"[<>:""|?*]", RegexOptions.Compiled);
    private const int MaxPathLength = 260; // Windows path limit
    private const int MaxFileNameLength = 255;

    /// <summary>
    /// Validates all aspects of bundle export options.
    /// Returns comprehensive validation result with specific error messages.
    /// </summary>
    /// <param name="options">Bundle export options to validate</param>
    /// <returns>Validation result indicating success/failure with detailed errors</returns>
    public ValidationResult Validate(BundleExportOptions options)
    {
        if (options == null)
        {
            return ValidationResult.Failure("Bundle export options cannot be null");
        }

        var errors = new List<string>();

        ValidateIndexWorkbookName(options.IndexWorkbookName, errors);
        ValidateDirectoryNames(options, errors);
        ValidateBundleRootPath(options.BundleRootPath, errors);
        ValidateSampleConfiguration(options, errors);

        return errors.Count == 0 
            ? ValidationResult.Success() 
            : ValidationResult.Failure(errors);
    }

    /// <summary>
    /// Validates individual table partition configurations.
    /// Ensures partition strategies are correctly configured.
    /// </summary>
    /// <param name="config">Table partition configuration to validate</param>
    /// <returns>Validation result for the partition configuration</returns>
    public ValidationResult ValidatePartitionConfig(TablePartitionConfig config)
    {
        if (config == null)
        {
            return ValidationResult.Failure("Partition configuration cannot be null");
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.TableName))
        {
            errors.Add("Table name cannot be null or empty");
        }
        else if (config.TableName.Length > 128)
        {
            errors.Add("Table name cannot exceed 128 characters");
        }

        ValidatePartitionStrategy(config, errors);

        return errors.Count == 0 
            ? ValidationResult.Success() 
            : ValidationResult.Failure(errors);
    }

    private void ValidateIndexWorkbookName(string indexWorkbookName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(indexWorkbookName))
        {
            errors.Add("Index workbook name cannot be null or empty");
            return;
        }

        if (!indexWorkbookName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Index workbook name must have .xlsx extension");
        }

        if (indexWorkbookName.Length > MaxFileNameLength)
        {
            errors.Add($"Index workbook name cannot exceed {MaxFileNameLength} characters");
        }

        if (InvalidPathCharsRegex.IsMatch(indexWorkbookName))
        {
            errors.Add("Index workbook name contains invalid characters");
        }

        var fileName = Path.GetFileNameWithoutExtension(indexWorkbookName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            errors.Add("Index workbook name must have a valid filename before the extension");
        }
    }

    private void ValidateDirectoryNames(BundleExportOptions options, List<string> errors)
    {
        ValidateDirectoryName(options.ManifestDirectoryName, "Manifest directory name", errors);
        ValidateDirectoryName(options.TablesDirectoryName, "Tables directory name", errors);

        // Check for conflicts
        if (options.ManifestDirectoryName.Equals(options.TablesDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Manifest and tables directory names cannot be the same");
        }

        // Ensure they don't conflict with the index workbook
        var workbookName = Path.GetFileNameWithoutExtension(options.IndexWorkbookName);
        if (options.ManifestDirectoryName.Equals(workbookName, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Manifest directory name conflicts with index workbook filename");
        }

        if (options.TablesDirectoryName.Equals(workbookName, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Tables directory name conflicts with index workbook filename");
        }
    }

    private void ValidateDirectoryName(string directoryName, string displayName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            errors.Add($"{displayName} cannot be null or empty");
            return;
        }

        if (directoryName.Length > MaxFileNameLength)
        {
            errors.Add($"{displayName} cannot exceed {MaxFileNameLength} characters");
        }

        if (InvalidPathCharsRegex.IsMatch(directoryName))
        {
            errors.Add($"{displayName} contains invalid characters");
        }

        // Check for reserved names (Windows)
        var reservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        if (reservedNames.Contains(directoryName.ToUpperInvariant()))
        {
            errors.Add($"{displayName} uses a reserved system name");
        }

        // Check for leading/trailing dots or spaces
        if (directoryName.StartsWith('.') || directoryName.EndsWith('.') || 
            directoryName.StartsWith(' ') || directoryName.EndsWith(' '))
        {
            errors.Add($"{displayName} cannot start or end with dots or spaces");
        }
    }

    private void ValidateBundleRootPath(string bundleRootPath, List<string> errors)
    {
        if (string.IsNullOrEmpty(bundleRootPath))
        {
            // Empty root path is valid - will use temp directory
            return;
        }

        if (bundleRootPath.Length > MaxPathLength)
        {
            errors.Add($"Bundle root path cannot exceed {MaxPathLength} characters");
        }

        try
        {
            var fullPath = Path.GetFullPath(bundleRootPath);
            
            // Check if path is rooted (absolute)
            if (!Path.IsPathRooted(fullPath))
            {
                errors.Add("Bundle root path must be an absolute path");
            }

            // Use Path.GetInvalidPathChars() instead of regex for accurate validation
            var invalidChars = Path.GetInvalidPathChars();
            if (bundleRootPath.Any(c => invalidChars.Contains(c)))
            {
                errors.Add("Bundle root path contains invalid characters");
            }
        }
        catch (ArgumentException)
        {
            errors.Add("Bundle root path contains invalid characters");
        }
        catch (NotSupportedException)
        {
            errors.Add("Bundle root path format is not supported");
        }
        catch (Exception ex)
        {
            errors.Add($"Bundle root path validation failed: {ex.Message}");
        }
    }

    private void ValidateSampleConfiguration(BundleExportOptions options, List<string> errors)
    {
        if (options.SampleRowLimit <= 0)
        {
            errors.Add("Sample row limit must be greater than 0");
        }

        if (options.SampleRowLimit > 1_000_000)
        {
            errors.Add("Sample row limit cannot exceed 1,000,000 rows for performance reasons");
        }

        // If samples are enabled but Parquet is disabled, that's fine
        // If neither are enabled, warn but don't error
        if (!options.IncludeSamples && !options.GenerateParquet)
        {
            // This is actually valid - JSONL only export
        }
    }


    private void ValidatePartitionStrategy(TablePartitionConfig config, List<string> errors)
    {
        switch (config.Strategy)
        {
            case PartitionStrategy.None:
                // No additional validation needed
                break;

            case PartitionStrategy.RowCount:
                if (config.RowsPerPartition <= 0)
                {
                    errors.Add("Rows per partition must be greater than 0 for RowCount strategy");
                }
                if (config.RowsPerPartition > 10_000_000)
                {
                    errors.Add("Rows per partition cannot exceed 10,000,000 for performance reasons");
                }
                break;

            case PartitionStrategy.TimeBased:
                if (string.IsNullOrWhiteSpace(config.TimeColumn))
                {
                    errors.Add("Time column name is required for TimeBased strategy");
                }
                // TimeGranularity enum validation is handled by the type system
                break;

            case PartitionStrategy.FilterBased:
                if (string.IsNullOrWhiteSpace(config.FilterExpression))
                {
                    errors.Add("Filter expression is required for FilterBased strategy");
                }
                if (string.IsNullOrWhiteSpace(config.FilterLabel))
                {
                    errors.Add("Filter label is required for FilterBased strategy");
                }
                ValidateFilterExpression(config.FilterExpression, errors);
                break;

            default:
                errors.Add($"Unknown partition strategy: {config.Strategy}");
                break;
        }
    }

    private void ValidateFilterExpression(string? filterExpression, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(filterExpression))
        {
            return;
        }

        // Basic SQL injection prevention
        var suspiciousPatterns = new[]
        {
            "DROP ", "DELETE ", "UPDATE ", "INSERT ", "EXEC ", "EXECUTE ",
            "UNION ", "-- ", "/*", "*/"
        };

        var upperExpression = filterExpression.ToUpperInvariant();
        foreach (var pattern in suspiciousPatterns)
        {
            if (upperExpression.Contains(pattern))
            {
                errors.Add($"Filter expression contains potentially unsafe SQL: {pattern.Trim()}");
            }
        }

        // Check for balanced parentheses
        var openCount = filterExpression.Count(c => c == '(');
        var closeCount = filterExpression.Count(c => c == ')');
        if (openCount != closeCount)
        {
            errors.Add("Filter expression has unbalanced parentheses");
        }

        if (filterExpression.Length > 1000)
        {
            errors.Add("Filter expression cannot exceed 1000 characters");
        }
    }
}

/// <summary>
/// Represents the result of a validation operation.
/// Immutable record with success status and error collection.
/// </summary>
public sealed record ValidationResult
{
    /// <summary>Indicates whether validation succeeded.</summary>
    public bool IsValid { get; init; }

    /// <summary>Collection of validation error messages. Empty if IsValid is true.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>Creates a successful validation result.</summary>
    public static ValidationResult Success() => new() { IsValid = true };

    /// <summary>Creates a failed validation result with a single error.</summary>
    public static ValidationResult Failure(string error) => 
        new() { IsValid = false, Errors = new[] { error } };

    /// <summary>Creates a failed validation result with multiple errors.</summary>
    public static ValidationResult Failure(IReadOnlyList<string> errors) => 
        new() { IsValid = false, Errors = errors };

    /// <summary>Creates a failed validation result from an enumerable of errors.</summary>
    public static ValidationResult Failure(IEnumerable<string> errors) => 
        new() { IsValid = false, Errors = errors.ToList() };
}