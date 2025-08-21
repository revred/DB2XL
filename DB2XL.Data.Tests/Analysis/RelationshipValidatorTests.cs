using Microsoft.Data.Sqlite;
using DB2XL.Data.Analysis;
using DB2XL.Core.Models;
using DB2XL.Core.Enums;
using Xunit;

namespace DB2XL.Data.Tests.Analysis;

public class RelationshipValidatorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RelationshipValidator _validator;

    public RelationshipValidatorTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _validator = new RelationshipValidator();
        SetupTestDatabase();
    }

    [Fact]
    public async Task ValidateAndResolveConflictsAsync_WithValidRelationship_ShouldKeepRelationship()
    {
        // Arrange
        var relationships = new[]
        {
            new GraphEdge("orders", "customers", RelationshipType.ForeignKey)
            {
                FromColumns = new[] { "customer_id" },
                ToColumns = new[] { "id" },
                ConfidenceScore = 1.0,
                DiscoveryMethod = RelationshipDiscoveryMethod.ForeignKey
            }
        };

        var options = new GraphAnalysisOptions();

        // Act
        var result = await _validator.ValidateAndResolveConflictsAsync(_connection, relationships, options);

        // Assert
        Assert.Single(result);
        var relationship = result[0];
        Assert.Equal("orders", relationship.FromTable);
        Assert.Equal("customers", relationship.ToTable);
        Assert.Equal(1.0, relationship.ConfidenceScore);
    }

    [Fact]
    public async Task ValidateAndResolveConflictsAsync_WithInvalidTable_ShouldExcludeRelationship()
    {
        // Arrange
        var relationships = new[]
        {
            new GraphEdge("nonexistent_table", "customers", RelationshipType.Inferred)
            {
                FromColumns = new[] { "customer_id" },
                ToColumns = new[] { "id" },
                ConfidenceScore = 0.8,
                DiscoveryMethod = RelationshipDiscoveryMethod.NamingPattern
            }
        };

        var options = new GraphAnalysisOptions();

        // Act
        var result = await _validator.ValidateAndResolveConflictsAsync(_connection, relationships, options);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ValidateAndResolveConflictsAsync_WithConflictingRelationships_PreferForeignKeys_ShouldChooseForeignKey()
    {
        // Arrange
        var relationships = new[]
        {
            new GraphEdge("orders", "customers", RelationshipType.ForeignKey)
            {
                FromColumns = new[] { "customer_id" },
                ToColumns = new[] { "id" },
                ConfidenceScore = 1.0,
                DiscoveryMethod = RelationshipDiscoveryMethod.ForeignKey
            },
            new GraphEdge("orders", "customers", RelationshipType.Inferred)
            {
                FromColumns = new[] { "customer_id" },
                ToColumns = new[] { "id" },
                ConfidenceScore = 0.9,
                DiscoveryMethod = RelationshipDiscoveryMethod.NamingPattern
            }
        };

        var options = new GraphAnalysisOptions
        {
            ConflictResolutionStrategy = ConflictResolutionStrategy.PreferForeignKeys
        };

        // Act
        var result = await _validator.ValidateAndResolveConflictsAsync(_connection, relationships, options);

        // Assert
        Assert.Single(result);
        var relationship = result[0];
        Assert.Equal(RelationshipDiscoveryMethod.ForeignKey, relationship.DiscoveryMethod);
        Assert.Equal(1.0, relationship.ConfidenceScore);
    }

    [Fact]
    public async Task ValidateAndResolveConflictsAsync_WithConflictingRelationships_HighestConfidence_ShouldChooseHighestScore()
    {
        // Arrange
        var relationships = new[]
        {
            new GraphEdge("orders", "customers", RelationshipType.Inferred)
            {
                FromColumns = new[] { "customer_id" },
                ToColumns = new[] { "id" },
                ConfidenceScore = 0.7,
                DiscoveryMethod = RelationshipDiscoveryMethod.NamingPattern
            },
            new GraphEdge("orders", "customers", RelationshipType.Inferred)
            {
                FromColumns = new[] { "customer_id" },
                ToColumns = new[] { "id" },
                ConfidenceScore = 0.9,
                DiscoveryMethod = RelationshipDiscoveryMethod.StatisticalAnalysis
            }
        };

        var options = new GraphAnalysisOptions
        {
            ConflictResolutionStrategy = ConflictResolutionStrategy.HighestConfidence
        };

        // Act
        var result = await _validator.ValidateAndResolveConflictsAsync(_connection, relationships, options);

        // Assert
        Assert.Single(result);
        var relationship = result[0];
        Assert.Equal(RelationshipDiscoveryMethod.StatisticalAnalysis, relationship.DiscoveryMethod);
        Assert.Equal(0.9, relationship.ConfidenceScore);
    }

    [Fact]
    public async Task ValidateAndResolveConflictsAsync_WithLowConfidenceRelationship_ShouldExclude()
    {
        // Arrange
        var relationships = new[]
        {
            new GraphEdge("orders", "customers", RelationshipType.Inferred)
            {
                FromColumns = new[] { "customer_id" },
                ToColumns = new[] { "id" },
                ConfidenceScore = 0.3,
                DiscoveryMethod = RelationshipDiscoveryMethod.NamingPattern
            }
        };

        var options = new GraphAnalysisOptions
        {
            MinimumConfidenceScore = 0.5
        };

        // Act
        var result = await _validator.ValidateAndResolveConflictsAsync(_connection, relationships, options);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ValidateAndResolveConflictsAsync_WithInvalidColumns_ShouldExcludeRelationship()
    {
        // Arrange
        var relationships = new[]
        {
            new GraphEdge("orders", "customers", RelationshipType.Inferred)
            {
                FromColumns = new[] { "nonexistent_column" },
                ToColumns = new[] { "id" },
                ConfidenceScore = 0.8,
                DiscoveryMethod = RelationshipDiscoveryMethod.NamingPattern
            }
        };

        var options = new GraphAnalysisOptions();

        // Act
        var result = await _validator.ValidateAndResolveConflictsAsync(_connection, relationships, options);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task ValidateAndResolveConflictsAsync_WithMostRestrictiveStrategy_ShouldPreferOneToOneCardinality()
    {
        // Arrange
        var relationships = new[]
        {
            new GraphEdge("orders", "customers", RelationshipType.Inferred)
            {
                FromColumns = new[] { "customer_id" },
                ToColumns = new[] { "id" },
                ConfidenceScore = 0.8,
                DiscoveryMethod = RelationshipDiscoveryMethod.NamingPattern,
                Cardinality = RelationshipCardinality.ManyToMany
            },
            new GraphEdge("orders", "customers", RelationshipType.Inferred)
            {
                FromColumns = new[] { "customer_id" },
                ToColumns = new[] { "id" },
                ConfidenceScore = 0.7,
                DiscoveryMethod = RelationshipDiscoveryMethod.StatisticalAnalysis,
                Cardinality = RelationshipCardinality.OneToOne
            }
        };

        var options = new GraphAnalysisOptions
        {
            ConflictResolutionStrategy = ConflictResolutionStrategy.MostRestrictive
        };

        // Act
        var result = await _validator.ValidateAndResolveConflictsAsync(_connection, relationships, options);

        // Assert
        Assert.Single(result);
        var relationship = result[0];
        Assert.Equal(RelationshipCardinality.OneToOne, relationship.Cardinality);
    }

    [Fact]
    public async Task ValidateAndResolveConflictsAsync_WithKeepAllStrategy_ShouldKeepBestRelationship()
    {
        // Arrange
        var relationships = new[]
        {
            new GraphEdge("orders", "customers", RelationshipType.ForeignKey)
            {
                FromColumns = new[] { "customer_id" },
                ToColumns = new[] { "id" },
                ConfidenceScore = 1.0,
                DiscoveryMethod = RelationshipDiscoveryMethod.ForeignKey
            },
            new GraphEdge("orders", "customers", RelationshipType.Inferred)
            {
                FromColumns = new[] { "customer_id" },
                ToColumns = new[] { "id" },
                ConfidenceScore = 0.8,
                DiscoveryMethod = RelationshipDiscoveryMethod.NamingPattern
            }
        };

        var options = new GraphAnalysisOptions
        {
            ConflictResolutionStrategy = ConflictResolutionStrategy.KeepAll
        };

        // Act
        var result = await _validator.ValidateAndResolveConflictsAsync(_connection, relationships, options);

        // Assert
        Assert.Single(result);
        var relationship = result[0];
        Assert.Equal(RelationshipDiscoveryMethod.ForeignKey, relationship.DiscoveryMethod);
        Assert.Equal(1.0, relationship.ConfidenceScore);
    }

    [Fact]
    public async Task ValidateAndResolveConflictsAsync_WithMultipleValidRelationshipGroups_ShouldReturnAll()
    {
        // Arrange
        var relationships = new[]
        {
            new GraphEdge("orders", "customers", RelationshipType.ForeignKey)
            {
                FromColumns = new[] { "customer_id" },
                ToColumns = new[] { "id" },
                ConfidenceScore = 1.0,
                DiscoveryMethod = RelationshipDiscoveryMethod.ForeignKey
            },
            new GraphEdge("order_items", "orders", RelationshipType.Inferred)
            {
                FromColumns = new[] { "order_id" },
                ToColumns = new[] { "id" },
                ConfidenceScore = 0.8,
                DiscoveryMethod = RelationshipDiscoveryMethod.NamingPattern
            }
        };

        var options = new GraphAnalysisOptions();

        // Act
        var result = await _validator.ValidateAndResolveConflictsAsync(_connection, relationships, options);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.FromTable == "orders" && r.ToTable == "customers");
        Assert.Contains(result, r => r.FromTable == "order_items" && r.ToTable == "orders");
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
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}