using DB2XL.Core.Models;
using DB2XL.Data.Schema;
using DB2XL.Transform.Interfaces;
using DB2XL.Transform.Examples;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DB2XL.Integration.Tests.Transformers;

/// <summary>
/// Integration tests demonstrating the transformer registry system with real transformers
/// </summary>
public class TransformerRegistryIntegrationTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    [Fact]
    public void Registry_ShouldCreateAndExecuteTextTransformers()
    {
        // Arrange
        var registry = ExampleTransformers.CreateRegistry();
        
        // Verify transformers are registered
        Assert.True(registry.IsRegistered("upper"));
        Assert.True(registry.IsRegistered("trim"));
        Assert.True(registry.IsRegistered("truncate"));
        Assert.True(registry.IsRegistered("coalesce"));
        Assert.True(registry.IsRegistered("email-mask"));

        // Act & Assert - Test each transformer
        var context = new CellContext("test_table", "test_column", 0, SqliteAffinity.Text);

        // Test uppercase transformer
        var upperTransformer = registry.CreateCell("upper", new Dictionary<string, string>());
        Assert.Equal("HELLO WORLD", upperTransformer.Transform(context, "hello world"));
        
        // Test trim transformer
        var trimTransformer = registry.CreateCell("trim", new Dictionary<string, string>());
        Assert.Equal("trimmed", trimTransformer.Transform(context, "  trimmed  "));
        
        // Test truncate transformer
        var truncateConfig = new Dictionary<string, string> { ["maxLength"] = "10", ["ellipsis"] = "..." };
        var truncateTransformer = registry.CreateCell("truncate", truncateConfig);
        Assert.Equal("This is...", truncateTransformer.Transform(context, "This is a very long string"));
        
        // Test coalesce transformer
        var coalesceConfig = new Dictionary<string, string> { ["default"] = "DEFAULT" };
        var coalesceTransformer = registry.CreateCell("coalesce", coalesceConfig);
        Assert.Equal("DEFAULT", coalesceTransformer.Transform(context, null));
        Assert.Equal("DEFAULT", coalesceTransformer.Transform(context, ""));
        Assert.Equal("value", coalesceTransformer.Transform(context, "value"));
    }

    [Fact]
    public void EmailMaskTransformer_ShouldWorkAsColumnTransformer()
    {
        // Arrange
        var registry = ExampleTransformers.CreateRegistry();
        var emailConfig = new Dictionary<string, string> { ["column"] = "email" };
        var transformer = registry.CreateCell("email-mask", emailConfig);

        // Verify it's a column transformer
        Assert.IsAssignableFrom<IColumnTransformer>(transformer);
        var columnTransformer = (IColumnTransformer)transformer;
        Assert.Equal("email", columnTransformer.ColumnName);

        // Test transformation
        var emailContext = new CellContext("users", "email", 0, SqliteAffinity.Text);
        var otherContext = new CellContext("users", "name", 0, SqliteAffinity.Text);

        Assert.True(transformer.CanApply(emailContext));
        Assert.False(transformer.CanApply(otherContext));

        Assert.Equal("j***@example.com", transformer.Transform(emailContext, "john@example.com"));
        Assert.Equal("a***@test.org", transformer.Transform(emailContext, "admin@test.org"));
        Assert.Equal("invalid-email", transformer.Transform(emailContext, "invalid-email"));
    }

    [Fact] 
    public async Task Registry_ShouldWorkWithRealDatabaseScenario()
    {
        // Arrange - Create test database with text data
        var dbPath = CreateTestDatabase();
        _tempFiles.Add(dbPath);

        var registry = ExampleTransformers.CreateRegistry();
        
        // Create transformers
        var upperTransformer = registry.CreateCell("upper", new Dictionary<string, string>());
        var trimTransformer = registry.CreateCell("trim", new Dictionary<string, string>());
        var emailMaskTransformer = registry.CreateCell("email-mask", 
            new Dictionary<string, string> { ["column"] = "email" });

        // Act - Process database with transformers
        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name, email, description FROM users ORDER BY id";
        using var reader = cmd.ExecuteReader();

        var results = new List<Dictionary<string, string?>>();
        int rowIndex = 0;

        while (reader.Read())
        {
            var row = new Dictionary<string, string?>();
            
            // Original values
            var name = reader.GetValue(0)?.ToString();
            var email = reader.GetValue(1)?.ToString();
            var description = reader.GetValue(2)?.ToString();
            
            row["name"] = name;
            row["email"] = email;
            row["description"] = description;

            // Apply transformers
            var nameContext = new CellContext("users", "name", rowIndex, SqliteAffinity.Text);
            var emailContext = new CellContext("users", "email", rowIndex, SqliteAffinity.Text);
            var descContext = new CellContext("users", "description", rowIndex, SqliteAffinity.Text);

            row["name_upper"] = upperTransformer.Transform(nameContext, name);
            row["description_trimmed"] = trimTransformer.Transform(descContext, description);
            
            if (emailMaskTransformer.CanApply(emailContext))
            {
                row["email_masked"] = emailMaskTransformer.Transform(emailContext, email);
            }

            results.Add(row);
            rowIndex++;
        }

        // Assert - Verify transformations worked
        Assert.Equal(3, results.Count);

        var user1 = results[0];
        Assert.Equal("  John Doe  ", user1["name"]);
        Assert.Equal("  JOHN DOE  ", user1["name_upper"]);
        Assert.Equal("john@example.com", user1["email"]);
        Assert.Equal("j***@example.com", user1["email_masked"]);
        Assert.Equal("Software developer", user1["description_trimmed"]);

        var user2 = results[1];
        Assert.Equal("Jane Smith", user2["name"]);
        Assert.Equal("JANE SMITH", user2["name_upper"]);
        Assert.Equal("jane@test.org", user2["email"]);
        Assert.Equal("j***@test.org", user2["email_masked"]);
    }

    [Fact]
    public void Registry_ShouldSupportCustomTransformerChaining()
    {
        // Arrange - Create a custom pipeline transformer
        var registry = new TransformerRegistry();
        
        // Register a pipeline transformer that applies multiple transformations
        registry.Register("pipeline", config =>
        {
            config.TryGetValue("steps", out var stepsValue);
            var steps = (stepsValue ?? "trim,upper").Split(',');
            return new PipelineTransformer(steps, registry, config);
        });

        // Register the individual transformers
        ExampleTransformers.RegisterAll(registry);

        // Act
        var pipelineConfig = new Dictionary<string, string> { ["steps"] = "trim,upper" };
        var transformer = registry.CreateCell("pipeline", pipelineConfig);
        
        var context = new CellContext("table", "column", 0, SqliteAffinity.Text);
        var result = transformer.Transform(context, "  hello world  ");

        // Assert
        Assert.Equal("HELLO WORLD", result);
    }

    [Fact]
    public void RegistryBuilder_ShouldSupportFluentConfiguration()
    {
        // Act
        var registry = new TransformerRegistryBuilder()
            .WithBuiltIns(false)
            .Register("custom1", _ => new MockCellTransformer { TransformResult = "result1" })
            .Register("custom2", _ => new MockCellTransformer { TransformResult = "result2" })
            .RegisterRow("custom-row", _ => new MockRowTransformer())
            .Build();

        // Register examples after building
        ExampleTransformers.RegisterAll(registry);

        // Assert
        Assert.True(registry.IsRegistered("custom1"));
        Assert.True(registry.IsRegistered("custom2"));
        Assert.True(registry.IsRowRegistered("custom-row"));
        Assert.True(registry.IsRegistered("upper"));
        Assert.True(registry.IsRegistered("trim"));

        var transformer1 = registry.CreateCell("custom1", new Dictionary<string, string>());
        Assert.Equal("result1", transformer1.Transform(new CellContext("t", "c", 0, SqliteAffinity.Text), "input"));
    }

    private string CreateTestDatabase()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"registry_test_{Guid.NewGuid():N}.db");
        
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT,
                email TEXT,
                description TEXT
            );

            INSERT INTO users VALUES 
            (1, '  John Doe  ', 'john@example.com', '   Software developer   '),
            (2, 'Jane Smith', 'jane@test.org', 'Project manager'),
            (3, 'Bob Wilson', 'bob@company.com', '  Data analyst  ');
        ";
        cmd.ExecuteNonQuery();
        
        return dbPath;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}

/// <summary>
/// Example transformer that applies multiple transformations in sequence
/// </summary>
public class PipelineTransformer : ICellTransformer
{
    private readonly List<ICellTransformer> _transformers = new();

    public PipelineTransformer(string[] steps, ITransformerRegistry registry, IDictionary<string, string> config)
    {
        foreach (var step in steps)
        {
            var stepName = step.Trim();
            if (!string.IsNullOrEmpty(stepName) && registry.IsRegistered(stepName))
            {
                _transformers.Add(registry.CreateCell(stepName, new Dictionary<string, string>(config)));
            }
        }
    }

    public bool CanApply(CellContext ctx)
    {
        return _transformers.Any(t => t.CanApply(ctx));
    }

    public string? Transform(CellContext ctx, string? raw)
    {
        var result = raw;
        foreach (var transformer in _transformers)
        {
            if (transformer.CanApply(ctx))
            {
                result = transformer.Transform(ctx, result);
            }
        }
        return result;
    }
}