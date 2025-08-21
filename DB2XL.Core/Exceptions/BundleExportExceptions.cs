namespace DB2XL.Core.Exceptions;

/// <summary>
/// Base exception for all bundle export related errors.
/// Provides structured error information with context and recovery suggestions.
/// </summary>
public class BundleExportException : Exception
{
    /// <summary>Specific error code for programmatic handling.</summary>
    public string ErrorCode { get; }
    
    /// <summary>Additional context information about the error.</summary>
    public IReadOnlyDictionary<string, object?> Context { get; }
    
    /// <summary>Suggests recovery actions or troubleshooting steps.</summary>
    public string? RecoverySuggestion { get; }
    
    /// <summary>Indicates whether the operation can be safely retried.</summary>
    public bool IsRetryable { get; }

    public BundleExportException(
        string message,
        string errorCode = "BUNDLE_EXPORT_ERROR",
        IReadOnlyDictionary<string, object?>? context = null,
        string? recoverySuggestion = null,
        bool isRetryable = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        Context = context ?? new Dictionary<string, object?>();
        RecoverySuggestion = recoverySuggestion;
        IsRetryable = isRetryable;
    }
}

/// <summary>
/// Exception thrown when bundle export configuration validation fails.
/// Contains detailed validation errors and suggestions for correction.
/// </summary>
public class BundleValidationException : BundleExportException
{
    /// <summary>Collection of specific validation errors.</summary>
    public IReadOnlyList<string> ValidationErrors { get; }
    
    /// <summary>Name of the configuration property that failed validation.</summary>
    public string? PropertyName { get; }

    public BundleValidationException(
        IReadOnlyList<string> validationErrors,
        string? propertyName = null)
        : base(
            message: CreateMessage(validationErrors, propertyName),
            errorCode: "BUNDLE_VALIDATION_ERROR",
            context: CreateContext(validationErrors, propertyName),
            recoverySuggestion: "Review and correct the bundle export configuration options.",
            isRetryable: false)
    {
        ValidationErrors = validationErrors;
        PropertyName = propertyName;
    }

    public BundleValidationException(
        string validationError,
        string? propertyName = null)
        : this(new[] { validationError }, propertyName)
    {
    }

    private static string CreateMessage(IReadOnlyList<string> errors, string? propertyName)
    {
        var prefix = string.IsNullOrEmpty(propertyName) 
            ? "Bundle export configuration validation failed"
            : $"Bundle export property '{propertyName}' validation failed";
        
        return errors.Count == 1 
            ? $"{prefix}: {errors[0]}" 
            : $"{prefix} with {errors.Count} errors: {string.Join("; ", errors)}";
    }

    private static Dictionary<string, object?> CreateContext(IReadOnlyList<string> errors, string? propertyName)
    {
        return new Dictionary<string, object?>
        {
            ["ValidationErrors"] = errors,
            ["PropertyName"] = propertyName,
            ["ErrorCount"] = errors.Count
        };
    }
}

/// <summary>
/// Exception thrown when SQLite database access or reading fails.
/// Includes database-specific error information and connection diagnostics.
/// </summary>
public class BundleDatabaseException : BundleExportException
{
    /// <summary>Path to the SQLite database file that caused the error.</summary>
    public string DatabasePath { get; }
    
    /// <summary>SQLite error code if available.</summary>
    public int? SqliteErrorCode { get; }
    
    /// <summary>Name of the table being accessed when error occurred (if applicable).</summary>
    public string? TableName { get; }

    public BundleDatabaseException(
        string message,
        string databasePath,
        int? sqliteErrorCode = null,
        string? tableName = null,
        Exception? innerException = null)
        : base(
            message: message,
            errorCode: "BUNDLE_DATABASE_ERROR",
            context: CreateContext(databasePath, sqliteErrorCode, tableName),
            recoverySuggestion: CreateRecoverySuggestion(sqliteErrorCode),
            isRetryable: IsRetryableError(sqliteErrorCode),
            innerException: innerException)
    {
        DatabasePath = databasePath;
        SqliteErrorCode = sqliteErrorCode;
        TableName = tableName;
    }

    private static Dictionary<string, object?> CreateContext(string databasePath, int? errorCode, string? tableName)
    {
        return new Dictionary<string, object?>
        {
            ["DatabasePath"] = databasePath,
            ["SqliteErrorCode"] = errorCode,
            ["TableName"] = tableName,
            ["FileExists"] = File.Exists(databasePath)
        };
    }

    private static string CreateRecoverySuggestion(int? sqliteErrorCode)
    {
        return sqliteErrorCode switch
        {
            1 => "Check if database file exists and is not corrupted. Verify read permissions.",
            5 => "Database is locked. Close other applications using this database and retry.",
            8 => "Database is read-only. Check file and directory permissions.",
            11 => "Database file is corrupted. Restore from backup if available.",
            14 => "Cannot open database file. Check file path and permissions.",
            _ => "Verify database file accessibility and SQLite compatibility."
        };
    }

    private static bool IsRetryableError(int? sqliteErrorCode)
    {
        return sqliteErrorCode switch
        {
            5 => true,  // SQLITE_BUSY - database is locked
            6 => true,  // SQLITE_LOCKED - table is locked
            _ => false
        };
    }
}

/// <summary>
/// Exception thrown when file I/O operations fail during bundle export.
/// Includes file system diagnostic information and recovery suggestions.
/// </summary>
public class BundleFileException : BundleExportException
{
    /// <summary>File path that caused the error.</summary>
    public string FilePath { get; }
    
    /// <summary>Type of file operation that failed.</summary>
    public FileOperation Operation { get; }
    
    /// <summary>Expected vs actual file size (for size validation failures).</summary>
    public FileSizeInfo? SizeInfo { get; }

    public BundleFileException(
        string message,
        string filePath,
        FileOperation operation,
        FileSizeInfo? sizeInfo = null,
        Exception? innerException = null)
        : base(
            message: message,
            errorCode: "BUNDLE_FILE_ERROR",
            context: CreateContext(filePath, operation, sizeInfo),
            recoverySuggestion: CreateRecoverySuggestion(operation),
            isRetryable: IsRetryableOperation(operation),
            innerException: innerException)
    {
        FilePath = filePath;
        Operation = operation;
        SizeInfo = sizeInfo;
    }

    private static Dictionary<string, object?> CreateContext(string filePath, FileOperation operation, FileSizeInfo? sizeInfo)
    {
        var context = new Dictionary<string, object?>
        {
            ["FilePath"] = filePath,
            ["Operation"] = operation.ToString(),
            ["DirectoryExists"] = Directory.Exists(Path.GetDirectoryName(filePath))
        };
        
        if (sizeInfo != null)
        {
            context["ExpectedSize"] = sizeInfo.ExpectedSize;
            context["ActualSize"] = sizeInfo.ActualSize;
        }
        
        try
        {
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                context["FileSize"] = fileInfo.Length;
                context["LastModified"] = fileInfo.LastWriteTimeUtc;
                context["IsReadOnly"] = fileInfo.IsReadOnly;
            }
        }
        catch
        {
            // Ignore file access errors during context creation
        }
        
        return context;
    }

    private static string CreateRecoverySuggestion(FileOperation operation)
    {
        return operation switch
        {
            FileOperation.Create => "Ensure directory exists and has write permissions. Check available disk space.",
            FileOperation.Write => "Verify write permissions and available disk space. File may be locked by another process.",
            FileOperation.Read => "Check if file exists and has read permissions.",
            FileOperation.Delete => "Verify file exists and delete permissions. File may be locked or in use.",
            FileOperation.Hash => "Ensure file is not being modified during hash calculation.",
            _ => "Check file permissions and ensure file is not in use by another process."
        };
    }

    private static bool IsRetryableOperation(FileOperation operation)
    {
        return operation is FileOperation.Write or FileOperation.Delete or FileOperation.Hash;
    }
}

/// <summary>
/// Exception thrown when data partitioning or processing fails.
/// Includes information about the table and partition configuration.
/// </summary>
public class BundlePartitionException : BundleExportException
{
    /// <summary>Name of the table being partitioned.</summary>
    public string TableName { get; }
    
    /// <summary>Partition configuration that caused the error.</summary>
    public string? PartitionConfig { get; }
    
    /// <summary>Number of rows processed before failure.</summary>
    public long ProcessedRows { get; }

    public BundlePartitionException(
        string message,
        string tableName,
        string? partitionConfig = null,
        long processedRows = 0,
        Exception? innerException = null)
        : base(
            message: message,
            errorCode: "BUNDLE_PARTITION_ERROR",
            context: CreateContext(tableName, partitionConfig, processedRows),
            recoverySuggestion: "Review partition configuration and ensure table data is compatible.",
            isRetryable: false,
            innerException: innerException)
    {
        TableName = tableName;
        PartitionConfig = partitionConfig;
        ProcessedRows = processedRows;
    }

    private static Dictionary<string, object?> CreateContext(string tableName, string? partitionConfig, long processedRows)
    {
        return new Dictionary<string, object?>
        {
            ["TableName"] = tableName,
            ["PartitionConfig"] = partitionConfig,
            ["ProcessedRows"] = processedRows
        };
    }
}

/// <summary>
/// Exception thrown when hash calculation or verification fails.
/// Critical for ensuring data integrity in bundle exports.
/// </summary>
public class BundleHashException : BundleExportException
{
    /// <summary>Path to the file being hashed.</summary>
    public string FilePath { get; }
    
    /// <summary>Expected hash value (for verification failures).</summary>
    public string? ExpectedHash { get; }
    
    /// <summary>Actual computed hash value.</summary>
    public string? ActualHash { get; }
    
    /// <summary>Hashing algorithm used.</summary>
    public string Algorithm { get; }

    public BundleHashException(
        string message,
        string filePath,
        string algorithm = "SHA256",
        string? expectedHash = null,
        string? actualHash = null,
        Exception? innerException = null)
        : base(
            message: message,
            errorCode: "BUNDLE_HASH_ERROR",
            context: CreateContext(filePath, algorithm, expectedHash, actualHash),
            recoverySuggestion: "Verify file integrity and ensure file is not being modified during hashing.",
            isRetryable: true,
            innerException: innerException)
    {
        FilePath = filePath;
        ExpectedHash = expectedHash;
        ActualHash = actualHash;
        Algorithm = algorithm;
    }

    private static Dictionary<string, object?> CreateContext(string filePath, string algorithm, string? expectedHash, string? actualHash)
    {
        return new Dictionary<string, object?>
        {
            ["FilePath"] = filePath,
            ["Algorithm"] = algorithm,
            ["ExpectedHash"] = expectedHash,
            ["ActualHash"] = actualHash,
            ["FileExists"] = File.Exists(filePath)
        };
    }
}

/// <summary>
/// Types of file operations that can fail during bundle export.
/// </summary>
public enum FileOperation
{
    Create,
    Write,
    Read,
    Delete,
    Hash,
    Compress
}

/// <summary>
/// File size information for size validation failures.
/// </summary>
public sealed record FileSizeInfo(long ExpectedSize, long ActualSize);