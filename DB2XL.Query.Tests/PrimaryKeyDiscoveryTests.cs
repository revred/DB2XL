using Xunit;
using Microsoft.Data.Sqlite;
using DB2XL.Query;

namespace DB2XL.Query.Tests;

public class PrimaryKeyDiscoveryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly PrimaryKeyDiscoveryService _service;

    public PrimaryKeyDiscoveryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _service = new PrimaryKeyDiscoveryService();
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }

    [Fact]
    public void DiscoverPrimaryKey_ExplicitSingleColumn_ReturnsExplicitStrategy()
    {
        // Arrange
        var sql = @"
            CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT,
                email TEXT
            )";
        ExecuteSql(sql);

        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "users");

        // Assert
        Assert.Equal(PrimaryKeyStrategy.ExplicitPrimaryKey, pk.Strategy);
        Assert.Single(pk.Columns);
        Assert.Equal("id", pk.Columns[0]);
        Assert.True(pk.IsDeterministic);
        Assert.Contains("Single column primary key: id", pk.Description);
    }

    [Fact]
    public void DiscoverPrimaryKey_ExplicitComposite_ReturnsExplicitStrategy()
    {
        // Arrange
        var sql = @"
            CREATE TABLE order_items (
                order_id INTEGER,
                product_id INTEGER,
                quantity INTEGER,
                PRIMARY KEY (order_id, product_id)
            )";
        ExecuteSql(sql);

        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "order_items");

        // Assert
        Assert.Equal(PrimaryKeyStrategy.ExplicitPrimaryKey, pk.Strategy);
        Assert.Equal(2, pk.Columns.Count);
        Assert.Contains("order_id", pk.Columns);
        Assert.Contains("product_id", pk.Columns);
        Assert.True(pk.IsDeterministic);
        Assert.Contains("Composite primary key:", pk.Description);
    }

    [Fact]
    public void DiscoverPrimaryKey_UniqueIndex_ReturnsUniqueIndexStrategy()
    {
        // Arrange
        var sql = @"
            CREATE TABLE products (
                id INTEGER,
                sku TEXT NOT NULL,
                name TEXT
            );
            CREATE UNIQUE INDEX idx_products_sku ON products(sku);";
        ExecuteSql(sql);

        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "products");

        // Assert
        Assert.Equal(PrimaryKeyStrategy.UniqueIndex, pk.Strategy);
        Assert.Single(pk.Columns);
        Assert.Equal("sku", pk.Columns[0]);
        Assert.True(pk.IsDeterministic);
        Assert.Contains("Unique index as PK:", pk.Description);
    }

    [Fact]
    public void DiscoverPrimaryKey_WithoutRowId_ReturnsExplicitStrategy()
    {
        // Arrange - WITHOUT ROWID table with explicit PK
        var sql = @"
            CREATE TABLE settings (
                key TEXT PRIMARY KEY,
                value TEXT,
                category TEXT
            ) WITHOUT ROWID";
        ExecuteSql(sql);

        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "settings");

        // Assert
        Assert.Equal(PrimaryKeyStrategy.ExplicitPrimaryKey, pk.Strategy);
        Assert.Single(pk.Columns);
        Assert.Equal("key", pk.Columns[0]);
        Assert.True(pk.IsDeterministic);
        Assert.Contains("Single column primary key: key", pk.Description);
    }

    [Fact]
    public void DiscoverPrimaryKey_ImplicitRowId_ReturnsRowIdStrategy()
    {
        // Arrange
        var sql = @"
            CREATE TABLE logs (
                timestamp TEXT,
                level TEXT,
                message TEXT
            )";
        ExecuteSql(sql);

        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "logs");

        // Assert
        Assert.Equal(PrimaryKeyStrategy.ImplicitRowId, pk.Strategy);
        Assert.Single(pk.Columns);
        Assert.Equal("rowid", pk.Columns[0]);
        Assert.True(pk.IsDeterministic);
        Assert.Contains("SQLite implicit rowid", pk.Description);
    }

    [Fact]
    public void GetColumns_ReturnsCorrectColumnInfo()
    {
        // Arrange
        var sql = @"
            CREATE TABLE test_table (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT,
                age INTEGER DEFAULT 0,
                active BOOLEAN
            )";
        ExecuteSql(sql);

        // Act
        var columns = _service.GetColumns(_connection, "test_table");

        // Assert
        Assert.Equal(5, columns.Count);

        var idColumn = columns.First(c => c.Name == "id");
        Assert.Equal("INTEGER", idColumn.Type);
        Assert.Equal(1, idColumn.PrimaryKey);
        Assert.False(idColumn.NotNull); // Even though it's PK, SQLite reports it as nullable

        var nameColumn = columns.First(c => c.Name == "name");
        Assert.Equal("TEXT", nameColumn.Type);
        Assert.True(nameColumn.NotNull);
        Assert.Equal(0, nameColumn.PrimaryKey);

        var ageColumn = columns.First(c => c.Name == "age");
        Assert.Equal("0", ageColumn.DefaultValue);
    }

    [Fact]
    public void GetIndexes_ReturnsCorrectIndexInfo()
    {
        // Arrange
        var sql = @"
            CREATE TABLE products (
                id INTEGER,
                sku TEXT,
                name TEXT,
                category TEXT
            );
            CREATE UNIQUE INDEX idx_sku ON products(sku);
            CREATE INDEX idx_category_name ON products(category, name);";
        ExecuteSql(sql);

        // Act
        var indexes = _service.GetIndexes(_connection, "products");

        // Assert
        Assert.Equal(2, indexes.Count);

        var uniqueIndex = indexes.First(i => i.Name == "idx_sku");
        Assert.True(uniqueIndex.IsUnique);
        Assert.Single(uniqueIndex.Columns);
        Assert.Equal("sku", uniqueIndex.Columns[0]);

        var compositeIndex = indexes.First(i => i.Name == "idx_category_name");
        Assert.False(compositeIndex.IsUnique);
        Assert.Equal(2, compositeIndex.Columns.Count);
        Assert.Contains("category", compositeIndex.Columns);
        Assert.Contains("name", compositeIndex.Columns);
    }

    [Fact]
    public void IsWithoutRowId_WithoutRowIdTable_ReturnsTrue()
    {
        // Arrange
        var sql = @"
            CREATE TABLE config (
                key TEXT PRIMARY KEY,
                value TEXT
            ) WITHOUT ROWID";
        ExecuteSql(sql);

        // Act
        var result = _service.IsWithoutRowId(_connection, "config");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsWithoutRowId_RegularTable_ReturnsFalse()
    {
        // Arrange
        var sql = @"
            CREATE TABLE regular_table (
                id INTEGER PRIMARY KEY,
                data TEXT
            )";
        ExecuteSql(sql);

        // Act
        var result = _service.IsWithoutRowId(_connection, "regular_table");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GenerateOrderByClause_SingleColumn_ReturnsCorrectClause()
    {
        // Arrange
        var pk = new PrimaryKeyInfo
        {
            Strategy = PrimaryKeyStrategy.ExplicitPrimaryKey,
            Columns = new[] { "id" },
            IsDeterministic = true
        };

        // Act
        var orderBy = _service.GenerateOrderByClause(pk);

        // Assert
        Assert.Equal("\"id\" ASC", orderBy);
    }

    [Fact]
    public void GenerateOrderByClause_CompositeKey_ReturnsCorrectClause()
    {
        // Arrange
        var pk = new PrimaryKeyInfo
        {
            Strategy = PrimaryKeyStrategy.ExplicitPrimaryKey,
            Columns = new[] { "order_id", "product_id" },
            IsDeterministic = true
        };

        // Act
        var orderBy = _service.GenerateOrderByClause(pk);

        // Assert
        Assert.Equal("\"order_id\" ASC, \"product_id\" ASC", orderBy);
    }

    [Fact]
    public void GenerateOrderByClause_NotDeterministic_ReturnsEmpty()
    {
        // Arrange
        var pk = new PrimaryKeyInfo
        {
            Strategy = PrimaryKeyStrategy.None,
            Columns = Array.Empty<string>(),
            IsDeterministic = false
        };

        // Act
        var orderBy = _service.GenerateOrderByClause(pk);

        // Assert
        Assert.Equal(string.Empty, orderBy);
    }

    [Fact]
    public void DiscoverPrimaryKey_UniqueIndexWithNullableColumn_FallsBackToRowId()
    {
        // Arrange - unique index on nullable column should not be used as PK
        var sql = @"
            CREATE TABLE customers (
                id INTEGER,
                email TEXT, -- nullable
                name TEXT
            );
            CREATE UNIQUE INDEX idx_email ON customers(email);";
        ExecuteSql(sql);

        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "customers");

        // Assert
        Assert.Equal(PrimaryKeyStrategy.ImplicitRowId, pk.Strategy);
        Assert.Equal("rowid", pk.Columns[0]);
    }

    [Fact]
    public void DiscoverPrimaryKey_PartialUniqueIndex_FallsBackToRowId()
    {
        // Arrange - partial index should not be used as PK
        var sql = @"
            CREATE TABLE orders (
                id INTEGER,
                status TEXT NOT NULL,
                external_id TEXT NOT NULL
            );
            CREATE UNIQUE INDEX idx_external_active ON orders(external_id) WHERE status = 'active';";
        ExecuteSql(sql);

        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "orders");

        // Assert
        Assert.Equal(PrimaryKeyStrategy.ImplicitRowId, pk.Strategy);
    }

    [Fact]
    public void DiscoverPrimaryKey_TestSyntheticHashGeneration()
    {
        // Arrange - test the synthetic hash generation directly using the helper
        var testValues = new object?[] { "test", 123, null, "value" };
        
        // Act
        var hash1 = SyntheticPrimaryKeyGenerator.GenerateRowHash(testValues);
        var hash2 = SyntheticPrimaryKeyGenerator.GenerateRowHash(testValues);
        
        // Assert - same values should produce same hash
        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // SHA256 hex string length
        Assert.NotEmpty(hash1);
        
        // Test different values produce different hashes
        var differentValues = new object?[] { "test", 124, null, "value" };
        var hash3 = SyntheticPrimaryKeyGenerator.GenerateRowHash(differentValues);
        Assert.NotEqual(hash1, hash3);
    }

    private void ExecuteSql(string sql)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}

public class SyntheticPrimaryKeyGeneratorTests
{
    [Fact]
    public void GenerateRowHash_SameValues_ReturnsSameHash()
    {
        // Arrange
        var values1 = new object?[] { "test", 123, null, true };
        var values2 = new object?[] { "test", 123, null, true };

        // Act
        var hash1 = SyntheticPrimaryKeyGenerator.GenerateRowHash(values1);
        var hash2 = SyntheticPrimaryKeyGenerator.GenerateRowHash(values2);

        // Assert
        Assert.Equal(hash1, hash2);
        Assert.NotEmpty(hash1);
        Assert.Equal(64, hash1.Length); // SHA256 produces 64 hex characters
    }

    [Fact]
    public void GenerateRowHash_DifferentValues_ReturnsDifferentHash()
    {
        // Arrange
        var values1 = new object?[] { "test", 123 };
        var values2 = new object?[] { "test", 124 };

        // Act
        var hash1 = SyntheticPrimaryKeyGenerator.GenerateRowHash(values1);
        var hash2 = SyntheticPrimaryKeyGenerator.GenerateRowHash(values2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void GenerateRowHash_NullValues_HandlesCorrectly()
    {
        // Arrange
        var values = new object?[] { null, "test", null };

        // Act
        var hash = SyntheticPrimaryKeyGenerator.GenerateRowHash(values);

        // Assert
        Assert.NotEmpty(hash);
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void GenerateRowHash_EmptyValues_HandlesCorrectly()
    {
        // Arrange
        var values = Array.Empty<object?>();

        // Act
        var hash = SyntheticPrimaryKeyGenerator.GenerateRowHash(values);

        // Assert
        Assert.NotEmpty(hash);
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void GenerateRowHash_DeterministicOrdering_SameInputSameOutput()
    {
        // Arrange - test that order matters
        var values1 = new object?[] { "a", "b", "c" };
        var values2 = new object?[] { "c", "b", "a" };

        // Act
        var hash1 = SyntheticPrimaryKeyGenerator.GenerateRowHash(values1);
        var hash2 = SyntheticPrimaryKeyGenerator.GenerateRowHash(values2);

        // Assert
        Assert.NotEqual(hash1, hash2); // Different order should produce different hash
    }
}