using Xunit;
using Microsoft.Data.Sqlite;
using DB2XL.Query;
using DB2XL.Core.Models;
using DB2XL.Core.Utilities;

namespace DB2XL.Query.Tests;

/// <summary>
/// Integration tests for PrimaryKeyDiscoveryService with various table schemas
/// Testing real-world scenarios and edge cases with actual SQLite databases
/// </summary>
public class PrimaryKeyDiscoveryIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly PrimaryKeyDiscoveryService _service;

    public PrimaryKeyDiscoveryIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _service = new PrimaryKeyDiscoveryService();
        
        CreateTestSchema();
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }

    private void CreateTestSchema()
    {
        var commands = new[]
        {
            // Table with single column explicit PK
            @"CREATE TABLE users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL UNIQUE,
                email TEXT,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP
            )",
            
            // Table with composite explicit PK
            @"CREATE TABLE order_items (
                order_id INTEGER NOT NULL,
                product_id INTEGER NOT NULL,
                quantity INTEGER NOT NULL DEFAULT 1,
                unit_price REAL,
                PRIMARY KEY (order_id, product_id)
            )",
            
            // Table with unique index (no explicit PK)
            @"CREATE TABLE products (
                sku TEXT NOT NULL,
                name TEXT NOT NULL,
                category TEXT,
                price REAL,
                stock_count INTEGER DEFAULT 0
            )",
            @"CREATE UNIQUE INDEX idx_products_sku ON products(sku)",
            
            // Table with composite unique index
            @"CREATE TABLE inventory (
                warehouse_id INTEGER NOT NULL,
                product_sku TEXT NOT NULL,
                quantity INTEGER DEFAULT 0,
                last_updated TEXT
            )",
            @"CREATE UNIQUE INDEX idx_inventory_location ON inventory(warehouse_id, product_sku)",
            
            // Regular table with implicit rowid
            @"CREATE TABLE logs (
                timestamp TEXT,
                level TEXT,
                message TEXT,
                source TEXT
            )",
            
            // WITHOUT ROWID table with explicit PK
            @"CREATE TABLE settings (
                key TEXT PRIMARY KEY,
                value TEXT,
                category TEXT DEFAULT 'general'
            ) WITHOUT ROWID",
            
            // WITHOUT ROWID table with composite PK
            @"CREATE TABLE user_permissions (
                user_id INTEGER,
                permission TEXT,
                granted_by INTEGER,
                granted_at TEXT DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (user_id, permission)
            ) WITHOUT ROWID",
            
            // Table with nullable unique index (should fall back to rowid)
            @"CREATE TABLE customers (
                name TEXT NOT NULL,
                email TEXT, -- nullable
                phone TEXT,
                address TEXT
            )",
            @"CREATE UNIQUE INDEX idx_customers_email ON customers(email)",
            
            // Table with partial unique index (should fall back to rowid)
            @"CREATE TABLE orders (
                id INTEGER,
                customer_email TEXT,
                status TEXT NOT NULL DEFAULT 'pending',
                total REAL,
                order_date TEXT DEFAULT CURRENT_TIMESTAMP
            )",
            @"CREATE UNIQUE INDEX idx_orders_active ON orders(customer_email) WHERE status != 'cancelled'",
            
            // Complex table with multiple indexes
            @"CREATE TABLE transactions (
                id INTEGER PRIMARY KEY,
                account_id INTEGER NOT NULL,
                transaction_type TEXT NOT NULL,
                amount REAL NOT NULL,
                reference_number TEXT NOT NULL,
                processed_at TEXT DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (account_id) REFERENCES accounts(id)
            )",
            @"CREATE UNIQUE INDEX idx_transactions_ref ON transactions(reference_number)",
            @"CREATE INDEX idx_transactions_account ON transactions(account_id)",
            @"CREATE INDEX idx_transactions_date ON transactions(processed_at)"
        };

        foreach (var sql in commands)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    [Fact]
    public void IntegrationTest_SingleColumnExplicitPK_AutoIncrement()
    {
        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "users");

        // Assert
        Assert.Equal(PrimaryKeyStrategy.ExplicitPrimaryKey, pk.Strategy);
        Assert.Single(pk.Columns);
        Assert.Equal("id", pk.Columns[0]);
        Assert.True(pk.IsDeterministic);
        Assert.Contains("Single column primary key: id", pk.Description);
        Assert.Equal(1, pk.Metadata["columnCount"]);
        Assert.Equal(false, pk.Metadata["composite"]);
    }

    [Fact]
    public void IntegrationTest_CompositeExplicitPK_OrderItems()
    {
        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "order_items");

        // Assert
        Assert.Equal(PrimaryKeyStrategy.ExplicitPrimaryKey, pk.Strategy);
        Assert.Equal(2, pk.Columns.Count);
        Assert.Contains("order_id", pk.Columns);
        Assert.Contains("product_id", pk.Columns);
        Assert.True(pk.IsDeterministic);
        Assert.Contains("Composite primary key:", pk.Description);
        Assert.Equal(2, pk.Metadata["columnCount"]);
        Assert.Equal(true, pk.Metadata["composite"]);
    }

    [Fact]
    public void IntegrationTest_UniqueIndexAsPK_Products()
    {
        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "products");

        // Assert
        Assert.Equal(PrimaryKeyStrategy.UniqueIndex, pk.Strategy);
        Assert.Single(pk.Columns);
        Assert.Equal("sku", pk.Columns[0]);
        Assert.True(pk.IsDeterministic);
        Assert.Contains("Unique index as PK:", pk.Description);
        Assert.Contains("idx_products_sku", pk.Description);
        Assert.Equal("idx_products_sku", pk.Metadata["indexName"]);
        Assert.Equal(true, pk.Metadata["allNotNull"]);
    }

    [Fact]
    public void IntegrationTest_CompositeUniqueIndex_Inventory()
    {
        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "inventory");

        // Assert
        Assert.Equal(PrimaryKeyStrategy.UniqueIndex, pk.Strategy);
        Assert.Equal(2, pk.Columns.Count);
        Assert.Contains("warehouse_id", pk.Columns);
        Assert.Contains("product_sku", pk.Columns);
        Assert.True(pk.IsDeterministic);
        Assert.Contains("Unique index as PK:", pk.Description);
        Assert.Contains("idx_inventory_location", pk.Description);
    }

    [Fact]
    public void IntegrationTest_ImplicitRowId_LogsTable()
    {
        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "logs");

        // Assert
        Assert.Equal(PrimaryKeyStrategy.ImplicitRowId, pk.Strategy);
        Assert.Single(pk.Columns);
        Assert.Equal("rowid", pk.Columns[0]);
        Assert.True(pk.IsDeterministic);
        Assert.Contains("SQLite implicit rowid", pk.Description);
        Assert.Equal(true, pk.Metadata["implicit"]);
        Assert.Equal(true, pk.Metadata["stable"]);
    }

    [Fact]
    public void IntegrationTest_WithoutRowIdExplicitPK_Settings()
    {
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
    public void IntegrationTest_WithoutRowIdComposite_UserPermissions()
    {
        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "user_permissions");

        // Assert
        Assert.Equal(PrimaryKeyStrategy.ExplicitPrimaryKey, pk.Strategy);
        Assert.Equal(2, pk.Columns.Count);
        Assert.Contains("user_id", pk.Columns);
        Assert.Contains("permission", pk.Columns);
        Assert.True(pk.IsDeterministic);
        Assert.Contains("Composite primary key:", pk.Description);
    }

    [Fact]
    public void IntegrationTest_NullableUniqueIndex_FallsBackToRowId()
    {
        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "customers");

        // Assert - Should fall back to rowid since email is nullable
        Assert.Equal(PrimaryKeyStrategy.ImplicitRowId, pk.Strategy);
        Assert.Single(pk.Columns);
        Assert.Equal("rowid", pk.Columns[0]);
        Assert.True(pk.IsDeterministic);
    }

    [Fact]
    public void IntegrationTest_PartialUniqueIndex_FallsBackToRowId()
    {
        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "orders");

        // Assert - Should fall back to rowid since index has WHERE clause
        Assert.Equal(PrimaryKeyStrategy.ImplicitRowId, pk.Strategy);
        Assert.Single(pk.Columns);
        Assert.Equal("rowid", pk.Columns[0]);
        Assert.True(pk.IsDeterministic);
    }

    [Fact]
    public void IntegrationTest_ExplicitPKWithMultipleIndexes_Transactions()
    {
        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "transactions");

        // Assert - Should prefer explicit PK over unique index
        Assert.Equal(PrimaryKeyStrategy.ExplicitPrimaryKey, pk.Strategy);
        Assert.Single(pk.Columns);
        Assert.Equal("id", pk.Columns[0]);
        Assert.True(pk.IsDeterministic);
        Assert.Contains("Single column primary key: id", pk.Description);
    }

    [Fact]
    public void IntegrationTest_OrderByClauseGeneration_VariousStrategies()
    {
        // Test ORDER BY generation for different strategies
        var testCases = new[]
        {
            ("users", "\"id\" ASC"),
            ("order_items", "\"order_id\" ASC, \"product_id\" ASC"),
            ("products", "\"sku\" ASC"),
            ("inventory", "\"warehouse_id\" ASC, \"product_sku\" ASC"),
            ("logs", "\"rowid\" ASC"),
            ("settings", "\"key\" ASC"),
            ("user_permissions", "\"user_id\" ASC, \"permission\" ASC")
        };

        foreach (var (tableName, expectedOrderBy) in testCases)
        {
            // Act
            var pk = _service.DiscoverPrimaryKey(_connection, tableName);
            var orderBy = _service.GenerateOrderByClause(pk);

            // Assert
            Assert.Equal(expectedOrderBy, orderBy);
            Assert.True(pk.IsDeterministic);
        }
    }

    [Fact]
    public void IntegrationTest_ColumnInformationAccuracy()
    {
        // Test that column information is accurately retrieved
        var columns = _service.GetColumns(_connection, "users");

        Assert.Equal(4, columns.Count);
        
        var idColumn = columns.First(c => c.Name == "id");
        Assert.Equal("INTEGER", idColumn.Type);
        Assert.True(idColumn.IsPrimaryKey);
        
        var usernameColumn = columns.First(c => c.Name == "username");
        Assert.Equal("TEXT", usernameColumn.Type);
        Assert.True(usernameColumn.NotNull);
        Assert.False(usernameColumn.IsPrimaryKey);
        
        var emailColumn = columns.First(c => c.Name == "email");
        Assert.Equal("TEXT", emailColumn.Type);
        Assert.False(emailColumn.NotNull);
        
        var createdColumn = columns.First(c => c.Name == "created_at");
        Assert.Equal("CURRENT_TIMESTAMP", createdColumn.DefaultValue);
    }

    [Fact]
    public void IntegrationTest_IndexInformationAccuracy()
    {
        // Test index information retrieval
        var indexes = _service.GetIndexes(_connection, "transactions");

        Assert.Equal(3, indexes.Count);
        
        var uniqueIndex = indexes.First(i => i.Name == "idx_transactions_ref");
        Assert.True(uniqueIndex.IsUnique);
        Assert.Single(uniqueIndex.Columns);
        Assert.Equal("reference_number", uniqueIndex.Columns[0]);
        Assert.Null(uniqueIndex.WhereClause);
        
        var regularIndex = indexes.First(i => i.Name == "idx_transactions_account");
        Assert.False(regularIndex.IsUnique);
        Assert.Single(regularIndex.Columns);
        Assert.Equal("account_id", regularIndex.Columns[0]);
    }

    [Fact]
    public void IntegrationTest_WithoutRowIdDetection()
    {
        // Test WITHOUT ROWID detection
        Assert.True(_service.IsWithoutRowId(_connection, "settings"));
        Assert.True(_service.IsWithoutRowId(_connection, "user_permissions"));
        Assert.False(_service.IsWithoutRowId(_connection, "users"));
        Assert.False(_service.IsWithoutRowId(_connection, "logs"));
        Assert.False(_service.IsWithoutRowId(_connection, "products"));
    }

    [Fact]
    public void IntegrationTest_PrimaryKeyStrategyPriority()
    {
        // Verify that the strategy priority is correct:
        // 1. Explicit PK (highest priority)
        // 2. Unique index (if no explicit PK and all columns NOT NULL)
        // 3. Implicit rowid (for regular tables)
        // 4. Synthetic hash (for WITHOUT ROWID tables with no other options)

        // Create a test table with both explicit PK and unique index
        var sql = @"
            CREATE TABLE priority_test (
                id INTEGER PRIMARY KEY,
                code TEXT NOT NULL UNIQUE,
                data TEXT
            );
            CREATE UNIQUE INDEX idx_priority_code ON priority_test(code);";
        
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();

        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "priority_test");

        // Assert - Should prefer explicit PK over unique index
        Assert.Equal(PrimaryKeyStrategy.ExplicitPrimaryKey, pk.Strategy);
        Assert.Equal("id", pk.Columns[0]);
    }

    [Fact]
    public void IntegrationTest_SyntheticHashGeneration_RealisticScenario()
    {
        // Test the synthetic hash generation with realistic row data
        var testData = new[]
        {
            new object?[] { "config", "database_host", "general" },
            new object?[] { "config", "database_port", "general" },
            new object?[] { "feature", "enable_analytics", "features" },
            new object?[] { "feature", "enable_notifications", "features" }
        };

        var hashes = testData.Select(SyntheticPrimaryKeyGenerator.GenerateRowHash).ToList();

        // Assert all hashes are unique
        Assert.Equal(testData.Length, hashes.Distinct().Count());
        
        // Assert all hashes are 64 characters (SHA256 hex)
        Assert.All(hashes, hash => Assert.Equal(64, hash.Length));
        
        // Assert deterministic - same input produces same hash
        var duplicateHash = SyntheticPrimaryKeyGenerator.GenerateRowHash(testData[0]);
        Assert.Equal(hashes[0], duplicateHash);
    }

    [Fact]
    public void IntegrationTest_EdgeCase_EmptyDatabase()
    {
        // Create a fresh connection with no tables
        using var emptyConnection = new SqliteConnection("Data Source=:memory:");
        emptyConnection.Open();

        // For non-existent tables, PRAGMA table_info returns no rows,
        // so this should fall back to implicit rowid strategy
        var pk = _service.DiscoverPrimaryKey(emptyConnection, "nonexistent_table");
        
        // Should fall back to implicit rowid for non-existent table
        Assert.Equal(PrimaryKeyStrategy.ImplicitRowId, pk.Strategy);
        Assert.Equal("rowid", pk.Columns[0]);
    }

    [Fact]
    public void IntegrationTest_EdgeCase_ReservedSQLiteNames()
    {
        // Test with tables that have SQL reserved words or special characters
        var reservedWordTable = @"
            CREATE TABLE ""order"" (
                ""select"" INTEGER PRIMARY KEY,
                ""from"" TEXT,
                ""where"" TEXT
            )";
        
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = reservedWordTable;
        cmd.ExecuteNonQuery();

        // Act
        var pk = _service.DiscoverPrimaryKey(_connection, "order");

        // Assert
        Assert.Equal(PrimaryKeyStrategy.ExplicitPrimaryKey, pk.Strategy);
        Assert.Equal("select", pk.Columns[0]);
    }

    [Fact]
    public void IntegrationTest_PerformanceWithLargeSchema()
    {
        // Create multiple tables to test performance with larger schemas
        for (int i = 0; i < 50; i++)
        {
            var tableSql = $@"
                CREATE TABLE test_table_{i} (
                    id INTEGER PRIMARY KEY,
                    data_{i} TEXT,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                );
                CREATE INDEX idx_test_{i} ON test_table_{i}(data_{i});";
            
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = tableSql;
            cmd.ExecuteNonQuery();
        }

        // Act & Assert - Should complete quickly even with many tables
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        for (int i = 0; i < 50; i++)
        {
            var pk = _service.DiscoverPrimaryKey(_connection, $"test_table_{i}");
            Assert.Equal(PrimaryKeyStrategy.ExplicitPrimaryKey, pk.Strategy);
            Assert.Equal("id", pk.Columns[0]);
        }
        
        stopwatch.Stop();
        
        // Should complete all 50 discoveries in under 1 second on modern hardware
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, 
            $"Primary key discovery took {stopwatch.ElapsedMilliseconds}ms for 50 tables, expected < 1000ms");
    }
}