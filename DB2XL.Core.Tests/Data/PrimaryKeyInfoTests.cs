using DB2XL.Core.Models;

namespace DB2XL.Core.Tests.Data;

public class PrimaryKeyInfoTests
{
    [Fact]
    public void PrimaryKeyInfo_DefaultValues_AreSetCorrectly()
    {
        // Act
        var info = new PrimaryKeyInfo();

        // Assert
        Assert.Equal(PrimaryKeyStrategy.ExplicitPrimaryKey, info.Strategy); // First enum value is default
        Assert.Empty(info.Columns);
        Assert.Equal(string.Empty, info.Description);
        Assert.False(info.IsDeterministic);
        Assert.NotNull(info.Metadata);
        Assert.Empty(info.Metadata);
    }

    [Fact]
    public void PrimaryKeyInfo_InitProperties_CanBeSet()
    {
        // Arrange
        var columns = new[] { "id", "name" };
        var metadata = new Dictionary<string, object>
        {
            ["test"] = "value",
            ["count"] = 42
        };

        // Act
        var info = new PrimaryKeyInfo
        {
            Strategy = PrimaryKeyStrategy.ExplicitPrimaryKey,
            Columns = columns,
            Description = "Test primary key",
            IsDeterministic = true,
            Metadata = metadata
        };

        // Assert
        Assert.Equal(PrimaryKeyStrategy.ExplicitPrimaryKey, info.Strategy);
        Assert.Equal(2, info.Columns.Count);
        Assert.Equal("id", info.Columns[0]);
        Assert.Equal("name", info.Columns[1]);
        Assert.Equal("Test primary key", info.Description);
        Assert.True(info.IsDeterministic);
        Assert.Equal(2, info.Metadata.Count);
        Assert.Equal("value", info.Metadata["test"]);
        Assert.Equal(42, info.Metadata["count"]);
    }

    [Theory]
    [InlineData(PrimaryKeyStrategy.ExplicitPrimaryKey)]
    [InlineData(PrimaryKeyStrategy.UniqueIndex)]
    [InlineData(PrimaryKeyStrategy.ImplicitRowId)]
    [InlineData(PrimaryKeyStrategy.SyntheticHash)]
    [InlineData(PrimaryKeyStrategy.None)]
    public void PrimaryKeyInfo_WithDifferentStrategies_StoresCorrectly(PrimaryKeyStrategy strategy)
    {
        // Act
        var info = new PrimaryKeyInfo { Strategy = strategy };

        // Assert
        Assert.Equal(strategy, info.Strategy);
    }

    [Fact]
    public void PrimaryKeyInfo_RecordEquality_WorksCorrectly()
    {
        // Arrange
        var columns = new[] { "id" };
        var metadata = new Dictionary<string, object> { ["test"] = "value" };
        
        var info1 = new PrimaryKeyInfo
        {
            Strategy = PrimaryKeyStrategy.ExplicitPrimaryKey,
            Columns = columns,
            Description = "Test",
            IsDeterministic = true,
            Metadata = metadata
        };
        
        var info2 = new PrimaryKeyInfo
        {
            Strategy = PrimaryKeyStrategy.ExplicitPrimaryKey,
            Columns = columns,
            Description = "Test",
            IsDeterministic = true,
            Metadata = metadata
        };
        
        var info3 = new PrimaryKeyInfo
        {
            Strategy = PrimaryKeyStrategy.UniqueIndex,
            Columns = columns,
            Description = "Test",
            IsDeterministic = true,
            Metadata = metadata
        };

        // Assert
        Assert.Equal(info1, info2);
        Assert.NotEqual(info1, info3);
    }

    [Fact]
    public void PrimaryKeyInfo_WithStatement_WorksCorrectly()
    {
        // Arrange
        var original = new PrimaryKeyInfo
        {
            Strategy = PrimaryKeyStrategy.ExplicitPrimaryKey,
            Columns = new[] { "id" },
            Description = "Original",
            IsDeterministic = true
        };

        // Act
        var modified = original with 
        { 
            Strategy = PrimaryKeyStrategy.UniqueIndex,
            Description = "Modified" 
        };

        // Assert
        Assert.Equal(PrimaryKeyStrategy.UniqueIndex, modified.Strategy);
        Assert.Equal("Modified", modified.Description);
        Assert.Equal(original.Columns, modified.Columns); // Unchanged
        Assert.True(modified.IsDeterministic); // Unchanged
    }
}