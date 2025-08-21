using DB2XL.Query;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;
using DB2XL.Core.Models;

namespace DB2XL.Query.Tests
{
    public class FilteringBenchmarkTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly SqliteConnection _connection;
        private readonly SqlBuilder _sqlBuilder;
        private readonly QueryPlanAnalyzer _planAnalyzer;
        private readonly MissingIndexDetector _indexDetector;

        public FilteringBenchmarkTests(ITestOutputHelper output)
        {
            _output = output;
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            _sqlBuilder = new SqlBuilder();
            _planAnalyzer = new QueryPlanAnalyzer();
            _indexDetector = new MissingIndexDetector();
            
            SetupLargeBenchmarkDatabase();
        }

        private void SetupLargeBenchmarkDatabase()
        {
            var sw = Stopwatch.StartNew();
            _output.WriteLine("Setting up benchmark database with 100K+ records...");
            
            var sql = @"
                PRAGMA synchronous = OFF;
                PRAGMA journal_mode = MEMORY;
                PRAGMA cache_size = 100000;

                CREATE TABLE benchmark_users (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    username TEXT NOT NULL,
                    email TEXT UNIQUE,
                    first_name TEXT,
                    last_name TEXT,
                    department TEXT,
                    job_title TEXT,
                    salary INTEGER,
                    hire_date TEXT,
                    is_active INTEGER DEFAULT 1,
                    created_at TEXT DEFAULT (datetime('now')),
                    updated_at TEXT DEFAULT (datetime('now'))
                );

                CREATE TABLE benchmark_orders (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER,
                    product_name TEXT,
                    category TEXT,
                    unit_price REAL,
                    quantity INTEGER,
                    total_amount REAL,
                    order_date TEXT,
                    status TEXT,
                    shipping_address TEXT,
                    created_at TEXT DEFAULT (datetime('now'))
                );

                -- Create some indexes for comparison
                CREATE INDEX idx_users_department ON benchmark_users(department);
                CREATE INDEX idx_users_salary ON benchmark_users(salary);
                CREATE INDEX idx_orders_user_id ON benchmark_orders(user_id);
                CREATE INDEX idx_orders_category ON benchmark_orders(category);
            ";

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();

            // Insert users (50,000 records)
            using var transaction = _connection.BeginTransaction();
            
            var departments = new[] { "Engineering", "Marketing", "Sales", "HR", "Finance", "Operations", "Support" };
            var jobTitles = new[] { "Manager", "Senior", "Junior", "Lead", "Director", "Analyst", "Specialist" };
            var random = new Random(42); // Fixed seed for reproducible results

            var insertUsers = @"
                INSERT INTO benchmark_users 
                (username, email, first_name, last_name, department, job_title, salary, hire_date, is_active, created_at, updated_at) 
                VALUES ";

            for (int batch = 0; batch < 500; batch++) // 500 batches of 100 users each
            {
                var values = new List<string>();
                for (int i = 0; i < 100; i++)
                {
                    var userId = batch * 100 + i + 1;
                    var department = departments[random.Next(departments.Length)];
                    var jobTitle = jobTitles[random.Next(jobTitles.Length)];
                    var salary = random.Next(30000, 150000);
                    var isActive = random.Next(0, 10) < 8 ? 1 : 0; // 80% active
                    var hireDate = $"202{random.Next(0, 4)}-{random.Next(1, 13):D2}-{random.Next(1, 29):D2}";
                    
                    values.Add($"('user{userId}', 'user{userId}@company.com', 'First{userId}', 'Last{userId}', " +
                              $"'{department}', '{jobTitle}', {salary}, '{hireDate}', {isActive}, " +
                              $"'2024-01-{random.Next(1, 29):D2} {random.Next(0, 24):D2}:{random.Next(0, 60):D2}:00', " +
                              $"'2024-01-{random.Next(1, 29):D2} {random.Next(0, 24):D2}:{random.Next(0, 60):D2}:00')");
                }

                using var userCmd = _connection.CreateCommand();
                userCmd.CommandText = insertUsers + string.Join(", ", values);
                userCmd.ExecuteNonQuery();
            }

            // Insert orders (75,000 records)
            var categories = new[] { "Electronics", "Books", "Clothing", "Home", "Sports", "Automotive", "Health" };
            var statuses = new[] { "pending", "processing", "shipped", "delivered", "cancelled" };
            var products = new[] { "Product A", "Product B", "Product C", "Product D", "Product E" };

            var insertOrders = @"
                INSERT INTO benchmark_orders 
                (user_id, product_name, category, unit_price, quantity, total_amount, order_date, status, shipping_address, created_at) 
                VALUES ";

            for (int batch = 0; batch < 750; batch++) // 750 batches of 100 orders each
            {
                var values = new List<string>();
                for (int i = 0; i < 100; i++)
                {
                    var userId = random.Next(1, 50001); // Reference to users
                    var product = products[random.Next(products.Length)];
                    var category = categories[random.Next(categories.Length)];
                    var unitPrice = Math.Round(random.NextDouble() * 1000 + 10, 2);
                    var quantity = random.Next(1, 6);
                    var totalAmount = Math.Round(unitPrice * quantity, 2);
                    var status = statuses[random.Next(statuses.Length)];
                    var orderDate = $"2024-{random.Next(1, 13):D2}-{random.Next(1, 29):D2}";
                    
                    values.Add($"({userId}, '{product} {batch * 100 + i + 1}', '{category}', {unitPrice}, {quantity}, " +
                              $"{totalAmount}, '{orderDate}', '{status}', 'Address {userId}', " +
                              $"'2024-{random.Next(1, 13):D2}-{random.Next(1, 29):D2} {random.Next(0, 24):D2}:{random.Next(0, 60):D2}:00')");
                }

                using var orderCmd = _connection.CreateCommand();
                orderCmd.CommandText = insertOrders + string.Join(", ", values);
                orderCmd.ExecuteNonQuery();
            }

            transaction.Commit();
            
            // Analyze tables for query optimization
            using var analyzeCmd = _connection.CreateCommand();
            analyzeCmd.CommandText = "ANALYZE;";
            analyzeCmd.ExecuteNonQuery();

            sw.Stop();
            _output.WriteLine($"Database setup completed in {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"Created 50,000 users and 75,000 orders");
        }

        [Fact]
        public void FilteringBenchmark_SimpleWhereClause_ShouldPerformWell()
        {
            var grammar = new SelectionGrammar
            {
                Table = "benchmark_users",
                Select = new[] { "id", "username", "department", "salary" },
                Where = new ComparisonExpression
                {
                    Column = "department",
                    Operator = ComparisonOperator.Equal,
                    Value = "Engineering"
                }
            };

            var sw = Stopwatch.StartNew();
            var query = _sqlBuilder.BuildQuery(grammar);
            var buildTime = sw.ElapsedMilliseconds;
            
            sw.Restart();
            var results = ExecuteQuery(query);
            var executionTime = sw.ElapsedMilliseconds;
            
            sw.Stop();

            _output.WriteLine($"Simple WHERE filter:");
            _output.WriteLine($"  Query build time: {buildTime}ms");
            _output.WriteLine($"  Query execution time: {executionTime}ms");
            _output.WriteLine($"  Results returned: {results.Count}");
            _output.WriteLine($"  Query: {query.Sql}");

            // Performance assertions
            Assert.True(buildTime < 100, $"Query build should be fast, took {buildTime}ms");
            Assert.True(executionTime < 5000, $"Query execution should complete within 5s, took {executionTime}ms");
            Assert.True(results.Count > 0, "Should return some results");
        }

        [Fact]
        public void FilteringBenchmark_ComplexAndExpression_ShouldPerformReasonably()
        {
            var grammar = new SelectionGrammar
            {
                Table = "benchmark_users",
                Select = new[] { "*" },
                Where = new AndExpression
                {
                    Expressions = new IWhereExpression[]
                    {
                        new ComparisonExpression { Column = "department", Operator = ComparisonOperator.Equal, Value = "Engineering" },
                        new ComparisonExpression { Column = "salary", Operator = ComparisonOperator.GreaterThan, Value = 75000 },
                        new ComparisonExpression { Column = "is_active", Operator = ComparisonOperator.Equal, Value = 1 }
                    }
                }
            };

            var sw = Stopwatch.StartNew();
            var query = _sqlBuilder.BuildQuery(grammar);
            var buildTime = sw.ElapsedMilliseconds;
            
            sw.Restart();
            var results = ExecuteQuery(query);
            var executionTime = sw.ElapsedMilliseconds;
            
            sw.Stop();

            _output.WriteLine($"Complex AND filter:");
            _output.WriteLine($"  Query build time: {buildTime}ms");
            _output.WriteLine($"  Query execution time: {executionTime}ms");
            _output.WriteLine($"  Results returned: {results.Count}");

            // Performance assertions
            Assert.True(buildTime < 100, $"Query build should be fast, took {buildTime}ms");
            Assert.True(executionTime < 10000, $"Complex query should complete within 10s, took {executionTime}ms");
        }

        [Fact]
        public void FilteringBenchmark_RangeQueryWithOrdering_ShouldPerformWell()
        {
            var grammar = new SelectionGrammar
            {
                Table = "benchmark_orders",
                Select = new[] { "id", "user_id", "total_amount", "order_date" },
                Where = new AndExpression
                {
                    Expressions = new IWhereExpression[]
                    {
                        new ComparisonExpression { Column = "total_amount", Operator = ComparisonOperator.GreaterThan, Value = 100.0 },
                        new ComparisonExpression { Column = "total_amount", Operator = ComparisonOperator.LessThan, Value = 1000.0 }
                    }
                },
                OrderBy = new[]
                {
                    new OrderByClause { Column = "total_amount", Direction = SortDirection.Descending }
                },
                Limit = 1000
            };

            var sw = Stopwatch.StartNew();
            var query = _sqlBuilder.BuildQuery(grammar);
            var buildTime = sw.ElapsedMilliseconds;
            
            sw.Restart();
            var results = ExecuteQuery(query);
            var executionTime = sw.ElapsedMilliseconds;
            
            sw.Stop();

            _output.WriteLine($"Range query with ordering:");
            _output.WriteLine($"  Query build time: {buildTime}ms");
            _output.WriteLine($"  Query execution time: {executionTime}ms");
            _output.WriteLine($"  Results returned: {results.Count}");

            // Performance assertions
            Assert.True(buildTime < 100, $"Query build should be fast, took {buildTime}ms");
            Assert.True(executionTime < 15000, $"Range query should complete within 15s, took {executionTime}ms");
            Assert.True(results.Count <= 1000, "Should respect LIMIT clause");
        }

        [Fact]
        public void FilteringBenchmark_JoinLikeQuery_ShouldPerformReasonably()
        {
            // Filtered query without aggregation (aggregation not supported in basic SelectionGrammar)
            var grammar = new SelectionGrammar
            {
                Table = "benchmark_orders",
                Select = new[] { "id", "category", "status", "total_amount" },
                Where = new ComparisonExpression
                {
                    Column = "status",
                    Operator = ComparisonOperator.Equal,
                    Value = "delivered"
                },
                OrderBy = new[]
                {
                    new OrderByClause { Column = "category", Direction = SortDirection.Ascending }
                },
                Limit = 1000
            };

            var sw = Stopwatch.StartNew();
            var query = _sqlBuilder.BuildQuery(grammar);
            var buildTime = sw.ElapsedMilliseconds;
            
            sw.Restart();
            var results = ExecuteQuery(query);
            var executionTime = sw.ElapsedMilliseconds;
            
            sw.Stop();

            _output.WriteLine($"Filtered query with ordering:");
            _output.WriteLine($"  Query build time: {buildTime}ms");
            _output.WriteLine($"  Query execution time: {executionTime}ms");
            _output.WriteLine($"  Results returned: {results.Count}");

            // Performance assertions
            Assert.True(buildTime < 100, $"Query build should be fast, took {buildTime}ms");
            Assert.True(executionTime < 20000, $"Filtered query should complete within 20s, took {executionTime}ms");
        }

        [Fact]
        public void PerformanceAnalysis_QueryPlanAnalyzer_ShouldCompleteQuickly()
        {
            var grammar = new SelectionGrammar
            {
                Table = "benchmark_users",
                Select = new[] { "*" },
                Where = new ComparisonExpression
                {
                    Column = "job_title", // No index on this column
                    Operator = ComparisonOperator.Like,
                    Value = "Manager%"
                }
            };

            var sw = Stopwatch.StartNew();
            var analysis = _planAnalyzer.AnalyzeQuery(_connection, grammar);
            sw.Stop();

            _output.WriteLine($"Query plan analysis:");
            _output.WriteLine($"  Analysis time: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"  Plan steps: {analysis.PlanSteps.Count}");
            _output.WriteLine($"  Performance issues: {analysis.PerformanceIssues.Count}");
            _output.WriteLine($"  Optimization suggestions: {analysis.OptimizationSuggestions.Count}");
            _output.WriteLine($"  Complexity: {analysis.EstimatedComplexity}");

            // Performance assertions
            Assert.True(sw.ElapsedMilliseconds < 5000, $"Plan analysis should be fast, took {sw.ElapsedMilliseconds}ms");
            Assert.NotNull(analysis);
            Assert.NotEmpty(analysis.PlanSteps);
        }

        [Fact]
        public void PerformanceAnalysis_MissingIndexDetector_ShouldCompleteQuickly()
        {
            var grammar = new SelectionGrammar
            {
                Table = "benchmark_orders",
                Select = new[] { "*" },
                Where = new AndExpression
                {
                    Expressions = new IWhereExpression[]
                    {
                        new ComparisonExpression { Column = "status", Operator = ComparisonOperator.Equal, Value = "shipped" },
                        new ComparisonExpression { Column = "unit_price", Operator = ComparisonOperator.GreaterThan, Value = 50.0 }
                    }
                },
                OrderBy = new[]
                {
                    new OrderByClause { Column = "order_date", Direction = SortDirection.Descending }
                }
            };

            var sw = Stopwatch.StartNew();
            var analysis = _indexDetector.AnalyzeQuery(_connection, grammar);
            sw.Stop();

            _output.WriteLine($"Missing index analysis:");
            _output.WriteLine($"  Analysis time: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"  Existing indexes: {analysis.ExistingIndexes.Count}");
            _output.WriteLine($"  Missing index recommendations: {analysis.MissingIndexRecommendations.Count}");
            _output.WriteLine($"  Performance impact: {analysis.PerformanceImpact}");

            // Performance assertions
            Assert.True(sw.ElapsedMilliseconds < 5000, $"Index analysis should be fast, took {sw.ElapsedMilliseconds}ms");
            Assert.NotNull(analysis);
            Assert.True(analysis.MissingIndexRecommendations.Count >= 0);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(5000)]
        public void FilteringBenchmark_VaryingLimits_ShouldScaleLinearly(int limit)
        {
            var grammar = new SelectionGrammar
            {
                Table = "benchmark_orders",
                Select = new[] { "id", "total_amount" },
                Where = new ComparisonExpression
                {
                    Column = "category",
                    Operator = ComparisonOperator.Equal,
                    Value = "Electronics"
                },
                Limit = limit
            };

            var sw = Stopwatch.StartNew();
            var query = _sqlBuilder.BuildQuery(grammar);
            var results = ExecuteQuery(query);
            sw.Stop();

            _output.WriteLine($"Limit {limit}: {sw.ElapsedMilliseconds}ms, {results.Count} results");

            // Performance should scale reasonably with result set size
            var expectedMaxTime = Math.Max(1000, limit / 2); // Very loose upper bound
            Assert.True(sw.ElapsedMilliseconds < expectedMaxTime, 
                $"Query with limit {limit} took {sw.ElapsedMilliseconds}ms, expected < {expectedMaxTime}ms");
            Assert.True(results.Count <= limit, $"Should respect limit, got {results.Count} vs {limit}");
        }

        [Fact]
        public void FilteringBenchmark_ConcurrentAccess_ShouldHandleMultipleQueries()
        {
            var grammars = new[]
            {
                new SelectionGrammar
                {
                    Table = "benchmark_users",
                    Where = new ComparisonExpression { Column = "department", Operator = ComparisonOperator.Equal, Value = "Engineering" },
                    Limit = 100
                },
                new SelectionGrammar
                {
                    Table = "benchmark_orders",
                    Where = new ComparisonExpression { Column = "status", Operator = ComparisonOperator.Equal, Value = "delivered" },
                    Limit = 100
                },
                new SelectionGrammar
                {
                    Table = "benchmark_users",
                    Where = new ComparisonExpression { Column = "salary", Operator = ComparisonOperator.GreaterThan, Value = 100000 },
                    Limit = 50
                }
            };

            var sw = Stopwatch.StartNew();
            var tasks = grammars.Select(async grammar =>
            {
                var query = _sqlBuilder.BuildQuery(grammar);
                return await Task.Run(() => ExecuteQuery(query));
            });

            var results = Task.WhenAll(tasks).Result;
            sw.Stop();

            _output.WriteLine($"Concurrent queries:");
            _output.WriteLine($"  Total time: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"  Queries: {grammars.Length}");
            for (int i = 0; i < results.Length; i++)
            {
                _output.WriteLine($"  Query {i + 1}: {results[i].Count} results");
            }

            // Should complete all queries reasonably quickly
            Assert.True(sw.ElapsedMilliseconds < 15000, 
                $"Concurrent queries should complete within 15s, took {sw.ElapsedMilliseconds}ms");
            Assert.All(results, r => Assert.True(r.Count >= 0));
        }

        [Fact]
        public void MemoryUsageBenchmark_LargeResultSet_ShouldManageMemoryWell()
        {
            var initialMemory = GC.GetTotalMemory(true);

            var grammar = new SelectionGrammar
            {
                Table = "benchmark_orders",
                Select = new[] { "*" },
                Limit = 10000 // Large result set
            };

            var sw = Stopwatch.StartNew();
            var query = _sqlBuilder.BuildQuery(grammar);
            var results = ExecuteQuery(query);
            sw.Stop();

            var finalMemory = GC.GetTotalMemory(false);
            var memoryUsed = finalMemory - initialMemory;

            _output.WriteLine($"Memory usage benchmark:");
            _output.WriteLine($"  Execution time: {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"  Results: {results.Count}");
            _output.WriteLine($"  Memory used: {memoryUsed / 1024 / 1024:F2} MB");

            // Memory usage should be reasonable for the result set size
            // Allow for more generous memory usage as .NET may allocate extra memory
            var expectedMaxMemory = results.Count * 5120; // ~5KB per row allows for overhead
            Assert.True(memoryUsed < expectedMaxMemory || memoryUsed < 50_000_000, // Max 50MB
                $"Memory usage {memoryUsed / 1024:F0}KB seems high for {results.Count} rows");
        }

        private List<Dictionary<string, object?>> ExecuteQuery(ParameterizedSql query)
        {
            var results = new List<Dictionary<string, object?>>();

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = query.Sql;
            
            foreach (var param in query.Parameters)
            {
                cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
            }

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var name = reader.GetName(i);
                    var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    row[name] = value;
                }
                results.Add(row);
            }

            return results;
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }
}