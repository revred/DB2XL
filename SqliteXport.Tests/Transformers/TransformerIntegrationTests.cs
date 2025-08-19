using DB2XL.Transformers;
using Microsoft.Data.Sqlite;
using Xunit;
using System.Text;
using System.Collections.Concurrent;

namespace SqliteXport.Tests.Transformers;

/// <summary>
/// Integration tests that demonstrate transformer interfaces working with real database scenarios
/// </summary>
public class TransformerIntegrationTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    [Fact]
    public async Task TransformerInterfaces_ShouldWorkWithRealDatabaseScenarios()
    {
        // Arrange - Create a realistic database scenario
        var dbPath = CreateTestDatabase();
        _tempFiles.Add(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        await connection.OpenAsync();

        // Create transformer instances
        var epochTransformer = new MockEpochTransformer();
        var jsonTransformer = new MockJsonTransformer();
        var emailMaskTransformer = new MockEmailMaskTransformer();

        // Act - Simulate processing database rows through transformers
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT user_id, email, created_at, profile_data FROM users ORDER BY user_id";
        using var reader = cmd.ExecuteReader();

        var transformedRows = new List<Dictionary<string, string?>>();
        int rowIndex = 0;

        while (reader.Read())
        {
            var originalRow = new Dictionary<string, string?>
            {
                ["user_id"] = reader.GetValue(0)?.ToString(),
                ["email"] = reader.GetValue(1)?.ToString(),
                ["created_at"] = reader.GetValue(2)?.ToString(),
                ["profile_data"] = reader.GetValue(3)?.ToString()
            };

            var transformedRow = new Dictionary<string, string?>(originalRow);

            // Apply cell transformers
            foreach (var kvp in originalRow)
            {
                var context = new CellContext("users", kvp.Key, rowIndex, 
                    SqliteTypeHelper.GetSqliteType(reader, Array.IndexOf(new[] { "user_id", "email", "created_at", "profile_data" }, kvp.Key)));

                // Apply appropriate transformer based on column
                if (kvp.Key == "created_at" && epochTransformer.CanApply(context))
                {
                    transformedRow[$"{kvp.Key}_t"] = epochTransformer.Transform(context, kvp.Value);
                }
                else if (kvp.Key == "email" && emailMaskTransformer.CanApply(context))
                {
                    transformedRow[$"{kvp.Key}_t"] = emailMaskTransformer.Transform(context, kvp.Value);
                }
                else if (kvp.Key == "profile_data" && jsonTransformer.CanApply(context))
                {
                    transformedRow[$"{kvp.Key}_t"] = jsonTransformer.Transform(context, kvp.Value);
                }
            }

            transformedRows.Add(transformedRow);
            rowIndex++;
        }

        // Assert - Verify transformations worked correctly
        Assert.Equal(3, transformedRows.Count);

        // Check first user
        var user1 = transformedRows[0];
        Assert.Equal("1", user1["user_id"]);
        Assert.Equal("user1@example.com", user1["email"]);
        Assert.Equal("u***@example.com", user1["email_t"]); // Masked email
        Assert.Equal("1692123456", user1["created_at"]);
        Assert.Equal("epoch:1692123456", user1["created_at_t"]); // Formatted epoch
        Assert.Contains("user1@example.com", user1["profile_data"]!);
        Assert.Equal("compact_json", user1["profile_data_t"]); // Compacted JSON

        // Check transformers were called correctly
        Assert.Equal(3, epochTransformer.TransformCalls.Count);
        Assert.Equal(3, emailMaskTransformer.TransformCalls.Count);
        Assert.Equal(3, jsonTransformer.TransformCalls.Count);

        // Verify context information was passed correctly
        var epochCall = epochTransformer.TransformCalls.First(c => c.Context.RowIndex == 0);
        Assert.Equal("users", epochCall.Context.Table);
        Assert.Equal("created_at", epochCall.Context.Column);
        Assert.Equal(0, epochCall.Context.RowIndex);
        Assert.Equal(SqliteAffinity.Integer, epochCall.Context.Affinity);
    }

    [Fact]
    public async Task TransformerError_ShouldBeCapturedAndHandledGracefully()
    {
        // Arrange
        var dbPath = CreateTestDatabase();
        _tempFiles.Add(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
        await connection.OpenAsync();

        var faultyTransformer = new FaultyTransformer();
        var errors = new List<TransformerException>();

        // Act - Process data with a faulty transformer
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT user_id, email FROM users LIMIT 1";
        using var reader = cmd.ExecuteReader();

        if (reader.Read())
        {
            var context = new CellContext("users", "email", 0, SqliteAffinity.Text);
            var email = reader.GetValue(1)?.ToString();

            try
            {
                faultyTransformer.Transform(context, email);
                Assert.True(false, "Expected transformer to throw exception");
            }
            catch (TransformerException ex)
            {
                errors.Add(ex);
            }
        }

        // Assert - Error should be captured with proper context
        Assert.Single(errors);
        var error = errors[0];
        Assert.Equal("FaultyTransformer", error.TransformerName);
        Assert.NotNull(error.CellContext);
        Assert.Equal("users", error.CellContext.Table);
        Assert.Equal("email", error.CellContext.Column);
        Assert.Equal(0, error.CellContext.RowIndex);
        Assert.Contains("Simulated transformer failure", error.Message);
    }

    [Fact]
    public void TransformerPerformance_ShouldBeAcceptableForLargeDatasets()
    {
        // Arrange - Simulate processing many rows
        const int rowCount = 10000;
        var transformer = new MockEpochTransformer();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act - Transform many values
        for (int i = 0; i < rowCount; i++)
        {
            var context = new CellContext("events", "timestamp", i, SqliteAffinity.Integer);
            transformer.Transform(context, "1692123456");
        }

        stopwatch.Stop();

        // Assert - Should complete in reasonable time (< 100ms for 10k transformations)
        Assert.True(stopwatch.ElapsedMilliseconds < 100, 
            $"Transformer took {stopwatch.ElapsedMilliseconds}ms for {rowCount} transformations");
        Assert.Equal(rowCount, transformer.TransformCalls.Count);
    }

    [Fact]
    public void TransformerStatelessness_ShouldBeVerifiedWithConcurrentAccess()
    {
        // Arrange
        var transformer = new MockEpochTransformer();
        const int threadCount = 10;
        const int operationsPerThread = 1000;
        var results = new List<string?>[threadCount];
        var tasks = new Task[threadCount];

        // Act - Run transformer from multiple threads concurrently
        for (int t = 0; t < threadCount; t++)
        {
            var threadIndex = t;
            results[threadIndex] = new List<string?>();
            
            tasks[threadIndex] = Task.Run(() =>
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    var context = new CellContext("table", "col", threadIndex * operationsPerThread + i, SqliteAffinity.Integer);
                    var result = transformer.Transform(context, $"value_{threadIndex}_{i}");
                    results[threadIndex].Add(result);
                }
            });
        }

        Task.WaitAll(tasks);

        // Assert - All operations should complete successfully
        for (int t = 0; t < threadCount; t++)
        {
            Assert.Equal(operationsPerThread, results[t].Count);
            
            // Each thread should get consistent results
            for (int i = 0; i < operationsPerThread; i++)
            {
                Assert.Equal($"epoch:value_{t}_{i}", results[t][i]);
            }
        }

        // Total call count should match expected
        Assert.Equal(threadCount * operationsPerThread, transformer.TransformCalls.Count);
    }

    private string CreateTestDatabase()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"transformer_test_{Guid.NewGuid():N}.db");
        
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE users (
                user_id INTEGER PRIMARY KEY,
                email TEXT NOT NULL,
                created_at INTEGER, -- Unix timestamp
                profile_data TEXT   -- JSON blob
            );

            INSERT INTO users VALUES 
            (1, 'user1@example.com', 1692123456, '{""name"":""John"",""email"":""user1@example.com"",""age"":30}'),
            (2, 'user2@test.org', 1692209856, '{""name"":""Jane"",""email"":""user2@test.org"",""age"":25}'),
            (3, 'admin@company.com', 1692296256, '{""name"":""Admin"",""email"":""admin@company.com"",""role"":""administrator""}');
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

// Mock transformers for testing
public class MockEpochTransformer : ICellTransformer
{
    public ConcurrentBag<(CellContext Context, string? Raw)> TransformCalls { get; } = new();

    public bool CanApply(CellContext ctx) => ctx.Column.Contains("timestamp") || ctx.Column.Contains("created_at");

    public string? Transform(CellContext ctx, string? raw)
    {
        TransformCalls.Add((ctx, raw));
        
        if (string.IsNullOrEmpty(raw))
            return raw;
            
        // Simple prefix transformation for testing
        return $"epoch:{raw}";
    }
}

public class MockJsonTransformer : ICellTransformer
{
    public ConcurrentBag<(CellContext Context, string? Raw)> TransformCalls { get; } = new();

    public bool CanApply(CellContext ctx) => ctx.Column.Contains("json") || ctx.Column.Contains("data");

    public string? Transform(CellContext ctx, string? raw)
    {
        TransformCalls.Add((ctx, raw));
        
        if (string.IsNullOrEmpty(raw))
            return raw;

        // Mock JSON compaction
        return "compact_json";
    }
}

public class MockEmailMaskTransformer : ICellTransformer
{
    public ConcurrentBag<(CellContext Context, string? Raw)> TransformCalls { get; } = new();

    public bool CanApply(CellContext ctx) => ctx.Column.Equals("email", StringComparison.OrdinalIgnoreCase);

    public string? Transform(CellContext ctx, string? raw)
    {
        TransformCalls.Add((ctx, raw));
        
        if (string.IsNullOrEmpty(raw) || !raw.Contains("@"))
            return raw;

        // Simple email masking
        var parts = raw.Split('@');
        return $"{parts[0][0]}***@{parts[1]}";
    }
}

public class FaultyTransformer : ICellTransformer
{
    public bool CanApply(CellContext ctx) => true;

    public string? Transform(CellContext ctx, string? raw)
    {
        throw new TransformerException("FaultyTransformer", ctx, 
            $"Simulated transformer failure for {ctx.Table}.{ctx.Column}[{ctx.RowIndex}]");
    }
}