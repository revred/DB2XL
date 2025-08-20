using DB2XL.Query;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace DB2XL.Query.Tests
{
    public class PerformanceTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly SqliteConnection _connection;
        private readonly SqlBuilder _sqlBuilder;
        private readonly PrimaryKeyDiscoveryService _pkService;

        public PerformanceTests(ITestOutputHelper output)
        {
            _output = output;
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            _sqlBuilder = new SqlBuilder();
            _pkService = new PrimaryKeyDiscoveryService();
            
            SetupLargeTestDatabase();
        }

        private void SetupLargeTestDatabase()
        {
            var sw = Stopwatch.StartNew();
            
            var sql = @"
                PRAGMA synchronous = OFF;
                PRAGMA journal_mode = MEMORY;
                PRAGMA cache_size = 100000;

                CREATE TABLE large_customers (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    email TEXT,
                    category TEXT,
                    credit_score INTEGER,
                    balance REAL,
                    created_at TEXT,
                    updated_at TEXT
                );

                CREATE TABLE large_orders (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    customer_id INTEGER,
                    amount REAL,
                    status TEXT,
                    priority INTEGER,
                    region TEXT,
                    created_at TEXT,
                    FOREIGN KEY (customer_id) REFERENCES large_customers(id)
                );

                CREATE TABLE large_products (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    category TEXT,
                    price REAL,
                    inventory_count INTEGER,
                    supplier_id INTEGER,
                    created_at TEXT
                );

                -- Create some indexes for performance testing
                CREATE INDEX idx_customers_category ON large_customers(category);
                CREATE INDEX idx_customers_credit_score ON large_customers(credit_score);
                CREATE INDEX idx_customers_created_at ON large_customers(created_at);
                CREATE INDEX idx_orders_customer_id ON large_orders(customer_id);
                CREATE INDEX idx_orders_amount ON large_orders(amount);
                CREATE INDEX idx_orders_status ON large_orders(status);
                CREATE INDEX idx_orders_created_at ON large_orders(created_at);
                CREATE INDEX idx_products_category ON large_products(category);
                CREATE INDEX idx_products_price ON large_products(price);
            ";

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();

            // Insert test data in batches for better performance
            var categories = new[] { "Premium", "Standard", "Basic", "VIP", "Corporate" };
            var statuses = new[] { "pending", "completed", "cancelled", "processing", "shipped" };
            var regions = new[] { "North", "South", "East", "West", "Central" };
            var productCategories = new[] { "Electronics", "Clothing", "Books", "Home", "Sports" };

            // Insert customers (10,000 records)
            using var transaction = _connection.BeginTransaction();
            for (int batch = 0; batch < 100; batch++)
            {
                var insertCustomers = @"
                    INSERT INTO large_customers (name, email, category, credit_score, balance, created_at, updated_at) 
                    VALUES ";
                
                var values = new List<string>();
                for (int i = 0; i < 100; i++)
                {
                    var id = batch * 100 + i;
                    var category = categories[id % categories.Length];
                    var creditScore = 300 + (id * 7) % 500;
                    var balance = (id * 13.7) % 10000;
                    var createdAt = $"2024-01-{(id % 28) + 1:D2} {(id % 24):D2}:{(id % 60):D2}:00";
                    var updatedAt = $"2024-01-{(id % 28) + 1:D2} {((id % 24) + 1) % 24:D2}:{(id % 60):D2}:00";
                    
                    values.Add($"('Customer_{id}', 'customer_{id}@test.com', '{category}', {creditScore}, {balance:F2}, '{createdAt}', '{updatedAt}')");
                }
                
                using var customerCmd = _connection.CreateCommand();
                customerCmd.CommandText = insertCustomers + string.Join(", ", values);
                customerCmd.ExecuteNonQuery();
            }

            // Insert orders (25,000 records)
            for (int batch = 0; batch < 250; batch++)
            {
                var insertOrders = @"
                    INSERT INTO large_orders (customer_id, amount, status, priority, region, created_at) 
                    VALUES ";
                
                var values = new List<string>();
                for (int i = 0; i < 100; i++)
                {
                    var id = batch * 100 + i;
                    var customerId = (id % 10000) + 1; // Reference existing customers
                    var amount = (id * 23.5) % 5000 + 10;
                    var status = statuses[id % statuses.Length];
                    var priority = (id % 5) + 1;
                    var region = regions[id % regions.Length];
                    var createdAt = $"2024-01-{(id % 28) + 1:D2} {(id % 24):D2}:{(id % 60):D2}:00";
                    
                    values.Add($"({customerId}, {amount:F2}, '{status}', {priority}, '{region}', '{createdAt}')");
                }
                
                using var orderCmd = _connection.CreateCommand();
                orderCmd.CommandText = insertOrders + string.Join(", ", values);
                orderCmd.ExecuteNonQuery();
            }

            // Insert products (5,000 records)
            for (int batch = 0; batch < 50; batch++)
            {
                var insertProducts = @"
                    INSERT INTO large_products (name, category, price, inventory_count, supplier_id, created_at) 
                    VALUES ";
                
                var values = new List<string>();
                for (int i = 0; i < 100; i++)
                {
                    var id = batch * 100 + i;
                    var category = productCategories[id % productCategories.Length];
                    var price = (id * 19.3) % 1000 + 5;
                    var inventory = (id * 31) % 500;
                    var supplierId = (id % 100) + 1;
                    var createdAt = $"2024-01-{(id % 28) + 1:D2} {(id % 24):D2}:{(id % 60):D2}:00";
                    
                    values.Add($"('Product_{id}', '{category}', {price:F2}, {inventory}, {supplierId}, '{createdAt}')");
                }
                
                using var productCmd = _connection.CreateCommand();
                productCmd.CommandText = insertProducts + string.Join(", ", values);
                productCmd.ExecuteNonQuery();
            }
            
            transaction.Commit();
            
            // Analyze tables for query optimization
            using var analyzeCmd = _connection.CreateCommand();
            analyzeCmd.CommandText = "ANALYZE;";
            analyzeCmd.ExecuteNonQuery();

            sw.Stop();
            _output.WriteLine($"Test database setup completed in {sw.ElapsedMilliseconds}ms");
            _output.WriteLine($"Customers: {GetRowCount("large_customers")}");
            _output.WriteLine($"Orders: {GetRowCount("large_orders")}");
            _output.WriteLine($"Products: {GetRowCount("large_products")}");
        }

        private int GetRowCount(string tableName)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {tableName}";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        [Fact]
        public void SelectionGrammar_SimpleQuery_ShouldPerformWithinTimeLimit()
        {
            var grammar = new SelectionGrammar
            {
                Table = "large_customers",
                Select = new[] { "*" },
                Where = new ComparisonExpression
                {
                    Column = "category",
                    Operator = ComparisonOperator.Equal,
                    Value = "Premium"
                },
                Limit = 1000
            };

            var sw = Stopwatch.StartNew();
            var result = _sqlBuilder.BuildQuery(grammar);
            sw.Stop();

            Assert.True(sw.ElapsedMilliseconds < 100, $"Query building took {sw.ElapsedMilliseconds}ms, expected < 100ms");

            // Execute the query
            sw.Restart();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = result.Sql;
            foreach (var param in result.Parameters)
            {
                cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
            }

            var records = new List<Dictionary<string, object?>>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var record = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    record[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                records.Add(record);
            }
            sw.Stop();

            _output.WriteLine($"Query execution time: {sw.ElapsedMilliseconds}ms for {records.Count} records");
            Assert.True(sw.ElapsedMilliseconds < 1000, $"Query execution took {sw.ElapsedMilliseconds}ms, expected < 1000ms");
            Assert.True(records.Count > 0);
            Assert.True(records.Count <= 1000);
        }

        [Fact]
        public void SelectionGrammar_ComplexAndQuery_ShouldPerformEfficiently()
        {
            var grammar = new SelectionGrammar
            {
                Table = "large_orders",
                Select = new[] { "id", "customer_id", "amount", "status" },
                Where = new AndExpression
                {
                    Expressions = new IWhereExpression[]
                    {
                        new ComparisonExpression
                        {
                            Column = "amount",
                            Operator = ComparisonOperator.GreaterThan,
                            Value = 1000.0
                        },
                        new ComparisonExpression
                        {
                            Column = "status",
                            Operator = ComparisonOperator.In,
                            Value = new[] { "completed", "processing" }
                        }
                    }
                },
                OrderBy = new[]
                {
                    new OrderByClause { Column = "amount", Direction = SortDirection.Descending }
                },
                Limit = 500
            };

            var sw = Stopwatch.StartNew();
            var result = _sqlBuilder.BuildQuery(grammar);
            var buildTime = sw.ElapsedMilliseconds;
            sw.Stop();

            _output.WriteLine($"Complex query build time: {buildTime}ms");
            Assert.True(buildTime < 50, $"Query building took {buildTime}ms, expected < 50ms");

            // Execute and measure performance
            sw.Restart();
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = result.Sql;
            foreach (var param in result.Parameters)
            {
                cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
            }

            var records = new List<Dictionary<string, object?>>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var record = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    record[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                records.Add(record);
            }
            var queryTime = sw.ElapsedMilliseconds;
            sw.Stop();

            _output.WriteLine($"Complex query execution time: {queryTime}ms for {records.Count} records");
            Assert.True(queryTime < 500, $"Query execution took {queryTime}ms, expected < 500ms");
            Assert.True(records.Count <= 500);

            // Verify results are ordered correctly
            for (int i = 1; i < records.Count; i++)
            {
                var prevAmount = Convert.ToDouble(records[i - 1]["amount"]);
                var currAmount = Convert.ToDouble(records[i]["amount"]);
                Assert.True(prevAmount >= currAmount, "Results should be ordered by amount descending");
            }
        }

        [Fact]
        public void PrimaryKeyDiscovery_LargeTable_ShouldPerformQuickly()
        {
            var sw = Stopwatch.StartNew();
            var pkInfo = _pkService.DiscoverPrimaryKey(_connection, "large_customers");
            sw.Stop();

            _output.WriteLine($"PK discovery time: {sw.ElapsedMilliseconds}ms");
            Assert.True(sw.ElapsedMilliseconds < 100, $"PK discovery took {sw.ElapsedMilliseconds}ms, expected < 100ms");
            
            Assert.Equal(PrimaryKeyStrategy.ExplicitPrimaryKey, pkInfo.Strategy);
            Assert.Single(pkInfo.Columns);
            Assert.Equal("id", pkInfo.Columns[0]);
            Assert.True(pkInfo.IsDeterministic);
        }

        [Fact]
        public void PrimaryKeyDiscovery_MultipleTablesInParallel_ShouldScaleWell()
        {
            var tables = new[] { "large_customers", "large_orders", "large_products" };
            
            var sw = Stopwatch.StartNew();
            
            var pkResults = new Dictionary<string, PrimaryKeyInfo>();
            
            // Sequential discovery
            foreach (var table in tables)
            {
                pkResults[table] = _pkService.DiscoverPrimaryKey(_connection, table);
            }
            
            var sequentialTime = sw.ElapsedMilliseconds;
            sw.Stop();

            _output.WriteLine($"Sequential PK discovery for {tables.Length} tables: {sequentialTime}ms");
            Assert.True(sequentialTime < 300, $"Sequential PK discovery took {sequentialTime}ms, expected < 300ms");

            // Verify all discoveries were successful
            Assert.All(pkResults, kvp =>
            {
                Assert.Equal(PrimaryKeyStrategy.ExplicitPrimaryKey, kvp.Value.Strategy);
                Assert.Single(kvp.Value.Columns);
                Assert.Equal("id", kvp.Value.Columns[0]);
            });
        }

        [Fact]
        public void SelectionGrammar_PaginationThroughLargeDataset_ShouldMaintainPerformance()
        {
            var pageSize = 100;
            var totalPages = 10;
            var allExecutionTimes = new List<long>();

            for (int page = 0; page < totalPages; page++)
            {
                var grammar = new SelectionGrammar
                {
                    Table = "large_customers",
                    Select = new[] { "id", "name", "category" },
                    Where = new ComparisonExpression
                    {
                        Column = "credit_score",
                        Operator = ComparisonOperator.GreaterThan,
                        Value = 400
                    },
                    OrderBy = new[] { new OrderByClause { Column = "id", Direction = SortDirection.Ascending } },
                    Limit = pageSize,
                    Offset = page * pageSize
                };

                var sw = Stopwatch.StartNew();
                var result = _sqlBuilder.BuildQuery(grammar);

                using var cmd = _connection.CreateCommand();
                cmd.CommandText = result.Sql;
                foreach (var param in result.Parameters)
                {
                    cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
                }

                var records = new List<Dictionary<string, object?>>();
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var record = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        record[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    records.Add(record);
                }
                sw.Stop();

                allExecutionTimes.Add(sw.ElapsedMilliseconds);
                Assert.Equal(pageSize, records.Count);
                
                _output.WriteLine($"Page {page + 1}: {sw.ElapsedMilliseconds}ms for {records.Count} records");
            }

            // Performance should be consistent across pages
            var avgTime = allExecutionTimes.Average();
            var maxTime = allExecutionTimes.Max();
            var minTime = allExecutionTimes.Min();

            _output.WriteLine($"Pagination performance - Min: {minTime}ms, Max: {maxTime}ms, Avg: {avgTime:F2}ms");
            
            Assert.True(maxTime < 500, $"Maximum page query time {maxTime}ms exceeded 500ms threshold");
            Assert.True(maxTime - minTime < 200, $"Performance variance {maxTime - minTime}ms too high (should be < 200ms)");
        }

        [Fact]
        public void SelectionGrammar_LikeQueryOnLargeDataset_ShouldPerformReasonably()
        {
            var grammar = new SelectionGrammar
            {
                Table = "large_customers",
                Select = new[] { "id", "name", "email" },
                Where = new OrExpression
                {
                    Expressions = new IWhereExpression[]
                    {
                        new ComparisonExpression
                        {
                            Column = "name",
                            Operator = ComparisonOperator.Like,
                            Value = "Customer_1%"
                        },
                        new ComparisonExpression
                        {
                            Column = "email",
                            Operator = ComparisonOperator.Like,
                            Value = "%customer_2%"
                        }
                    }
                },
                Limit = 1000
            };

            var sw = Stopwatch.StartNew();
            var result = _sqlBuilder.BuildQuery(grammar);

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = result.Sql;
            foreach (var param in result.Parameters)
            {
                cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
            }

            var records = new List<Dictionary<string, object?>>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var record = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    record[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                records.Add(record);
            }
            sw.Stop();

            _output.WriteLine($"LIKE query execution time: {sw.ElapsedMilliseconds}ms for {records.Count} records");
            
            // LIKE queries are generally slower, so allow more time
            Assert.True(sw.ElapsedMilliseconds < 2000, $"LIKE query took {sw.ElapsedMilliseconds}ms, expected < 2000ms");
            Assert.True(records.Count > 0);
        }

        [Fact]
        public void SqlBuilder_ConcurrentQueryBuilding_ShouldBeThreadSafe()
        {
            var grammar = new SelectionGrammar
            {
                Table = "large_orders",
                Select = new[] { "*" },
                Where = new ComparisonExpression
                {
                    Column = "status",
                    Operator = ComparisonOperator.Equal,
                    Value = "completed"
                }
            };

            var tasks = new List<Task<ParameterizedSql>>();
            var sw = Stopwatch.StartNew();

            // Create 10 concurrent query building tasks
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() => _sqlBuilder.BuildQuery(grammar)));
            }

            var results = Task.WhenAll(tasks).Result;
            sw.Stop();

            _output.WriteLine($"Concurrent query building (10 tasks): {sw.ElapsedMilliseconds}ms");
            Assert.True(sw.ElapsedMilliseconds < 1000, $"Concurrent query building took {sw.ElapsedMilliseconds}ms, expected < 1000ms");

            // All results should be identical
            var firstResult = results[0];
            Assert.All(results, result =>
            {
                Assert.Equal(firstResult.Sql, result.Sql);
                Assert.Equal(firstResult.Parameters.Count, result.Parameters.Count);
            });
        }

        [Theory]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(5000)]
        public void SelectionGrammar_VaryingLimits_ShouldScaleLinearly(int limit)
        {
            var grammar = new SelectionGrammar
            {
                Table = "large_orders",
                Select = new[] { "id", "amount", "status" },
                Where = new ComparisonExpression
                {
                    Column = "amount",
                    Operator = ComparisonOperator.GreaterThan,
                    Value = 100.0
                },
                OrderBy = new[] { new OrderByClause { Column = "id", Direction = SortDirection.Ascending } },
                Limit = limit
            };

            var sw = Stopwatch.StartNew();
            var result = _sqlBuilder.BuildQuery(grammar);

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = result.Sql;
            foreach (var param in result.Parameters)
            {
                cmd.Parameters.AddWithValue(param.Key, param.Value ?? DBNull.Value);
            }

            var records = new List<Dictionary<string, object?>>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var record = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    record[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                records.Add(record);
            }
            sw.Stop();

            _output.WriteLine($"Query with limit {limit}: {sw.ElapsedMilliseconds}ms for {records.Count} records");
            
            // Performance should scale reasonably with data size
            var expectedMaxTime = Math.Max(1000, limit / 2); // At least 1 second, or 1ms per 2 records
            Assert.True(sw.ElapsedMilliseconds < expectedMaxTime, 
                $"Query with limit {limit} took {sw.ElapsedMilliseconds}ms, expected < {expectedMaxTime}ms");
            
            Assert.Equal(Math.Min(limit, GetRowCount("large_orders")), records.Count);
        }

        [Fact]
        public void MemoryUsage_BuildingManyQueries_ShouldNotGrowExcessively()
        {
            var initialMemory = GC.GetTotalMemory(true);
            
            // Build many queries to test for memory leaks
            for (int i = 0; i < 1000; i++)
            {
                var grammar = new SelectionGrammar
                {
                    Table = "large_customers",
                    Select = new[] { "*" },
                    Where = new ComparisonExpression
                    {
                        Column = "id",
                        Operator = ComparisonOperator.Equal,
                        Value = i
                    }
                };

                var result = _sqlBuilder.BuildQuery(grammar);
                Assert.NotNull(result);
            }

            // Force garbage collection and measure memory
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var finalMemory = GC.GetTotalMemory(false);
            var memoryIncrease = finalMemory - initialMemory;
            
            _output.WriteLine($"Memory increase after 1000 queries: {memoryIncrease:N0} bytes ({memoryIncrease / 1024.0:F2} KB)");
            
            // Memory increase should be reasonable (less than 10MB for 1000 queries)
            Assert.True(memoryIncrease < 10_000_000, 
                $"Memory increased by {memoryIncrease:N0} bytes, expected < 10MB");
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }
}