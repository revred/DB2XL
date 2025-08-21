using Microsoft.Data.Sqlite;
using DB2XL.Data.Analysis;
using DB2XL.Core.Models;
using Xunit;

namespace DB2XL.Data.Tests.Analysis;

public class QueryExecutionPlanAnalyzerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly QueryExecutionPlanAnalyzer _analyzer;

    public QueryExecutionPlanAnalyzerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _analyzer = new QueryExecutionPlanAnalyzer();
        SetupTestDatabase();
    }

    [Fact]
    public async Task AnalyzeQueryAsync_SimpleSelect_ShouldReturnBasicPlan()
    {
        // Arrange
        var query = "SELECT * FROM customers WHERE id = 1";

        // Act
        var plan = await _analyzer.AnalyzeQueryAsync(_connection, query);

        // Assert
        Assert.NotNull(plan);
        Assert.Equal(query, plan.Query);
        Assert.NotEmpty(plan.Steps);
        Assert.NotNull(plan.Metrics);
    }

    [Fact]
    public async Task AnalyzeQueryAsync_TableScan_ShouldIdentifyPerformanceIssue()
    {
        // Arrange
        var query = "SELECT * FROM customers WHERE name = 'John'"; // No index on name

        // Act
        var plan = await _analyzer.AnalyzeQueryAsync(_connection, query);

        // Assert
        Assert.Contains(plan.Steps, step => step.Performance.IsTableScan);
        Assert.Contains(plan.Issues, issue => issue.Type == PerformanceIssueType.TableScan);
        Assert.True(plan.Metrics.Grade == PerformanceGrade.Poor || plan.Metrics.Grade == PerformanceGrade.Terrible);
    }

    [Fact]
    public async Task AnalyzeQueryAsync_IndexedQuery_ShouldShowGoodPerformance()
    {
        // Arrange
        var query = "SELECT * FROM customers WHERE id = 1"; // Primary key lookup

        // Act
        var plan = await _analyzer.AnalyzeQueryAsync(_connection, query);

        // Assert
        Assert.Contains(plan.Steps, step => 
            step.IndexUsages.Any(usage => usage.UsageType == IndexUsageType.PrimaryKey));
        Assert.True(plan.Metrics.Grade == PerformanceGrade.Excellent || plan.Metrics.Grade == PerformanceGrade.Good);
        Assert.True(plan.Metrics.ComplexityScore < 50);
    }

    [Fact]
    public async Task AnalyzeQueryAsync_JoinQuery_ShouldDetectJoinOperations()
    {
        // Arrange
        var query = @"
            SELECT c.name, o.total 
            FROM customers c 
            JOIN orders o ON c.id = o.customer_id";

        // Act
        var plan = await _analyzer.AnalyzeQueryAsync(_connection, query);

        // Assert - SQLite may optimize simple JOINs to scans, so check for multiple tables being processed
        Assert.True(plan.Steps.Count > 0);
        var allTables = plan.Steps.SelectMany(s => s.Tables).Distinct().ToList();
        Assert.True(allTables.Count >= 2, "JOIN query should involve multiple tables");
    }

    [Fact]
    public async Task AnalyzeQueryAsync_ComplexQuery_ShouldProvideRecommendations()
    {
        // Arrange
        var query = @"
            SELECT c.name, COUNT(*) as order_count
            FROM customers c
            LEFT JOIN orders o ON c.id = o.customer_id
            WHERE c.email LIKE '%@gmail.com'
            GROUP BY c.id, c.name
            ORDER BY order_count DESC";

        // Act
        var plan = await _analyzer.AnalyzeQueryAsync(_connection, query);

        // Assert
        Assert.NotEmpty(plan.Recommendations);
        Assert.Contains(plan.Steps, step => step.Operation == ExecutionOperation.Sort || 
                                          step.Operation == ExecutionOperation.Group);
    }

    [Fact]
    public async Task AnalyzeQueryAsync_SubqueryQuery_ShouldIdentifySubqueries()
    {
        // Arrange
        var query = @"
            SELECT * FROM customers 
            WHERE id IN (SELECT customer_id FROM orders WHERE total > 100)";

        // Act
        var plan = await _analyzer.AnalyzeQueryAsync(_connection, query);

        // Assert
        Assert.Contains(plan.Steps, step => step.Operation == ExecutionOperation.Subquery ||
                                          step.Detail.Contains("SUBQUERY", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnalyzeQueryAsync_MultipleTableScans_ShouldIdentifyCriticalIssues()
    {
        // Arrange
        var query = @"
            SELECT c.name, o.total, p.name as product_name
            FROM customers c, orders o, order_items oi, products p
            WHERE c.email LIKE '%test%'
            AND o.total > 50
            AND p.name LIKE '%widget%'";

        // Act
        var plan = await _analyzer.AnalyzeQueryAsync(_connection, query);

        // Assert
        Assert.True(plan.Metrics.TableScanCount > 1);
        Assert.Contains(plan.Issues, issue => issue.Severity == IssueSeverity.Critical);
        Assert.Equal(PerformanceGrade.Terrible, plan.Metrics.Grade);
    }

    [Theory]
    [InlineData("SELECT COUNT(*) FROM customers", ExecutionOperation.Scan)] // COUNT(*) shows as SCAN in SQLite execution plans
    [InlineData("SELECT * FROM customers ORDER BY name", ExecutionOperation.Sort)]
    [InlineData("SELECT DISTINCT name FROM customers", ExecutionOperation.Group)]
    public async Task AnalyzeQueryAsync_SpecificOperations_ShouldDetectCorrectOperationType(
        string query, ExecutionOperation expectedOperation)
    {
        // Act
        var plan = await _analyzer.AnalyzeQueryAsync(_connection, query);

        // Assert
        Assert.Contains(plan.Steps, step => step.Operation == expectedOperation);
    }

    [Fact]
    public async Task AnalyzeQueryAsync_LargeResultSet_ShouldEstimateHighRowCount()
    {
        // Arrange
        var query = "SELECT * FROM orders"; // Should process all orders

        // Act
        var plan = await _analyzer.AnalyzeQueryAsync(_connection, query);

        // Assert
        Assert.True(plan.Metrics.EstimatedRowsProcessed > 0);
        Assert.Contains(plan.Steps, step => step.Performance.EstimatedRows > 0);
    }

    [Fact]
    public async Task AnalyzeQueryAsync_PerformanceGrading_ShouldAssignCorrectGrades()
    {
        // Arrange & Act
        var simpleIndexedQuery = await _analyzer.AnalyzeQueryAsync(_connection, 
            "SELECT * FROM customers WHERE id = 1");
        
        var tableScanQuery = await _analyzer.AnalyzeQueryAsync(_connection, 
            "SELECT * FROM customers WHERE name = 'test'");

        // Assert
        Assert.True(simpleIndexedQuery.Metrics.Grade <= PerformanceGrade.Good);
        Assert.True(tableScanQuery.Metrics.Grade >= PerformanceGrade.Poor);
    }

    [Fact]
    public async Task AnalyzeQueryAsync_IndexUsageDetection_ShouldIdentifyCorrectIndexTypes()
    {
        // Arrange
        // Create a custom index for testing
        await _connection.ExecuteNonQueryAsync("CREATE INDEX idx_customer_email ON customers (email)");
        
        var query = "SELECT * FROM customers WHERE email = 'test@example.com'";

        // Act
        var plan = await _analyzer.AnalyzeQueryAsync(_connection, query);

        // Assert
        var indexUsages = plan.Steps.SelectMany(s => s.IndexUsages).ToList();
        Assert.Contains(indexUsages, usage => 
            usage.IndexName.Contains("email", StringComparison.OrdinalIgnoreCase) &&
            usage.UsageType == IndexUsageType.NonUniqueIndex);
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
            (3, 'Bob Wilson', 'bob@gmail.com', '2023-01-03', 'inactive')");

        _connection.ExecuteNonQuery(@"
            INSERT INTO products (id, name, price, category, description) VALUES 
            (1, 'Widget A', 10.99, 'widgets', 'A great widget'),
            (2, 'Widget B', 15.50, 'widgets', 'An even better widget'),
            (3, 'Gadget X', 25.00, 'gadgets', 'Useful gadget'),
            (4, 'Tool Y', 5.99, 'tools', 'Handy tool')");

        _connection.ExecuteNonQuery(@"
            INSERT INTO orders (id, customer_id, order_date, total, status) VALUES 
            (1, 1, '2023-01-01', 26.49, 'completed'),
            (2, 1, '2023-01-05', 25.00, 'completed'),
            (3, 2, '2023-01-02', 15.50, 'completed'),
            (4, 3, '2023-01-03', 5.99, 'pending')");

        _connection.ExecuteNonQuery(@"
            INSERT INTO order_items (id, order_id, product_id, quantity, unit_price) VALUES 
            (1, 1, 1, 2, 10.99),
            (2, 1, 2, 1, 15.50),
            (3, 2, 3, 1, 25.00),
            (4, 3, 2, 1, 15.50),
            (5, 4, 4, 1, 5.99)");
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}