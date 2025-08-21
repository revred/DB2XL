using DB2XL.Core.Models;

namespace DB2XL.Core.Tests.Models;

public class TableInfoTests
{
    [Fact]
    public void TableInfo_BasicProperties_AreSetCorrectly()
    {
        // Arrange
        const string name = "TestTable";
        const string type = "table";

        // Act
        var tableInfo = new TableInfo(name, type);

        // Assert
        Assert.Equal(name, tableInfo.Name);
        Assert.Equal(type, tableInfo.Type);
    }

    [Fact]
    public void TableInfo_IsTable_ReturnsTrueForTableType()
    {
        // Arrange & Act
        var tableInfo = new TableInfo("MyTable", "table");

        // Assert
        Assert.True(tableInfo.IsTable);
        Assert.False(tableInfo.IsView);
    }

    [Fact]
    public void TableInfo_IsView_ReturnsTrueForViewType()
    {
        // Arrange & Act
        var tableInfo = new TableInfo("MyView", "view");

        // Assert
        Assert.True(tableInfo.IsView);
        Assert.False(tableInfo.IsTable);
    }

    [Theory]
    [InlineData("table")]
    [InlineData("TABLE")]
    [InlineData("Table")]
    [InlineData("tAbLe")]
    public void TableInfo_IsTable_CaseInsensitive(string type)
    {
        // Arrange & Act
        var tableInfo = new TableInfo("TestTable", type);

        // Assert
        Assert.True(tableInfo.IsTable);
        Assert.False(tableInfo.IsView);
    }

    [Theory]
    [InlineData("view")]
    [InlineData("VIEW")]
    [InlineData("View")]
    [InlineData("vIeW")]
    public void TableInfo_IsView_CaseInsensitive(string type)
    {
        // Arrange & Act
        var tableInfo = new TableInfo("TestView", type);

        // Assert
        Assert.True(tableInfo.IsView);
        Assert.False(tableInfo.IsTable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("index")]
    [InlineData("trigger")]
    [InlineData("unknown")]
    [InlineData("TABLE_VIEW")] // Not exact match
    public void TableInfo_IsNeitherTableNorView_ForOtherTypes(string type)
    {
        // Arrange & Act
        var tableInfo = new TableInfo("TestObject", type);

        // Assert
        Assert.False(tableInfo.IsTable);
        Assert.False(tableInfo.IsView);
    }

    [Fact]
    public void TableInfo_RecordEquality_WorksCorrectly()
    {
        // Arrange
        var table1 = new TableInfo("TestTable", "table");
        var table2 = new TableInfo("TestTable", "table");
        var table3 = new TableInfo("TestTable", "view");
        var table4 = new TableInfo("OtherTable", "table");

        // Assert
        Assert.Equal(table1, table2);
        Assert.NotEqual(table1, table3);
        Assert.NotEqual(table1, table4);
        Assert.True(table1 == table2);
        Assert.False(table1 == table3);
        Assert.False(table1 == table4);
    }

    [Fact]
    public void TableInfo_WithStatement_WorksCorrectly()
    {
        // Arrange
        var original = new TableInfo("OriginalTable", "table");

        // Act
        var modified = original with { Name = "ModifiedTable", Type = "view" };

        // Assert
        Assert.Equal("ModifiedTable", modified.Name);
        Assert.Equal("view", modified.Type);
        Assert.False(modified.IsTable);
        Assert.True(modified.IsView);
    }

    [Fact]
    public void TableInfo_EmptyName_IsAllowed()
    {
        // Arrange & Act
        var tableInfo = new TableInfo("", "table");

        // Assert
        Assert.Equal("", tableInfo.Name);
        Assert.Equal("table", tableInfo.Type);
        Assert.True(tableInfo.IsTable);
    }

    [Fact]
    public void TableInfo_ComplexTableNames_AreStored()
    {
        // Arrange
        const string complexName = "schema.table_name_with_underscores";

        // Act
        var tableInfo = new TableInfo(complexName, "table");

        // Assert
        Assert.Equal(complexName, tableInfo.Name);
        Assert.True(tableInfo.IsTable);
    }
}