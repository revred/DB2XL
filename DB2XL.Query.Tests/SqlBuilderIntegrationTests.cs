using Xunit;
using Microsoft.Data.Sqlite;
using DB2XL.Query;

namespace DB2XL.Query.Tests;

public class SqlBuilderIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqlBuilder _sqlBuilder;
    private readonly QueryExecutor _queryExecutor;

    public SqlBuilderIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _sqlBuilder = new SqlBuilder();
        _queryExecutor = new QueryExecutor();
        
        CreateTestTables();
        InsertTestData();
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }

    private void CreateTestTables()
    {
        var commands = new[]
        {
            @"CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT UNIQUE,
                age INTEGER,
                active BOOLEAN DEFAULT 1,
                created_at TEXT
            )",
            @"CREATE TABLE orders (
                id INTEGER PRIMARY KEY,
                user_id INTEGER,
                total REAL,
                status TEXT,
                order_date TEXT,
                FOREIGN KEY (user_id) REFERENCES users(id)
            )",
            @"CREATE TABLE products (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                category TEXT,
                price REAL,
                in_stock INTEGER
            )"
        };

        foreach (var sql in commands)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    private void InsertTestData()
    {
        var commands = new[]
        {
            "INSERT INTO users (name, email, age, active, created_at) VALUES ('Alice', 'alice@test.com', 25, 1, '2023-01-15')",
            "INSERT INTO users (name, email, age, active, created_at) VALUES ('Bob', 'bob@test.com', 30, 1, '2023-02-01')",
            "INSERT INTO users (name, email, age, active, created_at) VALUES ('Charlie', 'charlie@test.com', 35, 0, '2023-03-10')",
            
            "INSERT INTO orders (user_id, total, status, order_date) VALUES (1, 99.99, 'completed', '2023-06-01')",
            "INSERT INTO orders (user_id, total, status, order_date) VALUES (1, 149.50, 'pending', '2023-06-15')",
            "INSERT INTO orders (user_id, total, status, order_date) VALUES (2, 75.25, 'completed', '2023-06-20')",
            
            "INSERT INTO products (name, category, price, in_stock) VALUES ('Laptop', 'electronics', 999.99, 5)",
            "INSERT INTO products (name, category, price, in_stock) VALUES ('Book', 'books', 29.99, 10)",
            "INSERT INTO products (name, category, price, in_stock) VALUES ('Phone', 'electronics', 599.99, 3)"
        };

        foreach (var sql in commands)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    [Fact]
    public void SqlBuilder_SimpleSelect_ReturnsAllUsers()
    {
        // Arrange
        var selection = SelectionGrammar.All("users");
        
        // Act
        var query = _sqlBuilder.BuildQuery(selection);
        var results = _queryExecutor.ExecuteQuery(_connection, query).ToList();
        
        // Assert
        Assert.Equal("SELECT * FROM \"users\"", query.Sql);
        Assert.Empty(query.Parameters);
        Assert.Equal(3, results.Count);
        Assert.Equal("Alice", results[0]["name"]);
        Assert.Equal("Bob", results[1]["name"]);
        Assert.Equal("Charlie", results[2]["name"]);
    }

    [Fact]
    public void SqlBuilder_SelectSpecificColumns_ReturnsCorrectData()
    {
        // Arrange
        var selection = SelectionGrammar.Columns("users", "name", "email");
        
        // Act
        var query = _sqlBuilder.BuildQuery(selection);
        var results = _queryExecutor.ExecuteQuery(_connection, query).ToList();
        
        // Assert
        Assert.Equal("SELECT \"name\", \"email\" FROM \"users\"", query.Sql);
        Assert.Equal(3, results.Count);
        Assert.Equal(2, results[0].Keys.Count);
        Assert.Contains("name", results[0].Keys);
        Assert.Contains("email", results[0].Keys);
    }

    [Fact]
    public void SqlBuilder_WhereEqual_FiltersCorrectly()
    {
        // Arrange
        var selection = SelectionBuilder
            .From("users")
            .SelectAll()
            .Where(Where.Equal("name", "Alice"))
            .Build();
        
        // Act
        var query = _sqlBuilder.BuildQuery(selection);
        var results = _queryExecutor.ExecuteQuery(_connection, query).ToList();
        
        // Assert
        Assert.Equal("SELECT * FROM \"users\" WHERE \"name\" = @param_0", query.Sql);
        Assert.Single(query.Parameters);
        Assert.Equal("Alice", query.Parameters["param_0"]);
        Assert.Single(results);
        Assert.Equal("Alice", results[0]["name"]);
    }

    [Fact]
    public void SqlBuilder_WhereIn_FiltersMultipleValues()
    {
        // Arrange
        var selection = SelectionBuilder
            .From("users")
            .Select("name", "age")
            .Where(Where.In("name", "Alice", "Bob"))
            .Build();
        
        // Act
        var query = _sqlBuilder.BuildQuery(selection);
        var results = _queryExecutor.ExecuteQuery(_connection, query).ToList();
        
        // Assert
        Assert.Equal("SELECT \"name\", \"age\" FROM \"users\" WHERE \"name\" IN (@param_0_0, @param_0_1)", query.Sql);
        Assert.Equal(2, query.Parameters.Count);
        Assert.Equal("Alice", query.Parameters["param_0_0"]);
        Assert.Equal("Bob", query.Parameters["param_0_1"]);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void SqlBuilder_ComplexWhere_HandlesAndOr()
    {
        // Arrange
        var selection = SelectionBuilder
            .From("users")
            .SelectAll()
            .Where(Where.And(
                Where.GreaterThan("age", 25),
                Where.Or(
                    Where.Equal("name", "Bob"),
                    Where.Equal("active", true)
                )
            ))
            .Build();
        
        // Act
        var query = _sqlBuilder.BuildQuery(selection);
        var results = _queryExecutor.ExecuteQuery(_connection, query).ToList();
        
        // Assert
        Assert.Contains("AND", query.Sql);
        Assert.Contains("OR", query.Sql);
        Assert.Equal(3, query.Parameters.Count);
        Assert.Equal(1, results.Count); // Only Bob matches: age > 25 AND (name = 'Bob' OR active = true)
    }

    [Fact]
    public void SqlBuilder_OrderBy_SortsCorrectly()
    {
        // Arrange
        var selection = SelectionBuilder
            .From("users")
            .Select("name", "age")
            .OrderByDesc("age")
            .OrderByAsc("name")
            .Build();
        
        // Act
        var query = _sqlBuilder.BuildQuery(selection);
        var results = _queryExecutor.ExecuteQuery(_connection, query).ToList();
        
        // Assert
        Assert.Equal("SELECT \"name\", \"age\" FROM \"users\" ORDER BY \"age\" DESC, \"name\" ASC", query.Sql);
        Assert.Equal(3, results.Count);
        Assert.Equal("Charlie", results[0]["name"]); // 35, highest age
        Assert.Equal("Bob", results[1]["name"]);     // 30
        Assert.Equal("Alice", results[2]["name"]);   // 25, lowest age
    }

    [Fact]
    public void SqlBuilder_LimitOffset_PaginatesCorrectly()
    {
        // Arrange
        var selection = SelectionBuilder
            .From("users")
            .SelectAll()
            .OrderByAsc("name")
            .Limit(2)
            .Offset(1)
            .Build();
        
        // Act
        var query = _sqlBuilder.BuildQuery(selection);
        var results = _queryExecutor.ExecuteQuery(_connection, query).ToList();
        
        // Assert
        Assert.Equal("SELECT * FROM \"users\" ORDER BY \"name\" ASC LIMIT 2 OFFSET 1", query.Sql);
        Assert.Equal(2, results.Count);
        Assert.Equal("Bob", results[0]["name"]);     // Skip Alice (offset 1)
        Assert.Equal("Charlie", results[1]["name"]); // Take 2 records
    }

    [Fact]
    public void SqlBuilder_CountQuery_ReturnsCorrectCount()
    {
        // Arrange
        var selection = SelectionBuilder
            .From("users")
            .SelectAll()
            .Where(Where.Equal("active", true))
            .Build();
        
        // Act
        var query = _sqlBuilder.BuildCountQuery(selection);
        var count = _queryExecutor.ExecuteCount(_connection, query);
        
        // Assert
        Assert.Equal("SELECT COUNT(*) FROM \"users\" WHERE \"active\" = @param_0", query.Sql);
        Assert.Equal(2, count); // Alice and Bob are active
    }

    [Fact]
    public void SqlBuilder_Between_FiltersByRange()
    {
        // Arrange
        var selection = SelectionBuilder
            .From("orders")
            .SelectAll()
            .Where(Where.Between("total", 75.0, 150.0))
            .Build();
        
        // Act
        var query = _sqlBuilder.BuildQuery(selection);
        var results = _queryExecutor.ExecuteQuery(_connection, query).ToList();
        
        // Assert
        Assert.Equal("SELECT * FROM \"orders\" WHERE \"total\" BETWEEN @param_0_start AND @param_0_end", query.Sql);
        Assert.Equal(2, query.Parameters.Count);
        Assert.Equal(75.0, query.Parameters["param_0_start"]);
        Assert.Equal(150.0, query.Parameters["param_0_end"]);
        Assert.Equal(3, results.Count); // All orders: 75.25, 99.99, and 149.50 are all within range
    }

    [Fact]
    public void SqlBuilder_Like_PerformsPatternMatching()
    {
        // Arrange
        var selection = SelectionBuilder
            .From("products")
            .SelectAll()
            .Where(Where.Like("name", "%oo%"))
            .Build();
        
        // Act
        var query = _sqlBuilder.BuildQuery(selection);
        var results = _queryExecutor.ExecuteQuery(_connection, query).ToList();
        
        // Assert
        Assert.Equal("SELECT * FROM \"products\" WHERE \"name\" LIKE @param_0", query.Sql);
        Assert.Equal("%oo%", query.Parameters["param_0"]);
        Assert.Single(results);
        Assert.Equal("Book", results[0]["name"]);
    }

    [Fact]
    public void SqlBuilder_IsNull_FindsNullValues()
    {
        // Insert a user with null email
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "INSERT INTO users (name, age, active) VALUES ('Dave', 40, 1)";
        cmd.ExecuteNonQuery();

        // Arrange
        var selection = SelectionBuilder
            .From("users")
            .Select("name")
            .Where(Where.IsNull("email"))
            .Build();
        
        // Act
        var query = _sqlBuilder.BuildQuery(selection);
        var results = _queryExecutor.ExecuteQuery(_connection, query).ToList();
        
        // Assert
        Assert.Equal("SELECT \"name\" FROM \"users\" WHERE \"email\" IS NULL", query.Sql);
        Assert.Empty(query.Parameters);
        Assert.Single(results);
        Assert.Equal("Dave", results[0]["name"]);
    }

    [Fact]
    public void SqlBuilder_NotExpression_NegatesCorrectly()
    {
        // Arrange
        var selection = SelectionBuilder
            .From("users")
            .SelectAll()
            .Where(Where.Not(Where.Equal("active", true)))
            .Build();
        
        // Act
        var query = _sqlBuilder.BuildQuery(selection);
        var results = _queryExecutor.ExecuteQuery(_connection, query).ToList();
        
        // Assert
        Assert.Equal("SELECT * FROM \"users\" WHERE NOT (\"active\" = @param_0)", query.Sql);
        Assert.Single(results);
        Assert.Equal("Charlie", results[0]["name"]); // Only inactive user
    }
}

public class SelectionGrammarFactoryIntegrationTests
{
    [Fact]
    public void SelectionGrammarFactory_ParseJson_CreatesValidSelection()
    {
        // Arrange
        var factory = new SelectionGrammarFactory();
        var json = """
        {
            "table": "users",
            "select": ["name", "email"],
            "where": {
                "and": [
                    {"col": "active", "op": "Equal", "val": true},
                    {"col": "age", "op": "GreaterThan", "val": 25}
                ]
            },
            "orderBy": [
                {"col": "name", "dir": "Ascending"}
            ],
            "limit": 10,
            "offset": 0
        }
        """;
        
        // Act & Assert (should not throw - WHERE parsing not fully implemented yet)
        var selection = factory.ParseJson(json);
        
        Assert.Equal("users", selection.Table);
        Assert.Equal(2, selection.Select.Count);
        Assert.Contains("name", selection.Select);
        Assert.Contains("email", selection.Select);
        Assert.Single(selection.OrderBy);
        Assert.Equal("name", selection.OrderBy[0].Column);
        Assert.Equal(10, selection.Limit);
        Assert.Equal(0, selection.Offset);
    }

    [Fact]
    public void SelectionGrammarFactory_CreateSimple_ReturnsBasicSelection()
    {
        // Arrange
        var factory = new SelectionGrammarFactory();
        
        // Act
        var selection = factory.CreateSimple("products", new[] { "name", "price" });
        
        // Assert
        Assert.Equal("products", selection.Table);
        Assert.Equal(2, selection.Select.Count);
        Assert.Contains("name", selection.Select);
        Assert.Contains("price", selection.Select);
        Assert.Null(selection.Where);
        Assert.Empty(selection.OrderBy);
    }

    [Fact]
    public void SelectionGrammarFactory_InvalidJson_ThrowsException()
    {
        // Arrange
        var factory = new SelectionGrammarFactory();
        var invalidJson = "{ invalid json }";
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => factory.ParseJson(invalidJson));
    }

    [Fact]
    public void SelectionGrammarFactory_EmptyTable_ThrowsException()
    {
        // Arrange
        var factory = new SelectionGrammarFactory();
        var json = """{"table": "", "select": ["*"]}""";
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => factory.ParseJson(json));
    }
}