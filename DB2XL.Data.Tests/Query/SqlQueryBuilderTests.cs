using DB2XL.Core.Models;
using DB2XL.Core.Enums;
using DB2XL.Data.Query;
using Xunit;

namespace DB2XL.Data.Tests.Query;

/// <summary>
/// Comprehensive tests for SqlQueryBuilder to achieve >60% coverage
/// </summary>
public class SqlQueryBuilderTests
{
    [Theory]
    [InlineData("simple", "\"simple\"")]
    [InlineData("table_name", "\"table_name\"")]
    [InlineData("column123", "\"column123\"")]
    [InlineData("CamelCase", "\"CamelCase\"")]
    public void QuoteIdentifier_WithValidIdentifiers_ReturnsQuotedString(string identifier, string expected)
    {
        // Act
        var result = SqlQueryBuilder.QuoteIdentifier(identifier);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void QuoteIdentifier_WithQuotesInIdentifier_EscapesQuotes()
    {
        // Arrange
        var identifier = "table\"with\"quotes";

        // Act
        var result = SqlQueryBuilder.QuoteIdentifier(identifier);

        // Assert
        Assert.Equal("\"table\"\"with\"\"quotes\"", result);
    }

    [Fact]
    public void QuoteIdentifier_WithEmptyString_ReturnsEmptyQuotes()
    {
        // Act
        var result = SqlQueryBuilder.QuoteIdentifier("");

        // Assert
        Assert.Equal("\"\"", result);
    }

    [Fact]
    public void BuildSelectQuery_WithSingleColumn_ReturnsCorrectSql()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("id", "INTEGER", false, null, true)
        };
        var orderInfo = OrderInfo.ByPrimaryKey(new[] { "id" });

        // Act
        var result = SqlQueryBuilder.BuildSelectQuery("users", columns, orderInfo, true);

        // Assert
        Assert.Equal("SELECT \"id\" FROM \"users\" ORDER BY \"id\" ASC", result);
    }

    [Fact]
    public void BuildSelectQuery_WithMultipleColumns_ReturnsCorrectSql()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("id", "INTEGER", false, null, true),
            new("name", "TEXT", true, null, false),
            new("email", "TEXT", true, null, false)
        };
        var orderInfo = OrderInfo.ByPrimaryKey(new[] { "id" });

        // Act
        var result = SqlQueryBuilder.BuildSelectQuery("users", columns, orderInfo, true);

        // Assert
        Assert.Equal("SELECT \"id\",\"name\",\"email\" FROM \"users\" ORDER BY \"id\" ASC", result);
    }

    [Fact]
    public void BuildSelectQuery_WithoutDeterministicOrder_OmitsOrderBy()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("id", "INTEGER", false, null, true)
        };
        var orderInfo = OrderInfo.ByPrimaryKey(new[] { "id" });

        // Act
        var result = SqlQueryBuilder.BuildSelectQuery("users", columns, orderInfo, false);

        // Assert
        Assert.Equal("SELECT \"id\" FROM \"users\"", result);
    }

    [Fact]
    public void BuildSelectQuery_WithNonDeterministicOrderInfo_OmitsOrderBy()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("id", "INTEGER", false, null, true)
        };
        var orderInfo = OrderInfo.None();

        // Act
        var result = SqlQueryBuilder.BuildSelectQuery("users", columns, orderInfo, true);

        // Assert
        Assert.Equal("SELECT \"id\" FROM \"users\"", result);
    }

    [Fact]
    public void BuildSelectQuery_WithMultipleOrderColumns_ReturnsCorrectOrderBy()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("id", "INTEGER", false, null, true),
            new("name", "TEXT", true, null, false)
        };
        var orderInfo = OrderInfo.ByPrimaryKey(new[] { "id", "name" });

        // Act
        var result = SqlQueryBuilder.BuildSelectQuery("users", columns, orderInfo, true);

        // Assert
        Assert.Equal("SELECT \"id\",\"name\" FROM \"users\" ORDER BY \"id\" ASC,\"name\" ASC", result);
    }

    [Fact]
    public void BuildSelectQuery_WithRowIdOrder_ReturnsCorrectSql()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("name", "TEXT", true, null, false)
        };
        var orderInfo = OrderInfo.ByRowId();

        // Act
        var result = SqlQueryBuilder.BuildSelectQuery("users", columns, orderInfo, true);

        // Assert
        Assert.Equal("SELECT \"name\" FROM \"users\" ORDER BY \"rowid\" ASC", result);
    }

    [Fact]
    public void BuildSelectQuery_WithSpecialCharactersInTableName_QuotesCorrectly()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("id", "INTEGER", false, null, true)
        };
        var orderInfo = OrderInfo.ByPrimaryKey(new[] { "id" });

        // Act
        var result = SqlQueryBuilder.BuildSelectQuery("user data", columns, orderInfo, true);

        // Assert
        Assert.Equal("SELECT \"id\" FROM \"user data\" ORDER BY \"id\" ASC", result);
    }

    [Fact]
    public void BuildSelectQuery_WithSpecialCharactersInColumnName_QuotesCorrectly()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("user id", "INTEGER", false, null, true),
            new("user name", "TEXT", true, null, false)
        };
        var orderInfo = OrderInfo.ByPrimaryKey(new[] { "user id" });

        // Act
        var result = SqlQueryBuilder.BuildSelectQuery("users", columns, orderInfo, true);

        // Assert
        Assert.Equal("SELECT \"user id\",\"user name\" FROM \"users\" ORDER BY \"user id\" ASC", result);
    }

    [Fact]
    public void BuildPaginatedSelectQuery_WithValidParameters_ReturnsCorrectSql()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("id", "INTEGER", false, null, true),
            new("name", "TEXT", true, null, false)
        };
        var orderInfo = OrderInfo.ByPrimaryKey(new[] { "id" });

        // Act
        var result = SqlQueryBuilder.BuildPaginatedSelectQuery("users", columns, orderInfo, 10, 5);

        // Assert
        Assert.Equal("SELECT \"id\",\"name\" FROM \"users\" ORDER BY \"id\" ASC LIMIT 5 OFFSET 10", result);
    }

    [Fact]
    public void BuildPaginatedSelectQuery_WithZeroOffset_ReturnsCorrectSql()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("id", "INTEGER", false, null, true)
        };
        var orderInfo = OrderInfo.ByPrimaryKey(new[] { "id" });

        // Act
        var result = SqlQueryBuilder.BuildPaginatedSelectQuery("users", columns, orderInfo, 0, 10);

        // Assert
        Assert.Equal("SELECT \"id\" FROM \"users\" ORDER BY \"id\" ASC LIMIT 10 OFFSET 0", result);
    }

    [Fact]
    public void BuildPaginatedSelectQuery_WithLargeNumbers_HandlesCorrectly()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("id", "INTEGER", false, null, true)
        };
        var orderInfo = OrderInfo.ByPrimaryKey(new[] { "id" });

        // Act
        var result = SqlQueryBuilder.BuildPaginatedSelectQuery("users", columns, orderInfo, 1000000, 50000);

        // Assert
        Assert.Equal("SELECT \"id\" FROM \"users\" ORDER BY \"id\" ASC LIMIT 50000 OFFSET 1000000", result);
    }

    [Fact]
    public void BuildCountQuery_WithSimpleTableName_ReturnsCorrectSql()
    {
        // Act
        var result = SqlQueryBuilder.BuildCountQuery("users");

        // Assert
        Assert.Equal("SELECT COUNT(*) FROM \"users\"", result);
    }

    [Fact]
    public void BuildCountQuery_WithSpecialCharactersInTableName_QuotesCorrectly()
    {
        // Act
        var result = SqlQueryBuilder.BuildCountQuery("user data");

        // Assert
        Assert.Equal("SELECT COUNT(*) FROM \"user data\"", result);
    }

    [Fact]
    public void BuildCountQuery_WithQuotesInTableName_EscapesQuotes()
    {
        // Act
        var result = SqlQueryBuilder.BuildCountQuery("user\"table");

        // Assert
        Assert.Equal("SELECT COUNT(*) FROM \"user\"\"table\"", result);
    }

    [Fact]
    public void BuildTableExistsQuery_ReturnsParameterizedQuery()
    {
        // Act
        var result = SqlQueryBuilder.BuildTableExistsQuery("users");

        // Assert
        Assert.Equal("SELECT 1 FROM sqlite_master WHERE type IN ('table', 'view') AND name = @tableName LIMIT 1", result);
    }

    [Fact]
    public void BuildTableExistsQuery_IgnoresTableNameParameter()
    {
        // Arrange & Act - The method doesn't actually use the tableName parameter in the query
        var result1 = SqlQueryBuilder.BuildTableExistsQuery("users");
        var result2 = SqlQueryBuilder.BuildTableExistsQuery("different_table");

        // Assert - Both should return the same parameterized query
        Assert.Equal(result1, result2);
    }

    [Fact]
    public void BuildSelectQuery_WithEmptyColumns_ReturnsEmptySelectList()
    {
        // Arrange
        var columns = new List<ColumnInfo>();
        var orderInfo = OrderInfo.None();

        // Act
        var result = SqlQueryBuilder.BuildSelectQuery("users", columns, orderInfo, false);

        // Assert
        Assert.Equal("SELECT  FROM \"users\"", result);
    }

    [Theory]
    [InlineData("simple_table")]
    [InlineData("TABLE_NAME")]
    [InlineData("123numeric")]
    [InlineData("table-with-dashes")]
    [InlineData("table.with.dots")]
    public void BuildCountQuery_WithVariousTableNames_QuotesCorrectly(string tableName)
    {
        // Act
        var result = SqlQueryBuilder.BuildCountQuery(tableName);

        // Assert
        Assert.StartsWith("SELECT COUNT(*) FROM \"", result);
        Assert.EndsWith("\"", result);
        Assert.Contains(tableName, result);
    }

    [Fact]
    public void BuildSelectQuery_WithComplexColumnNames_HandlesCorrectly()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("column\"with\"quotes", "TEXT", true, null, false),
            new("column with spaces", "TEXT", true, null, false),
            new("column.with.dots", "TEXT", true, null, false)
        };
        var orderInfo = OrderInfo.None();

        // Act
        var result = SqlQueryBuilder.BuildSelectQuery("users", columns, orderInfo, false);

        // Assert
        Assert.Contains("\"column\"\"with\"\"quotes\"", result);
        Assert.Contains("\"column with spaces\"", result);
        Assert.Contains("\"column.with.dots\"", result);
    }

    [Fact]
    public void BuildPaginatedSelectQuery_AlwaysUsesDeterministicOrder()
    {
        // Arrange
        var columns = new List<ColumnInfo>
        {
            new("id", "INTEGER", false, null, true)
        };
        var orderInfo = OrderInfo.ByPrimaryKey(new[] { "id" });

        // Act
        var result = SqlQueryBuilder.BuildPaginatedSelectQuery("users", columns, orderInfo, 0, 10);

        // Assert
        Assert.Contains("ORDER BY", result);
    }
}