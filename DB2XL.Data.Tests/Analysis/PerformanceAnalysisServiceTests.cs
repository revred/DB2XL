using Microsoft.Data.Sqlite;
using DB2XL.Data.Analysis;
using DB2XL.Core.Models;
using Xunit;

namespace DB2XL.Data.Tests.Analysis;

public class PerformanceAnalysisServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly PerformanceAnalysisService _service;

    public PerformanceAnalysisServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _service = new PerformanceAnalysisService();
        SetupTestDatabase();
    }

    [Fact]
    public async Task AnalyzeDatabasePerformanceAsync_BasicDatabase_ShouldReturnComprehensiveAnalysis()
    {
        // Arrange
        var options = new PerformanceAnalysisOptions();

        // Act
        var analysis = await _service.AnalyzeDatabasePerformanceAsync(_connection, options);

        // Assert
        Assert.NotNull(analysis);
        Assert.NotEmpty(analysis.TableStatistics);
        Assert.NotNull(analysis.IndexAnalysis);
        Assert.NotNull(analysis.DatabaseGraph);
        Assert.True(analysis.OverallScore >= 0 && analysis.OverallScore <= 1);
    }

    [Fact]
    public async Task AnalyzeDatabasePerformanceAsync_WithQueries_ShouldAnalyzeQueryPerformance()
    {
        // Arrange
        var options = new PerformanceAnalysisOptions
        {
            CommonQueries = new[]
            {
                "SELECT * FROM customers WHERE id = ?",
                "SELECT c.name, COUNT(*) FROM customers c JOIN orders o ON c.id = o.customer_id GROUP BY c.id"
            }
        };

        // Act
        var analysis = await _service.AnalyzeDatabasePerformanceAsync(_connection, options);

        // Assert
        Assert.Equal(2, analysis.QueryAnalyses.Count);
        Assert.All(analysis.QueryAnalyses, qa => Assert.NotNull(qa.Metrics));
    }

    [Fact]
    public async Task AnalyzeDatabasePerformanceAsync_ShouldIdentifyMissingIndexes()
    {
        // Act
        var analysis = await _service.AnalyzeDatabasePerformanceAsync(_connection);

        // Assert
        Assert.NotEmpty(analysis.IndexAnalysis.MissingIndexRecommendations);
        Assert.Contains(analysis.IndexAnalysis.MissingIndexRecommendations,
            r => r.Reason == IndexRecommendationReason.ForeignKeyCandidate);
    }

    [Fact]
    public async Task AnalyzeDatabasePerformanceAsync_LargeTable_ShouldIdentifyPerformanceIssues()
    {
        // Arrange - Create a large table simulation by adding metadata
        await CreateLargeTableSimulation();

        // Act
        var analysis = await _service.AnalyzeDatabasePerformanceAsync(_connection);

        // Assert
        var largeTableStats = analysis.TableStatistics.FirstOrDefault(t => t.TableName == "large_table");
        Assert.NotNull(largeTableStats);
        Assert.True(largeTableStats.RowCount > 1000);
    }

    [Fact]
    public async Task AnalyzeQueryPerformanceAsync_SimpleQuery_ShouldReturnAnalysis()
    {
        // Arrange
        var query = "SELECT * FROM customers WHERE name = 'John'";

        // Act
        var analysis = await _service.AnalyzeQueryPerformanceAsync(_connection, query);

        // Assert
        Assert.NotNull(analysis);
        Assert.Equal(query, analysis.Query);
        Assert.NotEmpty(analysis.Steps);
        Assert.NotNull(analysis.Metrics);
    }

    [Fact]
    public async Task AnalyzeDatabasePerformanceAsync_WithColumnCardinality_ShouldAnalyzeColumns()
    {
        // Arrange
        var options = new PerformanceAnalysisOptions
        {
            AnalyzeColumnCardinality = true
        };

        // Act
        var analysis = await _service.AnalyzeDatabasePerformanceAsync(_connection, options);

        // Assert
        var customerStats = analysis.TableStatistics.FirstOrDefault(t => t.TableName == "customers");
        Assert.NotNull(customerStats);
        Assert.NotEmpty(customerStats.ColumnStatistics);
        
        var idColumn = customerStats.ColumnStatistics.FirstOrDefault(c => c.ColumnName == "id");
        Assert.NotNull(idColumn);
        Assert.True(idColumn.Selectivity > 0);
    }

    [Fact]
    public async Task AnalyzeDatabasePerformanceAsync_ShouldCalculateStorageInfo()
    {
        // Act
        var analysis = await _service.AnalyzeDatabasePerformanceAsync(_connection);

        // Assert
        Assert.All(analysis.TableStatistics, stats =>
        {
            Assert.NotNull(stats.StorageInfo);
            if (stats.RowCount > 0)
            {
                Assert.True(stats.StorageInfo.EstimatedSizeBytes >= 0);
            }
        });
    }

    [Fact]
    public async Task AnalyzeDatabasePerformanceAsync_ShouldProvideRecommendations()
    {
        // Arrange
        var options = new PerformanceAnalysisOptions
        {
            CommonQueries = new[] { "SELECT * FROM customers WHERE email = 'test@example.com'" }
        };

        // Act
        var analysis = await _service.AnalyzeDatabasePerformanceAsync(_connection, options);

        // Assert
        Assert.NotEmpty(analysis.Recommendations);
        Assert.Contains(analysis.Recommendations, 
            r => r.Type == OptimizationType.CreateIndex);
    }

    [Fact]
    public async Task AnalyzeDatabasePerformanceAsync_WithExistingIndexes_ShouldAnalyzeUsage()
    {
        // Arrange - Create an index
        await _connection.ExecuteNonQueryAsync("CREATE INDEX idx_customer_email ON customers (email)");

        // Act
        var analysis = await _service.AnalyzeDatabasePerformanceAsync(_connection);

        // Assert
        Assert.NotEmpty(analysis.IndexAnalysis.ExistingIndexes);
        var emailIndex = analysis.IndexAnalysis.ExistingIndexes
            .FirstOrDefault(idx => idx.IndexName.Contains("email", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(emailIndex);
        Assert.Contains("email", emailIndex.Columns, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, 4)] // Should analyze all tables
    [InlineData(false, 0)] // Should not analyze columns when disabled
    public async Task AnalyzeDatabasePerformanceAsync_ColumnCardinalityOption_ShouldRespectSetting(
        bool analyzeCardinality, int expectedMinTables)
    {
        // Arrange
        var options = new PerformanceAnalysisOptions
        {
            AnalyzeColumnCardinality = analyzeCardinality
        };

        // Act
        var analysis = await _service.AnalyzeDatabasePerformanceAsync(_connection, options);

        // Assert
        var tablesWithColumnStats = analysis.TableStatistics.Count(t => t.ColumnStatistics.Count > 0);
        if (analyzeCardinality)
        {
            Assert.True(tablesWithColumnStats >= expectedMinTables);
        }
        else
        {
            Assert.Equal(0, tablesWithColumnStats);
        }
    }

    [Fact]
    public async Task AnalyzeDatabasePerformanceAsync_IndexHealth_ShouldCalculateHealthScore()
    {
        // Arrange - Create some indexes to improve health
        await _connection.ExecuteNonQueryAsync("CREATE INDEX idx_orders_customer ON orders (customer_id)");
        await _connection.ExecuteNonQueryAsync("CREATE INDEX idx_order_items_order ON order_items (order_id)");

        // Act
        var analysis = await _service.AnalyzeDatabasePerformanceAsync(_connection);

        // Assert
        Assert.True(analysis.IndexAnalysis.OverallIndexHealth >= 0);
        Assert.True(analysis.IndexAnalysis.OverallIndexHealth <= 1);
        
        // With added indexes, health should be better than without
        Assert.True(analysis.IndexAnalysis.OverallIndexHealth > 0.1);
    }

    [Fact]
    public async Task AnalyzeDatabasePerformanceAsync_MultipleTableTypes_ShouldAnalyzeAll()
    {
        // Act
        var analysis = await _service.AnalyzeDatabasePerformanceAsync(_connection);

        // Assert
        var expectedTables = new[] { "customers", "orders", "products", "order_items" };
        foreach (var expectedTable in expectedTables)
        {
            Assert.Contains(analysis.TableStatistics, 
                stats => stats.TableName.Equals(expectedTable, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task AnalyzeDatabasePerformanceAsync_OverallScore_ShouldBeReasonable()
    {
        // Arrange - Add some indexes to improve the score
        await _connection.ExecuteNonQueryAsync("CREATE INDEX idx_orders_date ON orders (order_date)");

        // Act
        var analysis = await _service.AnalyzeDatabasePerformanceAsync(_connection);

        // Assert
        Assert.True(analysis.OverallScore >= 0.0);
        Assert.True(analysis.OverallScore <= 1.0);
        // With basic indexes and small dataset, should have decent score
        Assert.True(analysis.OverallScore >= 0.3);
    }

    private async Task CreateLargeTableSimulation()
    {
        // Create a table that simulates being large
        await _connection.ExecuteNonQueryAsync(@"
            CREATE TABLE large_table (
                id INTEGER PRIMARY KEY,
                data TEXT,
                value INTEGER,
                timestamp TEXT
            )");

        // Insert enough data to trigger large table analysis
        var sb = new System.Text.StringBuilder("INSERT INTO large_table (data, value, timestamp) VALUES ");
        for (int i = 0; i < 2000; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"('data_{i}', {i % 100}, '2023-01-{(i % 28) + 1:D2}')");
        }
        
        await _connection.ExecuteNonQueryAsync(sb.ToString());
    }

    private void SetupTestDatabase()
    {
        // Create customers table
        _connection.ExecuteNonQuery(@"
            CREATE TABLE customers (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT,
                created_at TEXT,
                status TEXT
            )");

        // Create orders table with foreign key
        _connection.ExecuteNonQuery(@"
            CREATE TABLE orders (
                id INTEGER PRIMARY KEY,
                customer_id INTEGER NOT NULL,
                order_date TEXT,
                total REAL,
                status TEXT,
                FOREIGN KEY (customer_id) REFERENCES customers(id)
            )");

        // Create products table
        _connection.ExecuteNonQuery(@"
            CREATE TABLE products (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                price REAL,
                category TEXT,
                description TEXT
            )");

        // Create order_items table
        _connection.ExecuteNonQuery(@"
            CREATE TABLE order_items (
                id INTEGER PRIMARY KEY,
                order_id INTEGER NOT NULL,
                product_id INTEGER NOT NULL,
                quantity INTEGER,
                unit_price REAL,
                FOREIGN KEY (order_id) REFERENCES orders(id),
                FOREIGN KEY (product_id) REFERENCES products(id)
            )");

        // Insert test data
        _connection.ExecuteNonQuery(@"
            INSERT INTO customers (id, name, email, created_at, status) VALUES 
            (1, 'John Doe', 'john@example.com', '2023-01-01', 'active'),
            (2, 'Jane Smith', 'jane@example.com', '2023-01-02', 'active'),
            (3, 'Bob Wilson', 'bob@gmail.com', '2023-01-03', 'inactive'),
            (4, 'Alice Brown', 'alice@test.com', '2023-01-04', 'active')");

        _connection.ExecuteNonQuery(@"
            INSERT INTO products (id, name, price, category, description) VALUES 
            (1, 'Widget A', 10.99, 'widgets', 'A great widget'),
            (2, 'Widget B', 15.50, 'widgets', 'An even better widget'),
            (3, 'Gadget X', 25.00, 'gadgets', 'Useful gadget'),
            (4, 'Tool Y', 5.99, 'tools', 'Handy tool'),
            (5, 'Premium Widget', 99.99, 'widgets', 'The best widget')");

        _connection.ExecuteNonQuery(@"
            INSERT INTO orders (id, customer_id, order_date, total, status) VALUES 
            (1, 1, '2023-01-01', 26.49, 'completed'),
            (2, 1, '2023-01-05', 25.00, 'completed'),
            (3, 2, '2023-01-02', 15.50, 'completed'),
            (4, 3, '2023-01-03', 5.99, 'pending'),
            (5, 4, '2023-01-04', 99.99, 'completed')");

        _connection.ExecuteNonQuery(@"
            INSERT INTO order_items (id, order_id, product_id, quantity, unit_price) VALUES 
            (1, 1, 1, 2, 10.99),
            (2, 1, 2, 1, 15.50),
            (3, 2, 3, 1, 25.00),
            (4, 3, 2, 1, 15.50),
            (5, 4, 4, 1, 5.99),
            (6, 5, 5, 1, 99.99)");
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}