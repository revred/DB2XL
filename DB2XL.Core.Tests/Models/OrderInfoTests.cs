using DB2XL.Core.Enums;
using DB2XL.Core.Models;

namespace DB2XL.Core.Tests.Models;

public class OrderInfoTests
{
    [Fact]
    public void OrderInfo_BasicProperties_AreSetCorrectly()
    {
        // Arrange
        var mode = OrderMode.PrimaryKey;
        var columns = new[] { "ID", "Name" };

        // Act
        var orderInfo = new OrderInfo(mode, columns);

        // Assert
        Assert.Equal(mode, orderInfo.Mode);
        Assert.Equal(2, orderInfo.Columns.Count);
        Assert.Equal("ID", orderInfo.Columns[0]);
        Assert.Equal("Name", orderInfo.Columns[1]);
    }

    [Fact]
    public void OrderInfo_IsDeterministic_ReturnsTrueForPrimaryKey()
    {
        // Arrange & Act
        var orderInfo = new OrderInfo(OrderMode.PrimaryKey, new[] { "ID" });

        // Assert
        Assert.True(orderInfo.IsDeterministic);
    }

    [Fact]
    public void OrderInfo_IsDeterministic_ReturnsTrueForRowId()
    {
        // Arrange & Act
        var orderInfo = new OrderInfo(OrderMode.Rowid, new[] { "rowid" });

        // Assert
        Assert.True(orderInfo.IsDeterministic);
    }

    [Fact]
    public void OrderInfo_IsDeterministic_ReturnsFalseForNone()
    {
        // Arrange & Act
        var orderInfo = new OrderInfo(OrderMode.None, Array.Empty<string>());

        // Assert
        Assert.False(orderInfo.IsDeterministic);
    }

    [Fact]
    public void OrderInfo_None_CreatesCorrectInstance()
    {
        // Act
        var orderInfo = OrderInfo.None();

        // Assert
        Assert.Equal(OrderMode.None, orderInfo.Mode);
        Assert.Empty(orderInfo.Columns);
        Assert.False(orderInfo.IsDeterministic);
    }

    [Fact]
    public void OrderInfo_ByRowId_CreatesCorrectInstance()
    {
        // Act
        var orderInfo = OrderInfo.ByRowId();

        // Assert
        Assert.Equal(OrderMode.Rowid, orderInfo.Mode);
        Assert.Single(orderInfo.Columns);
        Assert.Equal("rowid", orderInfo.Columns[0]);
        Assert.True(orderInfo.IsDeterministic);
    }

    [Fact]
    public void OrderInfo_ByPrimaryKey_CreatesCorrectInstance()
    {
        // Arrange
        var pkColumns = new[] { "CustomerID", "OrderID" };

        // Act
        var orderInfo = OrderInfo.ByPrimaryKey(pkColumns);

        // Assert
        Assert.Equal(OrderMode.PrimaryKey, orderInfo.Mode);
        Assert.Equal(2, orderInfo.Columns.Count);
        Assert.Equal("CustomerID", orderInfo.Columns[0]);
        Assert.Equal("OrderID", orderInfo.Columns[1]);
        Assert.True(orderInfo.IsDeterministic);
    }

    [Fact]
    public void OrderInfo_ByPrimaryKey_WithEmptyColumns_CreatesCorrectInstance()
    {
        // Arrange
        var pkColumns = Array.Empty<string>();

        // Act
        var orderInfo = OrderInfo.ByPrimaryKey(pkColumns);

        // Assert
        Assert.Equal(OrderMode.PrimaryKey, orderInfo.Mode);
        Assert.Empty(orderInfo.Columns);
        Assert.True(orderInfo.IsDeterministic);
    }

    [Fact]
    public void OrderInfo_ByPrimaryKey_WithSingleColumn_CreatesCorrectInstance()
    {
        // Arrange
        var pkColumns = new[] { "ID" };

        // Act
        var orderInfo = OrderInfo.ByPrimaryKey(pkColumns);

        // Assert
        Assert.Equal(OrderMode.PrimaryKey, orderInfo.Mode);
        Assert.Single(orderInfo.Columns);
        Assert.Equal("ID", orderInfo.Columns[0]);
        Assert.True(orderInfo.IsDeterministic);
    }

    [Fact]
    public void OrderInfo_RecordEquality_WorksCorrectly()
    {
        // Arrange
        var columns = new[] { "ID", "Name" };
        var order1 = new OrderInfo(OrderMode.PrimaryKey, columns);
        var order2 = new OrderInfo(OrderMode.PrimaryKey, columns);
        var order3 = new OrderInfo(OrderMode.Rowid, new[] { "rowid" });

        // Assert
        Assert.Equal(order1, order2);
        Assert.NotEqual(order1, order3);
        Assert.True(order1 == order2);
        Assert.False(order1 == order3);
    }

    [Fact]
    public void OrderInfo_WithStatement_WorksCorrectly()
    {
        // Arrange
        var original = OrderInfo.ByPrimaryKey(new[] { "ID" });

        // Act
        var modified = original with { Mode = OrderMode.Rowid, Columns = new[] { "rowid" } };

        // Assert
        Assert.Equal(OrderMode.Rowid, modified.Mode);
        Assert.Single(modified.Columns);
        Assert.Equal("rowid", modified.Columns[0]);
        Assert.True(modified.IsDeterministic);
    }

    [Theory]
    [InlineData(OrderMode.None, false)]
    [InlineData(OrderMode.Rowid, true)]
    [InlineData(OrderMode.PrimaryKey, true)]
    public void OrderInfo_IsDeterministic_ReturnsCorrectValueForMode(OrderMode mode, bool expectedDeterministic)
    {
        // Arrange & Act
        var orderInfo = new OrderInfo(mode, Array.Empty<string>());

        // Assert
        Assert.Equal(expectedDeterministic, orderInfo.IsDeterministic);
    }
}