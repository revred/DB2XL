using DB2XL.Core.Exceptions;

namespace DB2XL.Core.Tests.Exceptions;

public class ExportExceptionTests
{
    [Fact]
    public void ExportException_WithMessage_SetsMessageCorrectly()
    {
        // Arrange
        const string message = "Test export error";

        // Act
        var exception = new ExportException(message);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.TableName);
        Assert.Null(exception.RowNumber);
        Assert.Null(exception.ColumnName);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ExportException_WithMessageAndInnerException_SetsPropertiesCorrectly()
    {
        // Arrange
        const string message = "Test export error";
        var innerException = new InvalidOperationException("Inner error");

        // Act
        var exception = new ExportException(message, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(innerException, exception.InnerException);
        Assert.Null(exception.TableName);
        Assert.Null(exception.RowNumber);
        Assert.Null(exception.ColumnName);
    }

    [Fact]
    public void ExportException_WithMessageAndTableName_SetsPropertiesCorrectly()
    {
        // Arrange
        const string message = "Test export error";
        const string tableName = "TestTable";

        // Act
        var exception = new ExportException(message, tableName);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(tableName, exception.TableName);
        Assert.Null(exception.RowNumber);
        Assert.Null(exception.ColumnName);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ExportException_WithMessageTableNameAndRowNumber_SetsPropertiesCorrectly()
    {
        // Arrange
        const string message = "Test export error";
        const string tableName = "TestTable";
        const int rowNumber = 42;

        // Act
        var exception = new ExportException(message, tableName, rowNumber);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(tableName, exception.TableName);
        Assert.Equal(rowNumber, exception.RowNumber);
        Assert.Null(exception.ColumnName);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ExportException_InitProperties_CanBeSet()
    {
        // Arrange
        const string message = "Test export error";
        const string tableName = "TestTable";
        const int rowNumber = 42;
        const string columnName = "TestColumn";

        // Act
        var exception = new ExportException(message)
        {
            TableName = tableName,
            RowNumber = rowNumber,
            ColumnName = columnName
        };

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(tableName, exception.TableName);
        Assert.Equal(rowNumber, exception.RowNumber);
        Assert.Equal(columnName, exception.ColumnName);
    }
}