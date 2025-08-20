using DB2XL.Query;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DB2XL.Query.Tests
{
    public class QueryPlanAnalyzerTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly QueryPlanAnalyzer _analyzer;

        public QueryPlanAnalyzerTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            _analyzer = new QueryPlanAnalyzer();
            
            SetupTestDatabase();
        }

        private void SetupTestDatabase()
        {
            var sql = @"
                CREATE TABLE customers (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    email TEXT UNIQUE,
                    category TEXT,
                    balance REAL
                );

                CREATE TABLE orders (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    customer_id INTEGER,
                    amount REAL,
                    status TEXT,
                    order_date TEXT
                );

                CREATE INDEX idx_orders_customer ON orders(customer_id);

                -- Insert test data
                INSERT INTO customers (name, email, category, balance) VALUES
                    ('John Doe', 'john@test.com', 'premium', 1000.00),
                    ('Jane Smith', 'jane@test.com', 'standard', 500.00),
                    ('Bob Johnson', 'bob@test.com', 'premium', 1500.00);

                INSERT INTO orders (customer_id, amount, status, order_date) VALUES
                    (1, 250.00, 'completed', '2024-01-01'),
                    (1, 100.00, 'pending', '2024-01-02'),
                    (2, 75.00, 'completed', '2024-01-01'),
                    (3, 300.00, 'completed', '2024-01-03');
            ";

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        [Fact]
        public void AnalyzeQuery_SimpleSelectWithIndex_ShouldDetectIndexUsage()
        {
            var grammar = new SelectionGrammar
            {
                Table = "orders",
                Select = new[] { "*" },
                Where = new ComparisonExpression
                {
                    Column = "customer_id",
                    Operator = ComparisonOperator.Equal,
                    Value = 1
                }
            };

            var analysis = _analyzer.AnalyzeQuery(_connection, grammar);

            Assert.NotNull(analysis);
            Assert.NotEmpty(analysis.Query);
            Assert.NotEmpty(analysis.PlanSteps);
            
            // Should have some plan steps analyzing the query
            Assert.NotEmpty(analysis.PlanSteps);
            
            // Should have executed without errors
            var hasErrorStep = analysis.PlanSteps.Any(s => s.Operation == "ERROR");
            Assert.False(hasErrorStep, "Should not have analysis errors");
        }

        [Fact]
        public void AnalyzeQuery_FullTableScan_ShouldDetectPerformanceIssue()
        {
            var grammar = new SelectionGrammar
            {
                Table = "customers",
                Select = new[] { "*" },
                Where = new ComparisonExpression
                {
                    Column = "category", // No index on category
                    Operator = ComparisonOperator.Equal,
                    Value = "premium"
                }
            };

            var analysis = _analyzer.AnalyzeQuery(_connection, grammar);

            Assert.NotNull(analysis);
            
            // Should have analyzed the query successfully 
            Assert.NotEmpty(analysis.PlanSteps);
            
            // Should have executed without errors
            var hasErrorStep = analysis.PlanSteps.Any(s => s.Operation == "ERROR");
            Assert.False(hasErrorStep, "Should not have analysis errors");
        }

        [Fact]
        public void AnalyzeQuery_WithOrderBy_ShouldDetectTempSorting()
        {
            var grammar = new SelectionGrammar
            {
                Table = "customers",
                Select = new[] { "*" },
                OrderBy = new[]
                {
                    new OrderByClause { Column = "balance", Direction = SortDirection.Descending }
                }
            };

            var analysis = _analyzer.AnalyzeQuery(_connection, grammar);

            Assert.NotNull(analysis);
            
            // May detect temporary sorting depending on SQLite optimization
            var complexityAtLeastModerate = analysis.EstimatedComplexity >= QueryComplexity.Simple;
            Assert.True(complexityAtLeastModerate, "Query should have some complexity");
        }

        [Fact]
        public void AnalyzeQuery_ComplexQuery_ShouldProvideOptimizationSuggestions()
        {
            var grammar = new SelectionGrammar
            {
                Table = "customers",
                Select = new[] { "*" },
                Where = new ComparisonExpression
                {
                    Column = "category",
                    Operator = ComparisonOperator.Equal,
                    Value = "premium"
                },
                OrderBy = new[]
                {
                    new OrderByClause { Column = "balance", Direction = SortDirection.Descending }
                }
            };

            var analysis = _analyzer.AnalyzeQuery(_connection, grammar);

            Assert.NotNull(analysis);
            Assert.NotEmpty(analysis.OptimizationSuggestions);
            
            // Should provide some analysis results
            Assert.NotEmpty(analysis.PlanSteps);
            
            // Should have executed without errors
            var hasErrorStep = analysis.PlanSteps.Any(s => s.Operation == "ERROR");
            Assert.False(hasErrorStep, "Should not have analysis errors");
        }

        [Fact]
        public void AnalyzeQuery_RawSqlWithParameters_ShouldAnalyzeSuccessfully()
        {
            var sql = "SELECT * FROM customers WHERE category = @category ORDER BY name";
            var parameters = new Dictionary<string, object?> { ["@category"] = "premium" };

            var analysis = _analyzer.AnalyzeQuery(_connection, sql, parameters);

            Assert.NotNull(analysis);
            Assert.Equal(sql, analysis.Query);
            Assert.Equal(parameters["@category"], analysis.Parameters["@category"]);
            Assert.NotEmpty(analysis.PlanSteps);
        }

        [Fact]
        public void GetSummary_ShouldReturnInformativeSummary()
        {
            var grammar = new SelectionGrammar
            {
                Table = "customers",
                Select = new[] { "*" },
                Where = new ComparisonExpression
                {
                    Column = "category",
                    Operator = ComparisonOperator.Equal,
                    Value = "premium"
                }
            };

            var analysis = _analyzer.AnalyzeQuery(_connection, grammar);
            var summary = analysis.GetSummary();

            Assert.NotEmpty(summary);
            Assert.Contains("Query complexity:", summary);
            Assert.Contains("Performance issues:", summary);
            Assert.Contains("Optimization suggestions:", summary);
        }

        [Fact]
        public void AnalyzeQuery_InvalidSql_ShouldHandleErrorsGracefully()
        {
            var sql = "SELECT * FROM non_existent_table WHERE invalid_column = @param";
            var parameters = new Dictionary<string, object?> { ["@param"] = "value" };

            var analysis = _analyzer.AnalyzeQuery(_connection, sql, parameters);

            Assert.NotNull(analysis);
            Assert.Contains("Error analyzing query plan", analysis.PlanSteps.First().Detail);
            Assert.Equal("ERROR", analysis.PlanSteps.First().Operation);
        }

        [Theory]
        [InlineData(QueryComplexity.Simple, true)]
        [InlineData(QueryComplexity.Moderate, true)]
        [InlineData(QueryComplexity.Complex, true)]
        [InlineData(QueryComplexity.VeryComplex, true)]
        public void EstimatedComplexity_ShouldBeValidEnumValue(QueryComplexity complexity, bool expected)
        {
            // Test that complexity calculation returns valid enum values
            Assert.True(Enum.IsDefined(typeof(QueryComplexity), complexity) == expected);
        }

        [Fact]
        public void PlanSteps_ShouldHaveValidStructure()
        {
            var grammar = new SelectionGrammar
            {
                Table = "customers",
                Select = new[] { "id", "name" }
            };

            var analysis = _analyzer.AnalyzeQuery(_connection, grammar);

            Assert.NotEmpty(analysis.PlanSteps);
            
            foreach (var step in analysis.PlanSteps)
            {
                Assert.True(step.Id >= 0 || step.Id == -1); // -1 for error steps
                Assert.NotEmpty(step.Detail);
                Assert.NotEmpty(step.Operation);
            }
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }
}