using Microsoft.Data.Sqlite;

namespace DB2XL.Export.Bundle.Tests.TestHelpers;

/// <summary>
/// Helper class for creating test SQLite databases for bundle export testing.
/// Provides methods to create databases with various table structures and data.
/// </summary>
public sealed class TestDatabaseHelper : IDisposable
{
    private readonly List<string> _createdDatabases = new();

    /// <summary>
    /// Creates a simple test database with basic table structure.
    /// </summary>
    /// <param name="tableNames">Names of tables to create (optional)</param>
    /// <returns>Path to the created database file</returns>
    public async Task<string> CreateTestDatabaseAsync(string[]? tableNames = null)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_db_{Guid.NewGuid():N}.sqlite");
        _createdDatabases.Add(dbPath);

        var connectionString = $"Data Source={dbPath}";
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // Create default tables if none specified
        var tables = tableNames ?? new[] { "test_table" };

        foreach (var tableName in tables)
        {
            await CreateBasicTableAsync(connection, tableName);
        }

        return dbPath;
    }

    /// <summary>
    /// Creates a test database with sample data for more comprehensive testing.
    /// </summary>
    /// <returns>Path to the created database file</returns>
    public async Task<string> CreateTestDatabaseWithDataAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_db_data_{Guid.NewGuid():N}.sqlite");
        _createdDatabases.Add(dbPath);

        var connectionString = $"Data Source={dbPath}";
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // Create users table
        await ExecuteCommandAsync(connection, @"
            CREATE TABLE users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                email TEXT UNIQUE,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                is_active BOOLEAN DEFAULT 1
            )");

        // Create orders table
        await ExecuteCommandAsync(connection, @"
            CREATE TABLE orders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER,
                amount DECIMAL(10,2),
                order_date DATE,
                status TEXT DEFAULT 'pending',
                metadata TEXT,
                FOREIGN KEY (user_id) REFERENCES users(id)
            )");

        // Create products table
        await ExecuteCommandAsync(connection, @"
            CREATE TABLE products (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                description TEXT,
                price DECIMAL(8,2),
                category TEXT,
                image_data BLOB,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP
            )");

        // Insert sample data
        await InsertSampleDataAsync(connection);

        return dbPath;
    }

    /// <summary>
    /// Creates a database with specific characteristics for testing complexity determination.
    /// </summary>
    /// <param name="tableCount">Number of tables to create</param>
    /// <param name="rowsPerTable">Approximate rows per table</param>
    /// <returns>Path to the created database file</returns>
    public async Task<string> CreateComplexityTestDatabaseAsync(int tableCount = 10, int rowsPerTable = 1000)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_db_complex_{Guid.NewGuid():N}.sqlite");
        _createdDatabases.Add(dbPath);

        var connectionString = $"Data Source={dbPath}";
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        for (int i = 1; i <= tableCount; i++)
        {
            var tableName = $"table_{i:D3}";
            
            await ExecuteCommandAsync(connection, $@"
                CREATE TABLE {tableName} (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    data_{i}_text TEXT,
                    data_{i}_number INTEGER,
                    data_{i}_decimal REAL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                )");

            // Insert sample rows
            for (int j = 1; j <= Math.Min(rowsPerTable, 100); j++) // Limit for test performance
            {
                await ExecuteCommandAsync(connection, $@"
                    INSERT INTO {tableName} (data_{i}_text, data_{i}_number, data_{i}_decimal)
                    VALUES ('Sample data {j}', {j}, {j * 1.5})");
            }
        }

        return dbPath;
    }

    /// <summary>
    /// Creates a database with various data types for comprehensive testing.
    /// </summary>
    /// <returns>Path to the created database file</returns>
    public async Task<string> CreateDataTypesTestDatabaseAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_db_types_{Guid.NewGuid():N}.sqlite");
        _createdDatabases.Add(dbPath);

        var connectionString = $"Data Source={dbPath}";
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await ExecuteCommandAsync(connection, @"
            CREATE TABLE data_types_test (
                id INTEGER PRIMARY KEY,
                text_field TEXT,
                integer_field INTEGER,
                real_field REAL,
                blob_field BLOB,
                numeric_field NUMERIC,
                boolean_field BOOLEAN,
                date_field DATE,
                datetime_field DATETIME,
                null_field TEXT
            )");

        // Insert sample data with various types
        await ExecuteCommandAsync(connection, @"
            INSERT INTO data_types_test (
                text_field, integer_field, real_field, blob_field, 
                numeric_field, boolean_field, date_field, datetime_field, null_field
            ) VALUES (
                'Sample text', 42, 3.14159, x'48656C6C6F',
                123.45, 1, '2025-01-01', '2025-01-01 12:00:00', NULL
            )");

        await ExecuteCommandAsync(connection, @"
            INSERT INTO data_types_test (
                text_field, integer_field, real_field, blob_field, 
                numeric_field, boolean_field, date_field, datetime_field, null_field
            ) VALUES (
                'Unicode: 你好', -100, -2.718, x'576F726C64',
                -99.99, 0, '2024-12-31', '2024-12-31 23:59:59', NULL
            )");

        return dbPath;
    }

    /// <summary>
    /// Creates an empty database with no tables for edge case testing.
    /// </summary>
    /// <returns>Path to the created database file</returns>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_db_empty_{Guid.NewGuid():N}.sqlite");
        _createdDatabases.Add(dbPath);

        var connectionString = $"Data Source={dbPath}";
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // Just open and close to create an empty database
        await Task.Delay(1); // Ensure async pattern

        return dbPath;
    }

    private static async Task CreateBasicTableAsync(SqliteConnection connection, string tableName)
    {
        var sanitizedTableName = SanitizeTableName(tableName);
        await ExecuteCommandAsync(connection, $@"
            CREATE TABLE {sanitizedTableName} (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                value INTEGER,
                created_at DATETIME DEFAULT CURRENT_TIMESTAMP
            )");

        // Insert a single test row
        await ExecuteCommandAsync(connection, $@"
            INSERT INTO {sanitizedTableName} (name, value) 
            VALUES ('Test Record', 100)");
    }

    private static async Task InsertSampleDataAsync(SqliteConnection connection)
    {
        // Insert sample users
        var userInserts = new[]
        {
            "('Alice Johnson', 'alice@example.com')",
            "('Bob Smith', 'bob@example.com')",
            "('Carol Davis', 'carol@example.com')",
            "('David Wilson', 'david@example.com')",
            "('Eve Brown', 'eve@example.com')"
        };

        foreach (var userValues in userInserts)
        {
            await ExecuteCommandAsync(connection, $"INSERT INTO users (name, email) VALUES {userValues}");
        }

        // Insert sample orders
        var orderInserts = new[]
        {
            "(1, 99.99, '2025-01-15', 'completed', '{\"priority\": \"normal\"}')",
            "(2, 149.50, '2025-01-16', 'pending', '{\"priority\": \"high\"}')",
            "(1, 75.25, '2025-01-17', 'completed', '{\"priority\": \"low\"}')",
            "(3, 200.00, '2025-01-18', 'processing', '{\"priority\": \"normal\"}')",
            "(2, 50.75, '2025-01-19', 'cancelled', '{\"priority\": \"low\"}')"
        };

        foreach (var orderValues in orderInserts)
        {
            await ExecuteCommandAsync(connection, $"INSERT INTO orders (user_id, amount, order_date, status, metadata) VALUES {orderValues}");
        }

        // Insert sample products
        var productInserts = new[]
        {
            "('Laptop Computer', 'High-performance laptop', 999.99, 'Electronics')",
            "('Wireless Mouse', 'Ergonomic wireless mouse', 29.99, 'Electronics')",
            "('Office Chair', 'Comfortable office chair', 199.99, 'Furniture')",
            "('Desk Lamp', 'LED desk lamp with dimmer', 45.99, 'Lighting')",
            "('Coffee Mug', 'Ceramic coffee mug', 12.99, 'Kitchen')"
        };

        foreach (var productValues in productInserts)
        {
            await ExecuteCommandAsync(connection, $"INSERT INTO products (name, description, price, category) VALUES {productValues}");
        }
    }

    private static async Task ExecuteCommandAsync(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string SanitizeTableName(string tableName)
    {
        // Basic sanitization for table names in tests
        return tableName.Replace(" ", "_")
                        .Replace("-", "_")
                        .Replace(".", "_");
    }

    public void Dispose()
    {
        // Clean up all created test databases
        foreach (var dbPath in _createdDatabases)
        {
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
        
        _createdDatabases.Clear();
    }
}