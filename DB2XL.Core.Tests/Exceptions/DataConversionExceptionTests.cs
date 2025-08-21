using DB2XL.Core.Exceptions;

namespace DB2XL.Core.Tests.Exceptions;

public class DataConversionExceptionTests
{
    [Fact]
    public void DataConversionException_WithAllParameters_SetsPropertiesCorrectly()
    {
        // Arrange
        const string message = "Conversion failed";
        const string originalValue = "invalid_number";
        var targetType = typeof(int);

        // Act
        var exception = new DataConversionException(message, originalValue, targetType);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(originalValue, exception.OriginalValue);
        Assert.Equal(targetType, exception.TargetType);
    }

    [Fact]
    public void DataConversionException_WithNullOriginalValue_SetsNullCorrectly()
    {
        // Arrange
        const string message = "Conversion failed";
        object? originalValue = null;
        var targetType = typeof(string);

        // Act
        var exception = new DataConversionException(message, originalValue, targetType);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.OriginalValue);
        Assert.Equal(targetType, exception.TargetType);
    }

    [Fact]
    public void DataConversionException_WithComplexOriginalValue_SetsCorrectly()
    {
        // Arrange
        const string message = "Conversion failed";
        var originalValue = new { Name = "Test", Value = 42 };
        var targetType = typeof(DateTime);

        // Act
        var exception = new DataConversionException(message, originalValue, targetType);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(originalValue, exception.OriginalValue);
        Assert.Equal(targetType, exception.TargetType);
    }

    [Fact]
    public void DataConversionException_InheritsFromExportException()
    {
        // Arrange
        const string message = "Conversion failed";
        var originalValue = "test";
        var targetType = typeof(int);

        // Act
        var exception = new DataConversionException(message, originalValue, targetType);

        // Assert
        Assert.IsAssignableFrom<ExportException>(exception);
    }

    [Fact]
    public void DataConversionException_CanSetTableNameAndRowNumber()
    {
        // Arrange
        const string message = "Conversion failed";
        var originalValue = "test";
        var targetType = typeof(int);
        const string tableName = "TestTable";
        const int rowNumber = 5;
        const string columnName = "TestColumn";

        // Act
        var exception = new DataConversionException(message, originalValue, targetType)
        {
            TableName = tableName,
            RowNumber = rowNumber,
            ColumnName = columnName
        };

        // Assert
        Assert.Equal(tableName, exception.TableName);
        Assert.Equal(rowNumber, exception.RowNumber);
        Assert.Equal(columnName, exception.ColumnName);
    }

    [Theory]
    [InlineData(typeof(int), "System.Int32")]
    [InlineData(typeof(string), "System.String")]
    [InlineData(typeof(DateTime), "System.DateTime")]
    public void DataConversionException_WithDifferentTargetTypes_StoresCorrectly(Type targetType, string expectedTypeName)
    {
        // Arrange
        const string message = "Conversion failed";
        const string originalValue = "test";

        // Act
        var exception = new DataConversionException(message, originalValue, targetType);

        // Assert
        Assert.Equal(targetType, exception.TargetType);
        Assert.Equal(expectedTypeName, exception.TargetType?.FullName);
    }

    [Fact]
    public void DataConversionException_WithGenericType_StoresCorrectly()
    {
        // Arrange
        const string message = "Conversion failed";
        const string originalValue = "test";
        var targetType = typeof(List<string>);

        // Act
        var exception = new DataConversionException(message, originalValue, targetType);

        // Assert
        Assert.Equal(targetType, exception.TargetType);
        Assert.NotNull(exception.TargetType?.FullName);
        Assert.Contains("List", exception.TargetType.FullName);
        Assert.Contains("String", exception.TargetType.FullName);
    }
}