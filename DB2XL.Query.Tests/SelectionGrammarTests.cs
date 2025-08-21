using Xunit;
using DB2XL.Query;
using DB2XL.Core.Models;

namespace DB2XL.Query.Tests;

public class SelectionGrammarTests
{
    [Fact]
    public void SelectionGrammar_All_CreatesValidSelection()
    {
        // Act
        var selection = SelectionGrammar.All("users");
        
        // Assert
        Assert.Equal("users", selection.Table);
        Assert.Single(selection.Select);
        Assert.Equal("*", selection.Select[0]);
        Assert.Null(selection.Where);
        Assert.Empty(selection.OrderBy);
        Assert.Null(selection.Limit);
        Assert.Null(selection.Offset);
    }
    
    [Fact]
    public void SelectionGrammar_Columns_CreatesValidSelection()
    {
        // Act
        var selection = SelectionGrammar.Columns("users", "id", "name", "email");
        
        // Assert
        Assert.Equal("users", selection.Table);
        Assert.Equal(3, selection.Select.Count);
        Assert.Contains("id", selection.Select);
        Assert.Contains("name", selection.Select);
        Assert.Contains("email", selection.Select);
    }
    
    [Fact]
    public void OrderByClause_Asc_CreatesAscendingOrder()
    {
        // Act
        var clause = OrderByClause.Asc("name");
        
        // Assert
        Assert.Equal("name", clause.Column);
        Assert.Equal(SortDirection.Ascending, clause.Direction);
    }
    
    [Fact]
    public void OrderByClause_Desc_CreatesDescendingOrder()
    {
        // Act
        var clause = OrderByClause.Desc("created_at");
        
        // Assert
        Assert.Equal("created_at", clause.Column);
        Assert.Equal(SortDirection.Descending, clause.Direction);
    }
}

public class SelectionBuilderTests
{
    [Fact]
    public void SelectionBuilder_FluentInterface_BuildsCorrectSelection()
    {
        // Act
        var selection = SelectionBuilder
            .From("logs")
            .Select("timestamp", "level", "message")
            .Where(Where.GreaterThan("timestamp", "2025-01-01"))
            .OrderByAsc("timestamp")
            .OrderByDesc("level")
            .Limit(1000)
            .Offset(50)
            .Build();
        
        // Assert
        Assert.Equal("logs", selection.Table);
        Assert.Equal(3, selection.Select.Count);
        Assert.Contains("timestamp", selection.Select);
        Assert.Contains("level", selection.Select);
        Assert.Contains("message", selection.Select);
        Assert.NotNull(selection.Where);
        Assert.Equal(2, selection.OrderBy.Count);
        Assert.Equal("timestamp", selection.OrderBy[0].Column);
        Assert.Equal(SortDirection.Ascending, selection.OrderBy[0].Direction);
        Assert.Equal("level", selection.OrderBy[1].Column);
        Assert.Equal(SortDirection.Descending, selection.OrderBy[1].Direction);
        Assert.Equal(1000, selection.Limit);
        Assert.Equal(50, selection.Offset);
    }
    
    [Fact]
    public void SelectionBuilder_SelectAll_OverridesPreviousSelections()
    {
        // Act
        var selection = SelectionBuilder
            .From("table")
            .Select("col1", "col2")
            .SelectAll()
            .Build();
        
        // Assert
        Assert.Single(selection.Select);
        Assert.Equal("*", selection.Select[0]);
    }
    
    [Fact]
    public void SelectionBuilder_EmptySelect_DefaultsToAll()
    {
        // Act
        var selection = SelectionBuilder
            .From("table")
            .Build();
        
        // Assert
        Assert.Single(selection.Select);
        Assert.Equal("*", selection.Select[0]);
    }
}

public class WhereExpressionTests
{
    [Fact]
    public void ComparisonExpression_Equal_GeneratesCorrectSql()
    {
        // Arrange
        var expr = Where.Equal("status", "active");
        var parameters = new Dictionary<string, object?>();
        
        // Act
        var sql = expr.ToSql(parameters);
        
        // Assert
        Assert.Equal("\"status\" = @param_0", sql);
        Assert.Single(parameters);
        Assert.Equal("active", parameters["param_0"]);
    }
    
    [Fact]
    public void ComparisonExpression_EqualNull_GeneratesIsNull()
    {
        // Arrange
        var expr = Where.Equal("deleted_at", null);
        var parameters = new Dictionary<string, object?>();
        
        // Act
        var sql = expr.ToSql(parameters);
        
        // Assert
        Assert.Equal("\"deleted_at\" IS NULL", sql);
        Assert.Empty(parameters);
    }
    
    [Fact]
    public void ComparisonExpression_In_GeneratesCorrectSql()
    {
        // Arrange
        var expr = Where.In("status", "active", "pending", "completed");
        var parameters = new Dictionary<string, object?>();
        
        // Act
        var sql = expr.ToSql(parameters);
        
        // Assert
        Assert.Equal("\"status\" IN (@param_0_0, @param_0_1, @param_0_2)", sql);
        Assert.Equal(3, parameters.Count);
        Assert.Equal("active", parameters["param_0_0"]);
        Assert.Equal("pending", parameters["param_0_1"]);
        Assert.Equal("completed", parameters["param_0_2"]);
    }
    
    [Fact]
    public void ComparisonExpression_Between_GeneratesCorrectSql()
    {
        // Arrange
        var expr = Where.Between("price", 10.0, 50.0);
        var parameters = new Dictionary<string, object?>();
        
        // Act
        var sql = expr.ToSql(parameters);
        
        // Assert
        Assert.Equal("\"price\" BETWEEN @param_0_start AND @param_0_end", sql);
        Assert.Equal(2, parameters.Count);
        Assert.Equal(10.0, parameters["param_0_start"]);
        Assert.Equal(50.0, parameters["param_0_end"]);
    }
    
    [Fact]
    public void AndExpression_MultipleConditions_GeneratesCorrectSql()
    {
        // Arrange
        var expr = Where.And(
            Where.Equal("status", "active"),
            Where.GreaterThan("price", 100),
            Where.Like("name", "%product%")
        );
        var parameters = new Dictionary<string, object?>();
        
        // Act
        var sql = expr.ToSql(parameters);
        
        // Assert
        Assert.Equal("(\"status\" = @param_0) AND (\"price\" > @param_1) AND (\"name\" LIKE @param_2)", sql);
        Assert.Equal(3, parameters.Count);
        Assert.Equal("active", parameters["param_0"]);
        Assert.Equal(100, parameters["param_1"]);
        Assert.Equal("%product%", parameters["param_2"]);
    }
    
    [Fact]
    public void OrExpression_MultipleConditions_GeneratesCorrectSql()
    {
        // Arrange
        var expr = Where.Or(
            Where.Equal("category", "electronics"),
            Where.Equal("category", "books")
        );
        var parameters = new Dictionary<string, object?>();
        
        // Act
        var sql = expr.ToSql(parameters);
        
        // Assert
        Assert.Equal("(\"category\" = @param_0) OR (\"category\" = @param_1)", sql);
        Assert.Equal(2, parameters.Count);
        Assert.Equal("electronics", parameters["param_0"]);
        Assert.Equal("books", parameters["param_1"]);
    }
    
    [Fact]
    public void NotExpression_NestedCondition_GeneratesCorrectSql()
    {
        // Arrange
        var expr = Where.Not(Where.Equal("deleted", true));
        var parameters = new Dictionary<string, object?>();
        
        // Act
        var sql = expr.ToSql(parameters);
        
        // Assert
        Assert.Equal("NOT (\"deleted\" = @param_0)", sql);
        Assert.Single(parameters);
        Assert.Equal(true, parameters["param_0"]);
    }
    
    [Fact]
    public void ComplexExpression_NestedAndOr_GeneratesCorrectSql()
    {
        // Arrange
        var expr = Where.And(
            Where.Equal("active", true),
            Where.Or(
                Where.Equal("category", "premium"),
                Where.GreaterThan("price", 1000)
            )
        );
        var parameters = new Dictionary<string, object?>();
        
        // Act
        var sql = expr.ToSql(parameters);
        
        // Assert
        Assert.Equal("(\"active\" = @param_0) AND ((\"category\" = @param_1) OR (\"price\" > @param_2))", sql);
        Assert.Equal(3, parameters.Count);
    }
}