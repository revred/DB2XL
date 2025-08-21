using DB2XL.Core.Models;

namespace DB2XL.Core.Tests.Models;

public class ColumnInfoTests
{
    [Fact]
    public void ColumnInfo_BasicProperties_AreSetCorrectly()
    {
        // Arrange
        const string name = "TestColumn";
        const string type = "VARCHAR(50)";
        const bool notNull = true;
        const string defaultValue = "default_value";
        const bool isPrimaryKey = false;

        // Act
        var columnInfo = new ColumnInfo(name, type, notNull, defaultValue, isPrimaryKey);

        // Assert
        Assert.Equal(name, columnInfo.Name);
        Assert.Equal(type, columnInfo.Type);
        Assert.Equal(notNull, columnInfo.NotNull);
        Assert.Equal(defaultValue, columnInfo.DefaultValue);
        Assert.Equal(isPrimaryKey, columnInfo.IsPrimaryKey);
    }

    [Fact]
    public void ColumnInfo_IsNullable_ReturnsTrueWhenNotNullIsFalse()
    {
        // Arrange & Act
        var columnInfo = new ColumnInfo("TestColumn", "TEXT", false, null, false);

        // Assert
        Assert.True(columnInfo.IsNullable);
        Assert.False(columnInfo.NotNull);
    }

    [Fact]
    public void ColumnInfo_IsNullable_ReturnsFalseWhenNotNullIsTrue()
    {
        // Arrange & Act
        var columnInfo = new ColumnInfo("TestColumn", "TEXT", true, null, false);

        // Assert
        Assert.False(columnInfo.IsNullable);
        Assert.True(columnInfo.NotNull);
    }

    [Fact]
    public void ColumnInfo_HasDefault_ReturnsTrueWhenDefaultValueIsNotNull()
    {
        // Arrange & Act
        var columnInfo = new ColumnInfo("TestColumn", "INTEGER", false, 42, false);

        // Assert
        Assert.True(columnInfo.HasDefault);
        Assert.Equal(42, columnInfo.DefaultValue);
    }

    [Fact]
    public void ColumnInfo_HasDefault_ReturnsFalseWhenDefaultValueIsNull()
    {
        // Arrange & Act
        var columnInfo = new ColumnInfo("TestColumn", "INTEGER", false, null, false);

        // Assert
        Assert.False(columnInfo.HasDefault);
        Assert.Null(columnInfo.DefaultValue);
    }

    [Fact]
    public void ColumnInfo_PrimaryKeyColumn_IsSetCorrectly()
    {
        // Arrange & Act
        var primaryKeyColumn = new ColumnInfo("ID", "INTEGER", true, null, true);

        // Assert
        Assert.True(primaryKeyColumn.IsPrimaryKey);
        Assert.Equal("ID", primaryKeyColumn.Name);
        Assert.Equal("INTEGER", primaryKeyColumn.Type);
        Assert.True(primaryKeyColumn.NotNull);
        Assert.False(primaryKeyColumn.IsNullable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("CURRENT_TIMESTAMP")]
    [InlineData("NULL")]
    public void ColumnInfo_WithStringDefaultValues_StoresCorrectly(string defaultValue)
    {
        // Act
        var columnInfo = new ColumnInfo("TestColumn", "TEXT", false, defaultValue, false);

        // Assert
        Assert.Equal(defaultValue, columnInfo.DefaultValue);
        Assert.True(columnInfo.HasDefault);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void ColumnInfo_WithNumericDefaultValues_StoresCorrectly(int defaultValue)
    {
        // Act
        var columnInfo = new ColumnInfo("TestColumn", "INTEGER", false, defaultValue, false);

        // Assert
        Assert.Equal(defaultValue, columnInfo.DefaultValue);
        Assert.True(columnInfo.HasDefault);
    }

    [Fact]
    public void ColumnInfo_RecordEquality_WorksCorrectly()
    {
        // Arrange
        var column1 = new ColumnInfo("Name", "TEXT", true, null, false);
        var column2 = new ColumnInfo("Name", "TEXT", true, null, false);
        var column3 = new ColumnInfo("Name", "TEXT", false, null, false);

        // Assert
        Assert.Equal(column1, column2);
        Assert.NotEqual(column1, column3);
        Assert.True(column1 == column2);
        Assert.False(column1 == column3);
    }

    [Fact]
    public void ColumnInfo_WithStatement_WorksCorrectly()
    {
        // Arrange
        var original = new ColumnInfo("Name", "TEXT", true, null, false);

        // Act
        var modified = original with { NotNull = false, DefaultValue = "test" };

        // Assert
        Assert.Equal("Name", modified.Name);
        Assert.Equal("TEXT", modified.Type);
        Assert.False(modified.NotNull);
        Assert.Equal("test", modified.DefaultValue);
        Assert.False(modified.IsPrimaryKey);
        Assert.True(modified.IsNullable);
        Assert.True(modified.HasDefault);
    }
}