using DB2XL.Core.Models;
using DB2XL.Data.Schema;
using DB2XL.Transform.Interfaces;
using Xunit;

namespace DB2XL.Integration.Tests.Transformers;

public class TransformerInterfacesTests
{
    [Theory]
    [InlineData("users", "email", 0, SqliteAffinity.Text)]
    [InlineData("events", "timestamp", 999, SqliteAffinity.Integer)]
    [InlineData("data", "payload", 42, SqliteAffinity.Blob)]
    public void CellContext_ShouldConstructCorrectly(string table, string column, int rowIndex, SqliteAffinity affinity)
    {
        // Act
        var context = new CellContext(table, column, rowIndex, affinity);
        
        // Assert
        Assert.Equal(table, context.Table);
        Assert.Equal(column, context.Column);
        Assert.Equal(rowIndex, context.RowIndex);
        Assert.Equal(affinity, context.Affinity);
    }

    [Theory]
    [InlineData("orders", 0)]
    [InlineData("products", 1000)]
    [InlineData("customers", 42)]
    public void RowContext_ShouldConstructCorrectly(string table, int rowIndex)
    {
        // Act
        var context = new RowContext(table, rowIndex);
        
        // Assert
        Assert.Equal(table, context.Table);
        Assert.Equal(rowIndex, context.RowIndex);
    }

    [Fact]
    public void CellContext_ShouldSupportValueEquality()
    {
        // Arrange
        var context1 = new CellContext("table1", "col1", 5, SqliteAffinity.Text);
        var context2 = new CellContext("table1", "col1", 5, SqliteAffinity.Text);
        var context3 = new CellContext("table1", "col1", 6, SqliteAffinity.Text);
        
        // Assert
        Assert.Equal(context1, context2);
        Assert.NotEqual(context1, context3);
        Assert.Equal(context1.GetHashCode(), context2.GetHashCode());
    }

    [Fact]
    public void RowContext_ShouldSupportValueEquality()
    {
        // Arrange
        var context1 = new RowContext("table1", 5);
        var context2 = new RowContext("table1", 5);
        var context3 = new RowContext("table2", 5);
        
        // Assert
        Assert.Equal(context1, context2);
        Assert.NotEqual(context1, context3);
        Assert.Equal(context1.GetHashCode(), context2.GetHashCode());
    }
}

public class TransformerExceptionTests
{
    [Fact]
    public void TransformerException_ShouldConstructWithTransformerName()
    {
        // Act
        var ex = new TransformerException("test-transformer", "Test message");
        
        // Assert
        Assert.Equal("test-transformer", ex.TransformerName);
        Assert.Equal("Test message", ex.Message);
        Assert.Null(ex.CellContext);
    }

    [Fact]
    public void TransformerException_ShouldConstructWithInnerException()
    {
        // Arrange
        var inner = new InvalidOperationException("Inner error");
        
        // Act
        var ex = new TransformerException("test-transformer", "Test message", inner);
        
        // Assert
        Assert.Equal("test-transformer", ex.TransformerName);
        Assert.Equal("Test message", ex.Message);
        Assert.Equal(inner, ex.InnerException);
    }

    [Fact]
    public void TransformerException_ShouldConstructWithCellContext()
    {
        // Arrange
        var context = new CellContext("table1", "col1", 5, SqliteAffinity.Text);
        
        // Act
        var ex = new TransformerException("test-transformer", context, "Test message");
        
        // Assert
        Assert.Equal("test-transformer", ex.TransformerName);
        Assert.Equal("Test message", ex.Message);
        Assert.Equal(context, ex.CellContext);
    }

    [Fact]
    public void TransformerException_ShouldConstructWithCellContextAndInnerException()
    {
        // Arrange
        var context = new CellContext("table1", "col1", 5, SqliteAffinity.Text);
        var inner = new ArgumentException("Inner error");
        
        // Act
        var ex = new TransformerException("test-transformer", context, "Test message", inner);
        
        // Assert
        Assert.Equal("test-transformer", ex.TransformerName);
        Assert.Equal("Test message", ex.Message);
        Assert.Equal(context, ex.CellContext);
        Assert.Equal(inner, ex.InnerException);
    }
}

public class CellTransformerBaseTests
{
    private class TestCellTransformer : CellTransformerBase
    {
        public TestCellTransformer(IDictionary<string, string> configuration) : base(configuration) { }
        
        public override string? Transform(CellContext ctx, string? raw)
        {
            return $"transformed:{raw}:{GetConfig("mode", "default")}";
        }
        
        // Expose protected methods for testing
        public new string GetConfig(string key, string defaultValue = "") => base.GetConfig(key, defaultValue);
        public new bool GetConfigBool(string key, bool defaultValue = false) => base.GetConfigBool(key, defaultValue);
        public new int GetConfigInt(string key, int defaultValue = 0) => base.GetConfigInt(key, defaultValue);
    }

    [Fact]
    public void CellTransformerBase_ShouldHandleNullConfiguration()
    {
        // Act
        var transformer = new TestCellTransformer(null);
        
        // Assert - Should not throw
        var result = transformer.GetConfig("missing", "default");
        Assert.Equal("default", result);
    }

    [Fact]
    public void CellTransformerBase_ShouldHandleEmptyConfiguration()
    {
        // Arrange
        var config = new Dictionary<string, string>();
        var transformer = new TestCellTransformer(config);
        
        // Act & Assert
        Assert.Equal("default", transformer.GetConfig("missing", "default"));
        Assert.Equal("", transformer.GetConfig("missing"));
    }

    [Fact]
    public void CellTransformerBase_GetConfig_ShouldReturnConfiguredValues()
    {
        // Arrange
        var config = new Dictionary<string, string>
        {
            ["mode"] = "advanced",
            ["prefix"] = "test_"
        };
        var transformer = new TestCellTransformer(config);
        
        // Act & Assert
        Assert.Equal("advanced", transformer.GetConfig("mode"));
        Assert.Equal("test_", transformer.GetConfig("prefix"));
        Assert.Equal("default", transformer.GetConfig("missing", "default"));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("True", true)]
    [InlineData("FALSE", false)]
    [InlineData("invalid", false)]
    [InlineData("", false)]
    public void CellTransformerBase_GetConfigBool_ShouldParseBooleanValues(string value, bool expected)
    {
        // Arrange
        var config = new Dictionary<string, string> { ["flag"] = value };
        var transformer = new TestCellTransformer(config);
        
        // Act & Assert
        Assert.Equal(expected, transformer.GetConfigBool("flag"));
    }

    [Fact]
    public void CellTransformerBase_GetConfigBool_ShouldReturnDefaultForMissing()
    {
        // Arrange
        var transformer = new TestCellTransformer(new Dictionary<string, string>());
        
        // Act & Assert
        Assert.False(transformer.GetConfigBool("missing"));
        Assert.True(transformer.GetConfigBool("missing", true));
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("-10", -10)]
    [InlineData("0", 0)]
    [InlineData("invalid", 0)]
    [InlineData("", 0)]
    public void CellTransformerBase_GetConfigInt_ShouldParseIntegerValues(string value, int expected)
    {
        // Arrange
        var config = new Dictionary<string, string> { ["number"] = value };
        var transformer = new TestCellTransformer(config);
        
        // Act & Assert
        Assert.Equal(expected, transformer.GetConfigInt("number"));
    }

    [Fact]
    public void CellTransformerBase_GetConfigInt_ShouldReturnDefaultForMissing()
    {
        // Arrange
        var transformer = new TestCellTransformer(new Dictionary<string, string>());
        
        // Act & Assert
        Assert.Equal(0, transformer.GetConfigInt("missing"));
        Assert.Equal(999, transformer.GetConfigInt("missing", 999));
    }

    [Fact]
    public void CellTransformerBase_CanApply_ShouldReturnTrueByDefault()
    {
        // Arrange
        var transformer = new TestCellTransformer(new Dictionary<string, string>());
        var context = new CellContext("table", "col", 0, SqliteAffinity.Text);
        
        // Act & Assert
        Assert.True(transformer.CanApply(context));
    }

    [Fact]
    public void CellTransformerBase_Transform_ShouldUseConfiguration()
    {
        // Arrange
        var config = new Dictionary<string, string> { ["mode"] = "test" };
        var transformer = new TestCellTransformer(config);
        var context = new CellContext("table", "col", 0, SqliteAffinity.Text);
        
        // Act
        var result = transformer.Transform(context, "input");
        
        // Assert
        Assert.Equal("transformed:input:test", result);
    }

    [Fact]
    public void CellTransformerBase_Transform_ShouldHandleNullInput()
    {
        // Arrange
        var transformer = new TestCellTransformer(new Dictionary<string, string>());
        var context = new CellContext("table", "col", 0, SqliteAffinity.Text);
        
        // Act
        var result = transformer.Transform(context, null);
        
        // Assert
        Assert.Equal("transformed::default", result);
    }
}

// Mock implementations for testing interfaces
public class MockCellTransformer : ICellTransformer
{
    public bool CanApplyResult { get; set; } = true;
    public string? TransformResult { get; set; } = "mock_result";
    public List<(CellContext Context, string? Raw)> TransformCalls { get; } = new();
    public List<CellContext> CanApplyCalls { get; } = new();

    public bool CanApply(CellContext ctx)
    {
        CanApplyCalls.Add(ctx);
        return CanApplyResult;
    }

    public string? Transform(CellContext ctx, string? raw)
    {
        TransformCalls.Add((ctx, raw));
        return TransformResult;
    }
}

public class MockRowTransformer : IRowTransformer
{
    public bool CanApplyResult { get; set; } = true;
    public IReadOnlyDictionary<string, string?> TransformResult { get; set; } = new Dictionary<string, string?>();
    public List<(RowContext Context, IReadOnlyDictionary<string, string?> RawRow)> TransformCalls { get; } = new();
    public List<RowContext> CanApplyCalls { get; } = new();

    public bool CanApply(RowContext ctx)
    {
        CanApplyCalls.Add(ctx);
        return CanApplyResult;
    }

    public IReadOnlyDictionary<string, string?> Transform(RowContext ctx, IReadOnlyDictionary<string, string?> rawRow)
    {
        TransformCalls.Add((ctx, rawRow));
        return TransformResult;
    }
}

public class MockColumnTransformer : IColumnTransformer
{
    public string ColumnName { get; set; } = "test_column";
    public bool CanApplyResult { get; set; } = true;
    public string? TransformResult { get; set; } = "mock_column_result";
    public List<(CellContext Context, string? Raw)> TransformCalls { get; } = new();
    public List<CellContext> CanApplyCalls { get; } = new();

    public bool CanApply(CellContext ctx)
    {
        CanApplyCalls.Add(ctx);
        return CanApplyResult;
    }

    public string? Transform(CellContext ctx, string? raw)
    {
        TransformCalls.Add((ctx, raw));
        return TransformResult;
    }
}

public class InterfaceContractTests
{
    [Fact]
    public void ICellTransformer_ShouldFollowExpectedContract()
    {
        // Arrange
        var transformer = new MockCellTransformer
        {
            CanApplyResult = true,
            TransformResult = "expected_result"
        };
        var context = new CellContext("test_table", "test_col", 5, SqliteAffinity.Text);
        
        // Act
        var canApply = transformer.CanApply(context);
        var result = transformer.Transform(context, "input_data");
        
        // Assert
        Assert.True(canApply);
        Assert.Equal("expected_result", result);
        Assert.Single(transformer.CanApplyCalls);
        Assert.Single(transformer.TransformCalls);
        Assert.Equal(context, transformer.CanApplyCalls[0]);
        Assert.Equal((context, "input_data"), transformer.TransformCalls[0]);
    }

    [Fact]
    public void IRowTransformer_ShouldFollowExpectedContract()
    {
        // Arrange
        var expectedResult = new Dictionary<string, string?> { ["new_col"] = "new_value" };
        var transformer = new MockRowTransformer
        {
            CanApplyResult = true,
            TransformResult = expectedResult
        };
        var context = new RowContext("test_table", 10);
        var inputRow = new Dictionary<string, string?> { ["existing_col"] = "existing_value" };
        
        // Act
        var canApply = transformer.CanApply(context);
        var result = transformer.Transform(context, inputRow);
        
        // Assert
        Assert.True(canApply);
        Assert.Equal(expectedResult, result);
        Assert.Single(transformer.CanApplyCalls);
        Assert.Single(transformer.TransformCalls);
        Assert.Equal(context, transformer.CanApplyCalls[0]);
        Assert.Equal((context, inputRow), transformer.TransformCalls[0]);
    }

    [Fact]
    public void IColumnTransformer_ShouldExtendCellTransformer()
    {
        // Arrange
        var transformer = new MockColumnTransformer
        {
            ColumnName = "email",
            CanApplyResult = true,
            TransformResult = "masked_email"
        };
        var context = new CellContext("users", "email", 3, SqliteAffinity.Text);
        
        // Act
        var columnName = transformer.ColumnName;
        var canApply = transformer.CanApply(context);
        var result = transformer.Transform(context, "user@example.com");
        
        // Assert
        Assert.Equal("email", columnName);
        Assert.True(canApply);
        Assert.Equal("masked_email", result);
        
        // Verify it's also a valid ICellTransformer
        ICellTransformer cellTransformer = transformer;
        Assert.True(cellTransformer.CanApply(context));
        Assert.Equal("masked_email", cellTransformer.Transform(context, "user@example.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("valid_input")]
    [InlineData("special!@#$%^&*()characters")]
    public void ICellTransformer_ShouldHandleVariousInputs(string? input)
    {
        // Arrange
        var transformer = new MockCellTransformer
        {
            TransformResult = $"transformed_{input ?? "null"}"
        };
        var context = new CellContext("table", "col", 0, SqliteAffinity.Text);
        
        // Act
        var result = transformer.Transform(context, input);
        
        // Assert
        Assert.Equal($"transformed_{input ?? "null"}", result);
        Assert.Equal(input, transformer.TransformCalls[0].Raw);
    }

    [Fact]
    public void Transformers_ShouldBeStateless()
    {
        // Arrange
        var transformer = new MockCellTransformer();
        var context1 = new CellContext("table1", "col1", 1, SqliteAffinity.Text);
        var context2 = new CellContext("table2", "col2", 2, SqliteAffinity.Integer);
        
        // Act - Call transform multiple times
        transformer.Transform(context1, "input1");
        transformer.Transform(context2, "input2");
        transformer.Transform(context1, "input3");
        
        // Assert - Should maintain call history but no internal state affecting results
        Assert.Equal(3, transformer.TransformCalls.Count);
        Assert.Equal("input1", transformer.TransformCalls[0].Raw);
        Assert.Equal("input2", transformer.TransformCalls[1].Raw);
        Assert.Equal("input3", transformer.TransformCalls[2].Raw);
        
        // Each call should be independent
        Assert.Equal(context1, transformer.TransformCalls[0].Context);
        Assert.Equal(context2, transformer.TransformCalls[1].Context);
        Assert.Equal(context1, transformer.TransformCalls[2].Context);
    }
}