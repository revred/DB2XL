using Microsoft.Data.Sqlite;
using DB2XL.Data.Analysis;
using DB2XL.Core.Models;
using DB2XL.Core.Enums;
using Xunit;

namespace DB2XL.Data.Tests.Analysis;

public class ForeignKeyDiscoveryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ForeignKeyDiscoveryService _service;

    public ForeignKeyDiscoveryServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _service = new ForeignKeyDiscoveryService();
        SetupTestDatabase();
    }

    [Fact]
    public async Task DiscoverForeignKeysAsync_WithExplicitForeignKeys_ShouldFindRelationships()
    {
        // Arrange
        var tableNames = new[] { "orders", "customers" };

        // Act
        var relationships = await _service.DiscoverForeignKeysAsync(_connection, tableNames);

        // Assert
        Assert.Single(relationships);
        var relationship = relationships[0];
        Assert.Equal("orders", relationship.FromTable);
        Assert.Equal("customers", relationship.ToTable);
        Assert.Equal(new[] { "customer_id" }, relationship.FromColumns);
        Assert.Equal(new[] { "id" }, relationship.ToColumns);
        Assert.Equal(RelationshipType.ForeignKey, relationship.Type);
        Assert.Equal(1.0, relationship.ConfidenceScore);
        Assert.Equal(RelationshipDiscoveryMethod.ForeignKey, relationship.DiscoveryMethod);
    }

    [Fact]
    public async Task DiscoverByNamingPatternsAsync_WithStandardPattern_ShouldFindInferredRelationships()
    {
        // Arrange
        var tableNames = new[] { "order_items", "products", "orders" };
        var patterns = new[] { "*_id" };

        // Act  
        var relationships = await _service.DiscoverByNamingPatternsAsync(_connection, tableNames, patterns);

        // Assert
        Assert.True(relationships.Count >= 2); // Should find product_id and order_id relationships
        
        var productRelationship = relationships.FirstOrDefault(r => 
            r.FromTable == "order_items" && r.ToTable == "products");
        Assert.NotNull(productRelationship);
        Assert.Equal(RelationshipType.Inferred, productRelationship.Type);
        Assert.Equal(RelationshipDiscoveryMethod.NamingPattern, productRelationship.DiscoveryMethod);
        Assert.True(productRelationship.ConfidenceScore >= 0.3);
    }

    [Fact]
    public async Task DiscoverByNamingPatternsAsync_WithNoMatchingTables_ShouldReturnEmpty()
    {
        // Arrange
        var tableNames = new[] { "unrelated_table" };
        var patterns = new[] { "*_id" };

        // Act
        var relationships = await _service.DiscoverByNamingPatternsAsync(_connection, tableNames, patterns);

        // Assert
        Assert.Empty(relationships);
    }

    [Fact]
    public async Task DiscoverForeignKeysAsync_WithCompositeForeignKey_ShouldHandleMultipleColumns()
    {
        // Arrange
        await CreateCompositeKeyTables();
        var tableNames = new[] { "order_details", "product_variants" };

        // Act
        var relationships = await _service.DiscoverForeignKeysAsync(_connection, tableNames);

        // Assert
        var relationship = relationships.FirstOrDefault(r => 
            r.FromTable == "order_details" && r.ToTable == "product_variants");
        Assert.NotNull(relationship);
        Assert.Equal(2, relationship.FromColumns.Count);
        Assert.Equal(2, relationship.ToColumns.Count);
        Assert.Contains("product_id", relationship.FromColumns);
        Assert.Contains("variant_id", relationship.FromColumns);
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

    private async Task CreateCompositeKeyTables()
    {
        // Create product_variants table with composite primary key
        await _connection.ExecuteNonQueryAsync(@"
            CREATE TABLE product_variants (
                product_id INTEGER,
                variant_id INTEGER,
                sku TEXT,
                price REAL,
                PRIMARY KEY (product_id, variant_id)
            )");

        // Create order_details table with composite foreign key
        await _connection.ExecuteNonQueryAsync(@"
            CREATE TABLE order_details (
                order_id INTEGER,
                product_id INTEGER,
                variant_id INTEGER,
                quantity INTEGER,
                FOREIGN KEY (product_id, variant_id) REFERENCES product_variants(product_id, variant_id)
            )");
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}

/// <summary>
/// Extension methods for easier test database setup
/// </summary>
public static class SqliteConnectionExtensions
{
    public static void ExecuteNonQuery(this SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    public static async Task ExecuteNonQueryAsync(this SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }
}