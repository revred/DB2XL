using DB2XL.Core.Exceptions;

namespace DB2XL.Core.Tests.Exceptions;

public class ValidationExceptionTests
{
    [Fact]
    public void ValidationException_WithMessageAndErrors_SetsPropertiesCorrectly()
    {
        // Arrange
        const string message = "Validation failed";
        var errors = new[] { "Error 1", "Error 2", "Error 3" };

        // Act
        var exception = new ValidationException(message, errors);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(3, exception.ValidationErrors.Count);
        Assert.Equal("Error 1", exception.ValidationErrors[0]);
        Assert.Equal("Error 2", exception.ValidationErrors[1]);
        Assert.Equal("Error 3", exception.ValidationErrors[2]);
    }

    [Fact]
    public void ValidationException_WithEmptyErrors_CreatesEmptyList()
    {
        // Arrange
        const string message = "Validation failed";
        var errors = Array.Empty<string>();

        // Act
        var exception = new ValidationException(message, errors);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Empty(exception.ValidationErrors);
    }

    [Fact]
    public void ValidationException_ValidationErrorsIsReadOnly()
    {
        // Arrange
        const string message = "Validation failed";
        var errors = new List<string> { "Error 1", "Error 2" };

        // Act
        var exception = new ValidationException(message, errors);

        // Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<string>>(exception.ValidationErrors);
        
        // Should not be able to cast to mutable list
        Assert.False(exception.ValidationErrors is IList<string> mutableList && !mutableList.IsReadOnly);
    }

    [Fact]
    public void ValidationException_InheritsFromExportException()
    {
        // Arrange
        const string message = "Validation failed";
        var errors = new[] { "Error 1" };

        // Act
        var exception = new ValidationException(message, errors);

        // Assert
        Assert.IsAssignableFrom<ExportException>(exception);
    }

    [Fact]
    public void ValidationException_CanSetTableNameAndRowNumber()
    {
        // Arrange
        const string message = "Validation failed";
        var errors = new[] { "Error 1" };
        const string tableName = "TestTable";
        const int rowNumber = 10;

        // Act
        var exception = new ValidationException(message, errors)
        {
            TableName = tableName,
            RowNumber = rowNumber
        };

        // Assert
        Assert.Equal(tableName, exception.TableName);
        Assert.Equal(rowNumber, exception.RowNumber);
    }
}