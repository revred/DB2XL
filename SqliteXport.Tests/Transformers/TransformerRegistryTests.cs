using DB2XL.Transformers;
using Xunit;

namespace SqliteXport.Tests.Transformers;

public class TransformerRegistryTests
{
    [Fact]
    public void Register_ShouldAcceptValidCellTransformer()
    {
        // Arrange
        var registry = new TransformerRegistry();
        var factory = (IDictionary<string, string> config) => new MockCellTransformer();

        // Act
        registry.Register("test", factory);

        // Assert
        Assert.True(registry.IsRegistered("test"));
        Assert.Contains("test", registry.GetRegisteredNames());
    }

    [Fact]
    public void RegisterRow_ShouldAcceptValidRowTransformer()
    {
        // Arrange
        var registry = new TransformerRegistry();
        var factory = (IDictionary<string, string> config) => new MockRowTransformer();

        // Act
        registry.RegisterRow("test-row", factory);

        // Assert
        Assert.True(registry.IsRowRegistered("test-row"));
        Assert.Contains("test-row", registry.GetRegisteredRowNames());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_ShouldThrowForInvalidNames(string? name)
    {
        // Arrange
        var registry = new TransformerRegistry();
        var factory = (IDictionary<string, string> config) => new MockCellTransformer();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => registry.Register(name!, factory));
    }

    [Fact]
    public void Register_ShouldThrowForNullFactory()
    {
        // Arrange
        var registry = new TransformerRegistry();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => registry.Register("test", null!));
    }

    [Fact]
    public void CreateCell_ShouldCreateTransformerWithConfiguration()
    {
        // Arrange
        var registry = new TransformerRegistry();
        var testConfig = new Dictionary<string, string> { ["key"] = "value" };
        var capturedConfig = new Dictionary<string, string>();

        registry.Register("configurable", config =>
        {
            foreach (var kvp in config)
                capturedConfig[kvp.Key] = kvp.Value;
            return new MockCellTransformer();
        });

        // Act
        var transformer = registry.CreateCell("configurable", testConfig);

        // Assert
        Assert.NotNull(transformer);
        Assert.Equal("value", capturedConfig["key"]);
    }

    [Fact]
    public void CreateRow_ShouldCreateRowTransformerWithConfiguration()
    {
        // Arrange
        var registry = new TransformerRegistry();
        var testConfig = new Dictionary<string, string> { ["mode"] = "advanced" };
        var capturedConfig = new Dictionary<string, string>();

        registry.RegisterRow("configurable-row", config =>
        {
            foreach (var kvp in config)
                capturedConfig[kvp.Key] = kvp.Value;
            return new MockRowTransformer();
        });

        // Act
        var transformer = registry.CreateRow("configurable-row", testConfig);

        // Assert
        Assert.NotNull(transformer);
        Assert.Equal("advanced", capturedConfig["mode"]);
    }

    [Fact]
    public void CreateCell_ShouldHandleNullConfiguration()
    {
        // Arrange
        var registry = new TransformerRegistry();
        registry.Register("test", config => new MockCellTransformer());

        // Act
        var transformer = registry.CreateCell("test", null);

        // Assert
        Assert.NotNull(transformer);
    }

    [Fact]
    public void CreateCell_ShouldThrowForUnregisteredTransformer()
    {
        // Arrange
        var registry = new TransformerRegistry();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => registry.CreateCell("unknown", new Dictionary<string, string>()));
        Assert.Contains("Cell transformer 'unknown' is not registered", ex.Message);
    }

    [Fact]
    public void CreateRow_ShouldThrowForUnregisteredTransformer()
    {
        // Arrange
        var registry = new TransformerRegistry();

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() => registry.CreateRow("unknown", new Dictionary<string, string>()));
        Assert.Contains("Row transformer 'unknown' is not registered", ex.Message);
    }

    [Fact]
    public void CreateCell_ShouldWrapFactoryExceptions()
    {
        // Arrange
        var registry = new TransformerRegistry();
        registry.Register("faulty", _ => throw new InvalidOperationException("Factory error"));

        // Act & Assert
        var ex = Assert.Throws<TransformerException>(() => registry.CreateCell("faulty", new Dictionary<string, string>()));
        Assert.Equal("faulty", ex.TransformerName);
        Assert.Contains("Failed to create cell transformer 'faulty'", ex.Message);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    public void Register_ShouldBeCaseInsensitive()
    {
        // Arrange
        var registry = new TransformerRegistry();
        registry.Register("TeSt", _ => new MockCellTransformer());

        // Act & Assert
        Assert.True(registry.IsRegistered("test"));
        Assert.True(registry.IsRegistered("TEST"));
        Assert.True(registry.IsRegistered("TeSt"));
        
        var transformer = registry.CreateCell("test", new Dictionary<string, string>());
        Assert.NotNull(transformer);
    }

    [Fact]
    public void Register_ShouldOverwriteExistingRegistration()
    {
        // Arrange
        var registry = new TransformerRegistry();
        var callCount = 0;

        registry.Register("test", _ => { callCount++; return new MockCellTransformer(); });
        registry.Register("test", _ => { callCount += 10; return new MockCellTransformer(); });

        // Act
        registry.CreateCell("test", new Dictionary<string, string>());

        // Assert
        Assert.Equal(10, callCount); // Second factory was used
    }

    [Fact]
    public void Unregister_ShouldRemoveTransformer()
    {
        // Arrange
        var registry = new TransformerRegistry();
        registry.Register("test", _ => new MockCellTransformer());
        Assert.True(registry.IsRegistered("test"));

        // Act
        var removed = registry.Unregister("test");

        // Assert
        Assert.True(removed);
        Assert.False(registry.IsRegistered("test"));
        Assert.DoesNotContain("test", registry.GetRegisteredNames());
    }

    [Fact]
    public void UnregisterRow_ShouldRemoveRowTransformer()
    {
        // Arrange
        var registry = new TransformerRegistry();
        registry.RegisterRow("test-row", _ => new MockRowTransformer());
        Assert.True(registry.IsRowRegistered("test-row"));

        // Act
        var removed = registry.UnregisterRow("test-row");

        // Assert
        Assert.True(removed);
        Assert.False(registry.IsRowRegistered("test-row"));
        Assert.DoesNotContain("test-row", registry.GetRegisteredRowNames());
    }

    [Fact]
    public void Clear_ShouldRemoveAllTransformers()
    {
        // Arrange
        var registry = new TransformerRegistry();
        registry.Register("cell1", _ => new MockCellTransformer());
        registry.Register("cell2", _ => new MockCellTransformer());
        registry.RegisterRow("row1", _ => new MockRowTransformer());
        
        Assert.Equal(3, registry.Count);

        // Act
        registry.Clear();

        // Assert
        Assert.Equal(0, registry.Count);
        Assert.Empty(registry.GetRegisteredNames());
        Assert.Empty(registry.GetRegisteredRowNames());
    }

    [Fact]
    public void Registry_ShouldBeThreadSafe()
    {
        // Arrange
        var registry = new TransformerRegistry();
        const int threadCount = 10;
        const int operationsPerThread = 100;
        var tasks = new Task[threadCount];

        // Act - Register transformers from multiple threads
        for (int t = 0; t < threadCount; t++)
        {
            var threadIndex = t;
            tasks[threadIndex] = Task.Run(() =>
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    var name = $"transformer_{threadIndex}_{i}";
                    registry.Register(name, _ => new MockCellTransformer());
                    
                    // Immediately try to create it
                    var transformer = registry.CreateCell(name, new Dictionary<string, string>());
                    Assert.NotNull(transformer);
                }
            });
        }

        Task.WaitAll(tasks);

        // Assert - All registrations should be successful
        Assert.Equal(threadCount * operationsPerThread, registry.GetRegisteredNames().Count);
    }

    [Fact]
    public void GetRegisteredNames_ShouldReturnReadOnlyCollection()
    {
        // Arrange
        var registry = new TransformerRegistry();
        registry.Register("test", _ => new MockCellTransformer());

        // Act
        var names = registry.GetRegisteredNames();

        // Assert
        Assert.IsAssignableFrom<IReadOnlyCollection<string>>(names);
        Assert.Contains("test", names);
    }
}

public class TransformerRegistryBuilderTests
{
    [Fact]
    public void CreateDefault_ShouldReturnRegistryWithBuiltIns()
    {
        // Act
        var registry = TransformerRegistryBuilder.CreateDefault();

        // Assert
        Assert.NotNull(registry);
        // Note: When built-in transformers are implemented, verify they're registered here
    }

    [Fact]
    public void CreateEmpty_ShouldReturnEmptyRegistry()
    {
        // Act
        var registry = TransformerRegistryBuilder.CreateEmpty();

        // Assert
        Assert.NotNull(registry);
        Assert.Empty(registry.GetRegisteredNames());
        Assert.Empty(registry.GetRegisteredRowNames());
    }

    [Fact]
    public void Builder_ShouldSupportMethodChaining()
    {
        // Act
        var registry = new TransformerRegistryBuilder()
            .WithBuiltIns(false)
            .Register("test1", _ => new MockCellTransformer())
            .RegisterRow("test-row", _ => new MockRowTransformer())
            .Register("test2", _ => new MockCellTransformer())
            .Build();

        // Assert
        Assert.True(registry.IsRegistered("test1"));
        Assert.True(registry.IsRegistered("test2"));
        Assert.True(registry.IsRowRegistered("test-row"));
    }

    [Fact]
    public void RegisterSimple_ShouldCreateParameterlessTransformer()
    {
        // Act
        var registry = new TransformerRegistryBuilder()
            .WithBuiltIns(false)
            .Register<SimpleMockTransformer>("simple")
            .Build();

        // Assert
        Assert.True(registry.IsRegistered("simple"));
        var transformer = registry.CreateCell("simple", new Dictionary<string, string>());
        Assert.IsType<SimpleMockTransformer>(transformer);
    }

    [Fact]
    public void RegisterConfigurable_ShouldCreateConfigurableTransformer()
    {
        // Act
        var registry = new TransformerRegistryBuilder()
            .WithBuiltIns(false)
            .RegisterConfigurable<ConfigurableMockTransformer>("configurable")
            .Build();

        // Assert
        Assert.True(registry.IsRegistered("configurable"));
        var config = new Dictionary<string, string> { ["test"] = "value" };
        var transformer = registry.CreateCell("configurable", config);
        Assert.IsType<ConfigurableMockTransformer>(transformer);
    }
}

// Test helper classes
public class SimpleMockTransformer : ICellTransformer
{
    public bool CanApply(CellContext ctx) => true;
    public string? Transform(CellContext ctx, string? raw) => $"simple:{raw}";
}

public class ConfigurableMockTransformer : CellTransformerBase
{
    public ConfigurableMockTransformer(IDictionary<string, string> configuration) : base(configuration) { }
    
    public override string? Transform(CellContext ctx, string? raw) => $"config:{GetConfig("test")}:{raw}";
}