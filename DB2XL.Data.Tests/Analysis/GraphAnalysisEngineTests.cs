using Microsoft.Data.Sqlite;
using DB2XL.Data.Analysis;
using DB2XL.Core.Models;
using DB2XL.Core.Enums;
using Xunit;

namespace DB2XL.Data.Tests.Analysis;

public class GraphAnalysisEngineTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly GraphAnalysisEngine _engine;

    public GraphAnalysisEngineTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _engine = new GraphAnalysisEngine();
        SetupTestDatabase();
    }

    [Fact]
    public async Task AnalyzeDatabaseAsync_WithDefaultOptions_ShouldBuildCompleteGraph()
    {
        // Arrange
        var options = new GraphAnalysisOptions();

        // Act
        var graph = await _engine.AnalyzeDatabaseAsync(_connection, options);

        // Assert
        Assert.NotNull(graph);
        Assert.True(graph.Nodes.Count >= 4); // customers, orders, products, order_items
        Assert.NotEmpty(graph.Edges);
        Assert.NotNull(graph.Statistics);
        Assert.True(graph.Statistics.AnalysisDurationMs > 0);
    }

    [Fact]
    public async Task AnalyzeDatabaseAsync_WithForeignKeyAnalysis_ShouldFindForeignKeyRelationships()
    {
        // Arrange
        var options = new GraphAnalysisOptions
        {
            AnalyzeForeignKeys = true,
            InferFromNaming = false
        };

        // Act
        var graph = await _engine.AnalyzeDatabaseAsync(_connection, options);

        // Assert
        var foreignKeyEdges = graph.Edges.Where(e => e.DiscoveryMethod == RelationshipDiscoveryMethod.ForeignKey);
        Assert.NotEmpty(foreignKeyEdges);
        
        var ordersToCustomers = foreignKeyEdges.FirstOrDefault(e => 
            e.FromTable == "orders" && e.ToTable == "customers");
        Assert.NotNull(ordersToCustomers);
        Assert.Equal(1.0, ordersToCustomers.ConfidenceScore);
    }

    [Fact]
    public async Task AnalyzeDatabaseAsync_WithNamingPatternAnalysis_ShouldFindInferredRelationships()
    {
        // Arrange
        var options = new GraphAnalysisOptions
        {
            AnalyzeForeignKeys = false,
            InferFromNaming = true
        };

        // Act
        var graph = await _engine.AnalyzeDatabaseAsync(_connection, options);

        // Assert
        var namingPatternEdges = graph.Edges.Where(e => e.DiscoveryMethod == RelationshipDiscoveryMethod.NamingPattern);
        Assert.NotEmpty(namingPatternEdges);
        
        // Should find relationships based on *_id pattern
        var orderItemRelationships = namingPatternEdges.Where(e => e.FromTable == "order_items");
        Assert.True(orderItemRelationships.Count() >= 1);
    }

    [Fact]
    public async Task AnalyzeDatabaseAsync_WithTableFiltering_ShouldRespectIncludeList()
    {
        // Arrange
        var options = new GraphAnalysisOptions
        {
            IncludeTables = new[] { "customers", "orders" }
        };

        // Act
        var graph = await _engine.AnalyzeDatabaseAsync(_connection, options);

        // Assert
        Assert.Equal(2, graph.Nodes.Count);
        Assert.Contains("customers", graph.Nodes.Keys);
        Assert.Contains("orders", graph.Nodes.Keys);
        Assert.DoesNotContain("products", graph.Nodes.Keys);
    }

    [Fact]
    public async Task AnalyzeDatabaseAsync_WithTableFiltering_ShouldRespectExcludeList()
    {
        // Arrange
        var options = new GraphAnalysisOptions
        {
            ExcludeTables = new[] { "products", "order_items" }
        };

        // Act
        var graph = await _engine.AnalyzeDatabaseAsync(_connection, options);

        // Assert
        Assert.DoesNotContain("products", graph.Nodes.Keys);
        Assert.DoesNotContain("order_items", graph.Nodes.Keys);
        Assert.Contains("customers", graph.Nodes.Keys);
        Assert.Contains("orders", graph.Nodes.Keys);
    }

    [Fact]
    public async Task AnalyzeDatabaseAsync_ShouldCalculateAccurateStatistics()
    {
        // Arrange
        var options = new GraphAnalysisOptions();

        // Act
        var graph = await _engine.AnalyzeDatabaseAsync(_connection, options);

        // Assert
        var stats = graph.Statistics;
        Assert.True(stats.NodeCount >= 4);
        Assert.True(stats.EdgeCount > 0);
        Assert.True(stats.AverageConnectivity >= 0);
        Assert.True(stats.Density >= 0 && stats.Density <= 1);
        Assert.True(stats.AverageConfidenceScore >= 0 && stats.AverageConfidenceScore <= 1);
        
        // Should have breakdown by method and type
        Assert.NotEmpty(stats.RelationshipsByMethod);
        Assert.NotEmpty(stats.RelationshipsByType);
    }

    [Fact]
    public async Task AnalyzeDatabaseAsync_ShouldBuildNodesWithCorrectMetadata()
    {
        // Arrange
        var options = new GraphAnalysisOptions();

        // Act
        var graph = await _engine.AnalyzeDatabaseAsync(_connection, options);

        // Assert
        var customersNode = graph.Nodes.GetValueOrDefault("customers");
        Assert.NotNull(customersNode);
        Assert.True(customersNode.RowCount.HasValue);
        Assert.True(customersNode.RowCount.Value >= 0);
        Assert.NotEmpty(customersNode.Columns);
        Assert.NotNull(customersNode.PrimaryKey);
        Assert.Single(customersNode.PrimaryKey.Columns);
        Assert.True(customersNode.PrimaryKey.IsDeterministic);
    }

    [Fact]
    public async Task AnalyzeDatabaseAsync_WithMinimumConfidenceFilter_ShouldExcludeLowConfidenceRelationships()
    {
        // Arrange
        var options = new GraphAnalysisOptions
        {
            MinimumConfidenceScore = 0.9
        };

        // Act
        var graph = await _engine.AnalyzeDatabaseAsync(_connection, options);

        // Assert
        // Should only include high-confidence relationships (foreign keys)
        Assert.All(graph.Edges, edge => Assert.True(edge.ConfidenceScore >= 0.9));
    }

    [Fact]
    public async Task AnalyzeDatabaseAsync_WithWildcardTableFilter_ShouldMatchCorrectTables()
    {
        // Arrange
        var options = new GraphAnalysisOptions
        {
            IncludeTables = new[] { "order*" }
        };

        // Act
        var graph = await _engine.AnalyzeDatabaseAsync(_connection, options);

        // Assert
        Assert.Contains("orders", graph.Nodes.Keys);
        Assert.Contains("order_items", graph.Nodes.Keys);
        Assert.DoesNotContain("customers", graph.Nodes.Keys);
        Assert.DoesNotContain("products", graph.Nodes.Keys);
    }

    [Fact]
    public async Task AnalyzeDatabaseAsync_WithEmptyDatabase_ShouldReturnEmptyGraph()
    {
        // Arrange
        using var emptyConnection = new SqliteConnection("Data Source=:memory:");
        emptyConnection.Open();
        
        var options = new GraphAnalysisOptions();

        // Act
        var graph = await _engine.AnalyzeDatabaseAsync(emptyConnection, options);

        // Assert
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
        Assert.Equal(0, graph.Statistics.NodeCount);
        Assert.Equal(0, graph.Statistics.EdgeCount);
    }

    private void SetupTestDatabase()
    {
        // Create customers table
        _connection.ExecuteNonQuery(@"
            CREATE TABLE customers (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT
            )");

        // Create orders table with foreign key
        _connection.ExecuteNonQuery(@"
            PRAGMA foreign_keys = ON;
            CREATE TABLE orders (
                id INTEGER PRIMARY KEY,
                customer_id INTEGER NOT NULL,
                order_date TEXT,
                total REAL,
                FOREIGN KEY (customer_id) REFERENCES customers(id)
            )");

        // Create products table
        _connection.ExecuteNonQuery(@"
            CREATE TABLE products (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                price REAL
            )");

        // Create order_items table (for naming pattern testing)
        _connection.ExecuteNonQuery(@"
            CREATE TABLE order_items (
                id INTEGER PRIMARY KEY,
                order_id INTEGER NOT NULL,
                product_id INTEGER NOT NULL,
                quantity INTEGER,
                unit_price REAL
            )");

        // Insert some test data
        _connection.ExecuteNonQuery(@"
            INSERT INTO customers (id, name, email) VALUES 
            (1, 'John Doe', 'john@example.com'),
            (2, 'Jane Smith', 'jane@example.com')");

        _connection.ExecuteNonQuery(@"
            INSERT INTO products (id, name, price) VALUES 
            (1, 'Widget A', 10.99),
            (2, 'Widget B', 15.50)");

        _connection.ExecuteNonQuery(@"
            INSERT INTO orders (id, customer_id, order_date, total) VALUES 
            (1, 1, '2023-01-01', 26.49),
            (2, 2, '2023-01-02', 15.50)");

        _connection.ExecuteNonQuery(@"
            INSERT INTO order_items (id, order_id, product_id, quantity, unit_price) VALUES 
            (1, 1, 1, 2, 10.99),
            (2, 1, 2, 1, 15.50),
            (3, 2, 2, 1, 15.50)");
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}