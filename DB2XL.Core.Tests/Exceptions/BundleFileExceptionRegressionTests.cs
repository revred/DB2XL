using DB2XL.Core.Exceptions;
using Xunit;

namespace DB2XL.Core.Tests.Exceptions;

/// <summary>
/// Comprehensive regression tests for BundleFileException to detect error handling issues
/// </summary>
public class BundleFileExceptionRegressionTests
{
    #region Constructor Tests

    [Fact]
    public void BundleFileException_BasicConstructor_SetsPropertiesCorrectly()
    {
        // Regression: Basic constructor should set all properties correctly
        var filePath = @"C:\temp\test.xlsx";
        var operation = FileOperation.Write;
        var message = "Write operation failed";
        
        var exception = new BundleFileException(message, filePath, operation);
        
        Assert.Equal(message, exception.Message);
        Assert.Equal(filePath, exception.FilePath);
        Assert.Equal(operation, exception.Operation);
        Assert.Equal("BUNDLE_FILE_ERROR", exception.ErrorCode);
        Assert.Null(exception.SizeInfo);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void BundleFileException_WithSizeInfo_SetsPropertiesCorrectly()
    {
        // Regression: Constructor with size info should work correctly
        var filePath = @"C:\temp\test.xlsx";
        var operation = FileOperation.Create;
        var message = "File size mismatch";
        var sizeInfo = new FileSizeInfo(1000, 500);
        
        var exception = new BundleFileException(message, filePath, operation, sizeInfo);
        
        Assert.Equal(message, exception.Message);
        Assert.Equal(filePath, exception.FilePath);
        Assert.Equal(operation, exception.Operation);
        Assert.Equal(sizeInfo, exception.SizeInfo);
        Assert.Equal(1000, exception.SizeInfo.ExpectedSize);
        Assert.Equal(500, exception.SizeInfo.ActualSize);
    }

    [Fact]
    public void BundleFileException_WithInnerException_SetsPropertiesCorrectly()
    {
        // Regression: Inner exceptions should be preserved
        var filePath = @"C:\temp\test.xlsx";
        var operation = FileOperation.Read;
        var message = "Read operation failed";
        var innerException = new InvalidOperationException("File is locked");
        
        var exception = new BundleFileException(message, filePath, operation, null, innerException);
        
        Assert.Equal(message, exception.Message);
        Assert.Equal(filePath, exception.FilePath);
        Assert.Equal(operation, exception.Operation);
        Assert.Equal(innerException, exception.InnerException);
    }

    #endregion

    #region Context Information Tests

    [Fact]
    public void BundleFileException_Context_ContainsRequiredInformation()
    {
        // Critical: Context should contain all required diagnostic information
        var filePath = @"C:\temp\test.xlsx";
        var operation = FileOperation.Hash;
        var message = "Hash calculation failed";
        
        var exception = new BundleFileException(message, filePath, operation);
        
        Assert.Contains("FilePath", exception.Context.Keys);
        Assert.Contains("Operation", exception.Context.Keys);
        Assert.Contains("DirectoryExists", exception.Context.Keys);
        
        Assert.Equal(filePath, exception.Context["FilePath"]);
        Assert.Equal(operation.ToString(), exception.Context["Operation"]);
    }

    [Fact]
    public void BundleFileException_ContextWithSizeInfo_ContainsSizeInformation()
    {
        // Regression: Size info should be included in context
        var filePath = @"C:\temp\test.xlsx";
        var operation = FileOperation.Write;
        var message = "Size validation failed";
        var sizeInfo = new FileSizeInfo(2000, 1500);
        
        var exception = new BundleFileException(message, filePath, operation, sizeInfo);
        
        Assert.Contains("ExpectedSize", exception.Context.Keys);
        Assert.Contains("ActualSize", exception.Context.Keys);
        Assert.Equal(2000L, exception.Context["ExpectedSize"]);
        Assert.Equal(1500L, exception.Context["ActualSize"]);
    }

    [Fact]
    public void BundleFileException_Context_HandlesFileAccessErrors()
    {
        // Security: Should not throw when file access fails during context creation
        var filePath = @"C:\invalid\nonexistent\test.xlsx";
        var operation = FileOperation.Read;
        var message = "File not found";
        
        var exception = new BundleFileException(message, filePath, operation);
        
        // Should not throw and should contain basic context
        Assert.Contains("FilePath", exception.Context.Keys);
        Assert.Contains("Operation", exception.Context.Keys);
        Assert.Contains("DirectoryExists", exception.Context.Keys);
    }

    #endregion

    #region Recovery Suggestion Tests

    [Fact]
    public void BundleFileException_CreateOperation_ProvideCorrectRecoverySuggestion()
    {
        // Regression: Create operations should have appropriate recovery suggestions
        var exception = new BundleFileException("Create failed", @"C:\temp\test.xlsx", FileOperation.Create);
        
        Assert.NotNull(exception.RecoverySuggestion);
        Assert.Contains("directory exists", exception.RecoverySuggestion);
        Assert.Contains("write permissions", exception.RecoverySuggestion);
        Assert.Contains("disk space", exception.RecoverySuggestion);
    }

    [Fact]
    public void BundleFileException_WriteOperation_ProvideCorrectRecoverySuggestion()
    {
        // Regression: Write operations should have appropriate recovery suggestions
        var exception = new BundleFileException("Write failed", @"C:\temp\test.xlsx", FileOperation.Write);
        
        Assert.NotNull(exception.RecoverySuggestion);
        Assert.Contains("write permissions", exception.RecoverySuggestion);
        Assert.Contains("disk space", exception.RecoverySuggestion);
        Assert.Contains("locked", exception.RecoverySuggestion);
    }

    [Fact]
    public void BundleFileException_ReadOperation_ProvideCorrectRecoverySuggestion()
    {
        // Regression: Read operations should have appropriate recovery suggestions
        var exception = new BundleFileException("Read failed", @"C:\temp\test.xlsx", FileOperation.Read);
        
        Assert.NotNull(exception.RecoverySuggestion);
        Assert.Contains("file exists", exception.RecoverySuggestion);
        Assert.Contains("read permissions", exception.RecoverySuggestion);
    }

    [Fact]
    public void BundleFileException_DeleteOperation_ProvideCorrectRecoverySuggestion()
    {
        // Regression: Delete operations should have appropriate recovery suggestions
        var exception = new BundleFileException("Delete failed", @"C:\temp\test.xlsx", FileOperation.Delete);
        
        Assert.NotNull(exception.RecoverySuggestion);
        Assert.Contains("file exists", exception.RecoverySuggestion);
        Assert.Contains("delete permissions", exception.RecoverySuggestion);
        Assert.Contains("locked", exception.RecoverySuggestion);
    }

    [Fact]
    public void BundleFileException_HashOperation_ProvideCorrectRecoverySuggestion()
    {
        // Regression: Hash operations should have appropriate recovery suggestions
        var exception = new BundleFileException("Hash failed", @"C:\temp\test.xlsx", FileOperation.Hash);
        
        Assert.NotNull(exception.RecoverySuggestion);
        Assert.Contains("file is not being modified", exception.RecoverySuggestion);
    }

    [Fact]
    public void BundleFileException_CompressOperation_ProvideGenericRecoverySuggestion()
    {
        // Edge case: Operations without specific suggestions should provide generic guidance
        var exception = new BundleFileException("Compress failed", @"C:\temp\test.xlsx", FileOperation.Compress);
        
        Assert.NotNull(exception.RecoverySuggestion);
        Assert.Contains("permissions", exception.RecoverySuggestion);
        Assert.Contains("not in use", exception.RecoverySuggestion);
    }

    #endregion

    #region Retry Logic Tests

    [Fact]
    public void BundleFileException_WriteOperation_IsRetryable()
    {
        // Critical: Write operations should be retryable
        var exception = new BundleFileException("Write failed", @"C:\temp\test.xlsx", FileOperation.Write);
        
        Assert.True(exception.IsRetryable);
    }

    [Fact]
    public void BundleFileException_DeleteOperation_IsRetryable()
    {
        // Critical: Delete operations should be retryable
        var exception = new BundleFileException("Delete failed", @"C:\temp\test.xlsx", FileOperation.Delete);
        
        Assert.True(exception.IsRetryable);
    }

    [Fact]
    public void BundleFileException_HashOperation_IsRetryable()
    {
        // Critical: Hash operations should be retryable
        var exception = new BundleFileException("Hash failed", @"C:\temp\test.xlsx", FileOperation.Hash);
        
        Assert.True(exception.IsRetryable);
    }

    [Fact]
    public void BundleFileException_CreateOperation_IsNotRetryable()
    {
        // Regression: Create operations should not be retryable by default
        var exception = new BundleFileException("Create failed", @"C:\temp\test.xlsx", FileOperation.Create);
        
        Assert.False(exception.IsRetryable);
    }

    [Fact]
    public void BundleFileException_ReadOperation_IsNotRetryable()
    {
        // Regression: Read operations should not be retryable by default
        var exception = new BundleFileException("Read failed", @"C:\temp\test.xlsx", FileOperation.Read);
        
        Assert.False(exception.IsRetryable);
    }

    #endregion

    #region FileSizeInfo Tests

    [Fact]
    public void FileSizeInfo_Record_WorksCorrectly()
    {
        // Regression: FileSizeInfo record should work correctly
        var sizeInfo = new FileSizeInfo(1000, 800);
        
        Assert.Equal(1000, sizeInfo.ExpectedSize);
        Assert.Equal(800, sizeInfo.ActualSize);
    }

    [Fact]
    public void FileSizeInfo_Equality_WorksCorrectly()
    {
        // Regression: Record equality should work
        var sizeInfo1 = new FileSizeInfo(1000, 800);
        var sizeInfo2 = new FileSizeInfo(1000, 800);
        var sizeInfo3 = new FileSizeInfo(1000, 900);
        
        Assert.Equal(sizeInfo1, sizeInfo2);
        Assert.NotEqual(sizeInfo1, sizeInfo3);
    }

    #endregion

    #region FileOperation Enum Tests

    [Fact]
    public void FileOperation_AllValues_AreDefined()
    {
        // Regression: Ensure all expected file operations are defined
        var operations = Enum.GetValues<FileOperation>();
        
        Assert.Contains(FileOperation.Create, operations);
        Assert.Contains(FileOperation.Write, operations);
        Assert.Contains(FileOperation.Read, operations);
        Assert.Contains(FileOperation.Delete, operations);
        Assert.Contains(FileOperation.Hash, operations);
        Assert.Contains(FileOperation.Compress, operations);
    }

    [Fact]
    public void FileOperation_ToString_WorksCorrectly()
    {
        // Regression: ToString should work for all operations
        Assert.Equal("Write", FileOperation.Write.ToString());
        Assert.Equal("Read", FileOperation.Read.ToString());
        Assert.Equal("Create", FileOperation.Create.ToString());
        Assert.Equal("Delete", FileOperation.Delete.ToString());
        Assert.Equal("Hash", FileOperation.Hash.ToString());
        Assert.Equal("Compress", FileOperation.Compress.ToString());
    }

    #endregion

    #region Inheritance Tests

    [Fact]
    public void BundleFileException_InheritsFromBundleExportException()
    {
        // Critical: Inheritance hierarchy should be correct
        var exception = new BundleFileException("Test", @"C:\temp\test.xlsx", FileOperation.Read);
        
        Assert.IsAssignableFrom<BundleExportException>(exception);
        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact]
    public void BundleFileException_BaseProperties_AreSetCorrectly()
    {
        // Regression: Base class properties should be initialized correctly
        var exception = new BundleFileException("Test message", @"C:\temp\test.xlsx", FileOperation.Write);
        
        Assert.Equal("BUNDLE_FILE_ERROR", exception.ErrorCode);
        Assert.NotNull(exception.Context);
        Assert.NotNull(exception.RecoverySuggestion);
    }

    #endregion

    #region Error Message Tests

    [Fact]
    public void BundleFileException_Message_PreservesOriginalMessage()
    {
        // Critical: Original error message should be preserved
        var originalMessage = "Original error message with details";
        var exception = new BundleFileException(originalMessage, @"C:\temp\test.xlsx", FileOperation.Read);
        
        Assert.Equal(originalMessage, exception.Message);
    }

    [Fact]
    public void BundleFileException_WithInnerException_PreservesInnerMessage()
    {
        // Regression: Inner exception message should be accessible
        var innerException = new UnauthorizedAccessException("Access denied to file");
        var exception = new BundleFileException("Outer message", @"C:\temp\test.xlsx", FileOperation.Write, null, innerException);
        
        Assert.Equal("Outer message", exception.Message);
        Assert.Equal("Access denied to file", exception.InnerException?.Message);
    }

    #endregion

    #region Path Handling Tests

    [Fact]
    public void BundleFileException_WindowsPaths_HandledCorrectly()
    {
        // Cross-platform: Windows paths should be handled correctly
        var windowsPath = @"C:\Users\Test\Documents\file.xlsx";
        var exception = new BundleFileException("Test", windowsPath, FileOperation.Read);
        
        Assert.Equal(windowsPath, exception.FilePath);
        Assert.Equal(windowsPath, exception.Context["FilePath"]);
    }

    [Fact]
    public void BundleFileException_UnixPaths_HandledCorrectly()
    {
        // Cross-platform: Unix paths should be handled correctly
        var unixPath = "/home/user/documents/file.xlsx";
        var exception = new BundleFileException("Test", unixPath, FileOperation.Read);
        
        Assert.Equal(unixPath, exception.FilePath);
        Assert.Equal(unixPath, exception.Context["FilePath"]);
    }

    [Fact]
    public void BundleFileException_NetworkPaths_HandledCorrectly()
    {
        // Edge case: Network paths should be handled correctly
        var networkPath = @"\\server\share\folder\file.xlsx";
        var exception = new BundleFileException("Test", networkPath, FileOperation.Read);
        
        Assert.Equal(networkPath, exception.FilePath);
        Assert.Equal(networkPath, exception.Context["FilePath"]);
    }

    [Fact]
    public void BundleFileException_RelativePaths_HandledCorrectly()
    {
        // Edge case: Relative paths should be handled correctly
        var relativePath = @".\temp\file.xlsx";
        var exception = new BundleFileException("Test", relativePath, FileOperation.Read);
        
        Assert.Equal(relativePath, exception.FilePath);
        Assert.Equal(relativePath, exception.Context["FilePath"]);
    }

    #endregion

    #region Serialization Tests

    [Fact]
    public void BundleFileException_Context_IsSerializable()
    {
        // Critical: Context dictionary should be serializable for logging
        var exception = new BundleFileException("Test", @"C:\temp\test.xlsx", FileOperation.Write);
        
        // Should be able to serialize context to JSON
        var contextJson = System.Text.Json.JsonSerializer.Serialize(exception.Context);
        Assert.NotNull(contextJson);
        Assert.Contains("FilePath", contextJson);
        Assert.Contains("Operation", contextJson);
    }

    #endregion

    #region Null Handling Tests

    [Fact]
    public void BundleFileException_NullSizeInfo_HandledCorrectly()
    {
        // Edge case: Null size info should be handled gracefully
        var exception = new BundleFileException("Test", @"C:\temp\test.xlsx", FileOperation.Write, null);
        
        Assert.Null(exception.SizeInfo);
        Assert.DoesNotContain("ExpectedSize", exception.Context.Keys);
        Assert.DoesNotContain("ActualSize", exception.Context.Keys);
    }

    [Fact]
    public void BundleFileException_EmptyFilePath_HandledCorrectly()
    {
        // Edge case: Empty file path should not crash
        var exception = new BundleFileException("Test", "", FileOperation.Read);
        
        Assert.Equal("", exception.FilePath);
        Assert.Equal("", exception.Context["FilePath"]);
    }

    #endregion
}