using DB2XL.Core.Models;

namespace DB2XL.Core.Tests.Data;

public class IndexInfoTests
{
    [Fact]
    public void IndexInfo_DefaultValues_AreSetCorrectly()
    {
        // Act
        var info = new IndexInfo();

        // Assert
        Assert.Equal(string.Empty, info.Name);
        Assert.Equal(string.Empty, info.TableName);
        Assert.False(info.IsUnique);
        Assert.Empty(info.Columns);
        Assert.Null(info.WhereClause);
    }

    [Fact]
    public void IndexInfo_InitProperties_CanBeSet()
    {
        // Arrange
        var columns = new[] { "id", "name" };
        const string whereClause = "name IS NOT NULL";

        // Act
        var info = new IndexInfo
        {
            Name = "idx_test",
            TableName = "test_table",
            IsUnique = true,
            Columns = columns,
            WhereClause = whereClause
        };

        // Assert
        Assert.Equal("idx_test", info.Name);
        Assert.Equal("test_table", info.TableName);
        Assert.True(info.IsUnique);
        Assert.Equal(2, info.Columns.Count);
        Assert.Equal("id", info.Columns[0]);
        Assert.Equal("name", info.Columns[1]);
        Assert.Equal(whereClause, info.WhereClause);
    }

    [Fact]
    public void IndexInfo_UniqueIndex_IsStoredCorrectly()
    {
        // Act
        var uniqueIndex = new IndexInfo { IsUnique = true };
        var regularIndex = new IndexInfo { IsUnique = false };

        // Assert
        Assert.True(uniqueIndex.IsUnique);
        Assert.False(regularIndex.IsUnique);
    }

    [Fact]
    public void IndexInfo_WithSingleColumn_WorksCorrectly()
    {
        // Act
        var info = new IndexInfo
        {
            Name = "idx_single",
            Columns = new[] { "email" }
        };

        // Assert
        Assert.Single(info.Columns);
        Assert.Equal("email", info.Columns[0]);
    }

    [Fact]
    public void IndexInfo_WithMultipleColumns_WorksCorrectly()
    {
        // Act
        var info = new IndexInfo
        {
            Name = "idx_composite",
            Columns = new[] { "last_name", "first_name", "middle_initial" }
        };

        // Assert
        Assert.Equal(3, info.Columns.Count);
        Assert.Equal("last_name", info.Columns[0]);
        Assert.Equal("first_name", info.Columns[1]);
        Assert.Equal("middle_initial", info.Columns[2]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("id > 0")]
    [InlineData("name IS NOT NULL")]
    [InlineData("status = 'active' AND deleted_at IS NULL")]
    public void IndexInfo_WithDifferentWhereClauses_StoresCorrectly(string? whereClause)
    {
        // Act
        var info = new IndexInfo { WhereClause = whereClause };

        // Assert
        Assert.Equal(whereClause, info.WhereClause);
    }

    [Fact]
    public void IndexInfo_RecordEquality_WorksCorrectly()
    {
        // Arrange
        var columns = new[] { "id" };
        
        var info1 = new IndexInfo
        {
            Name = "idx_test",
            TableName = "test_table",
            IsUnique = true,
            Columns = columns,
            WhereClause = "id > 0"
        };
        
        var info2 = new IndexInfo
        {
            Name = "idx_test",
            TableName = "test_table",
            IsUnique = true,
            Columns = columns,
            WhereClause = "id > 0"
        };
        
        var info3 = new IndexInfo
        {
            Name = "idx_different",
            TableName = "test_table",
            IsUnique = true,
            Columns = columns,
            WhereClause = "id > 0"
        };

        // Assert
        Assert.Equal(info1, info2);
        Assert.NotEqual(info1, info3);
    }

    [Fact]
    public void IndexInfo_WithStatement_WorksCorrectly()
    {
        // Arrange
        var original = new IndexInfo
        {
            Name = "idx_original",
            TableName = "original_table",
            IsUnique = false,
            Columns = new[] { "id" },
            WhereClause = null
        };

        // Act
        var modified = original with 
        { 
            Name = "idx_modified",
            IsUnique = true,
            WhereClause = "id > 0"
        };

        // Assert
        Assert.Equal("idx_modified", modified.Name);
        Assert.True(modified.IsUnique);
        Assert.Equal("id > 0", modified.WhereClause);
        Assert.Equal(original.TableName, modified.TableName); // Unchanged
        Assert.Equal(original.Columns, modified.Columns); // Unchanged
    }

    [Fact]
    public void IndexInfo_EmptyColumnsList_IsAllowed()
    {
        // Act
        var info = new IndexInfo
        {
            Name = "empty_index",
            Columns = Array.Empty<string>()
        };

        // Assert
        Assert.Empty(info.Columns);
        Assert.Equal("empty_index", info.Name);
    }
}