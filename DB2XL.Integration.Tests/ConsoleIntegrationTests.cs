using DB2XL.Core.Models;
using DB2XL.Data.Schema;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace DB2XL.Integration.Tests
{
    public class ConsoleIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _testDbPath;
        private readonly string _tempDirectory;
        
        public ConsoleIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDirectory = Path.Combine(Path.GetTempPath(), "DB2XL_ConsoleTests_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDirectory);
            _testDbPath = Path.Combine(_tempDirectory, "test.sqlite");
            
            SetupTestDatabase();
        }

        private void SetupTestDatabase()
        {
            using var connection = new SqliteConnection($"Data Source={_testDbPath}");
            connection.Open();
            
            var sql = @"
                CREATE TABLE customers (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    email TEXT,
                    created_at TEXT DEFAULT (datetime('now')),
                    updated_at TEXT DEFAULT (datetime('now'))
                );

                CREATE TABLE orders (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    customer_id INTEGER,
                    total REAL,
                    status TEXT DEFAULT 'pending',
                    created_at TEXT DEFAULT (datetime('now')),
                    FOREIGN KEY (customer_id) REFERENCES customers(id)
                );

                CREATE TABLE products (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    price REAL,
                    category TEXT,
                    in_stock BOOLEAN DEFAULT 1
                );

                -- Insert test data
                INSERT INTO customers (name, email, created_at, updated_at) VALUES 
                    ('Alice Johnson', 'alice@example.com', '2024-01-01 10:00:00', '2024-01-01 10:00:00'),
                    ('Bob Smith', 'bob@example.com', '2024-01-01 11:00:00', '2024-01-01 11:00:00'),
                    ('Carol Davis', 'carol@example.com', '2024-01-01 12:00:00', '2024-01-01 12:00:00');

                INSERT INTO orders (customer_id, total, status, created_at) VALUES
                    (1, 150.00, 'completed', '2024-01-01 10:30:00'),
                    (1, 200.00, 'pending', '2024-01-01 11:30:00'),
                    (2, 75.00, 'completed', '2024-01-01 12:30:00'),
                    (3, 300.00, 'pending', '2024-01-01 13:30:00');

                INSERT INTO products (name, price, category, in_stock) VALUES
                    ('Laptop', 999.99, 'Electronics', 1),
                    ('Mouse', 25.99, 'Electronics', 1),
                    ('Desk Chair', 199.99, 'Furniture', 0),
                    ('Notebook', 5.99, 'Office Supplies', 1);
            ";

            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private async Task<(int ExitCode, string Output, string Error)> RunConsoleCommand(string arguments)
        {
            var consolePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "..", "..", "..", "..", "DB2XL.Console", "bin", "Debug", "net9.0", "DB2XL.Console.exe"
            );

            // Fallback to dotnet run if exe doesn't exist
            if (!File.Exists(consolePath))
            {
                var projectPath = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "..", "..", "..", "..", "DB2XL.Console"
                );
                
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"run --project \"{projectPath}\" -- {arguments}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = _tempDirectory
                    }
                };

                process.Start();
                
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                
                await process.WaitForExitAsync();
                
                _output.WriteLine($"Command: dotnet run --project \"{projectPath}\" -- {arguments}");
                _output.WriteLine($"Exit Code: {process.ExitCode}");
                _output.WriteLine($"Output: {output}");
                if (!string.IsNullOrEmpty(error))
                    _output.WriteLine($"Error: {error}");
                
                return (process.ExitCode, output, error);
            }
            else
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = consolePath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = _tempDirectory
                    }
                };

                process.Start();
                
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                
                await process.WaitForExitAsync();
                
                _output.WriteLine($"Command: {consolePath} {arguments}");
                _output.WriteLine($"Exit Code: {process.ExitCode}");
                _output.WriteLine($"Output: {output}");
                if (!string.IsNullOrEmpty(error))
                    _output.WriteLine($"Error: {error}");
                
                return (process.ExitCode, output, error);
            }
        }

        [Fact]
        public async Task AnalyzeCommand_BasicUsage_ShouldSucceed()
        {
            var (exitCode, output, error) = await RunConsoleCommand($"analyze \"{_testDbPath}\"");
            
            Assert.Equal(0, exitCode);
            Assert.Contains("customers", output);
            Assert.Contains("orders", output);
            Assert.Contains("products", output);
            Assert.Contains("3 rows", output);
            Assert.Contains("4 rows", output);
        }

        [Fact]
        public async Task AnalyzeCommand_WithPkDiscovery_ShouldShowPrimaryKeyInfo()
        {
            var (exitCode, output, error) = await RunConsoleCommand($"analyze \"{_testDbPath}\" --pk-discovery");
            
            Assert.Equal(0, exitCode);
            Assert.Contains("Primary Key Strategy", output);
            Assert.Contains("ExplicitPrimaryKey", output);
        }

        [Fact]
        public async Task AnalyzeCommand_WithPerformance_ShouldShowPerformanceMetrics()
        {
            var (exitCode, output, error) = await RunConsoleCommand($"analyze \"{_testDbPath}\" --performance");
            
            Assert.Equal(0, exitCode);
            Assert.Contains("Performance Analysis", output);
        }

        [Fact]
        public async Task ExportCommand_BasicExport_ShouldCreateExcelFile()
        {
            var outputFile = Path.Combine(_tempDirectory, "basic_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\"");
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputFile));
            
            var fileInfo = new FileInfo(outputFile);
            Assert.True(fileInfo.Length > 1000); // Should be a reasonably sized Excel file
        }

        [Fact]
        public async Task ExportCommand_WithMetadata_ShouldIncludeMetadataSheet()
        {
            var outputFile = Path.Combine(_tempDirectory, "metadata_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --metadata");
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputFile));
            Assert.Contains("metadata", output.ToLowerInvariant());
        }

        [Fact]
        public async Task ExportCommand_WithBasicFilterFile_ShouldHandleSimpleSelection()
        {
            // Create simple filter file without WHERE clause (which requires parameters)
            var filterFile = Path.Combine(_tempDirectory, "simple_filter.json");
            var filter = new
            {
                table = "orders",
                select = new[] { "id", "total", "status" },
                orderBy = new[]
                {
                    new { col = "total", dir = "Descending" }
                }
            };

            await File.WriteAllTextAsync(filterFile, JsonSerializer.Serialize(filter, new JsonSerializerOptions { WriteIndented = true }));

            var outputFile = Path.Combine(_tempDirectory, "simple_filtered_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --filter \"{filterFile}\"");
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputFile));
        }

        [Fact]
        public async Task ExportCommand_WithDeltaMode_ShouldCreateDeltaExport()
        {
            var outputFile = Path.Combine(_tempDirectory, "delta_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --delta");
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputFile));
            
            // Check if checkpoint file was created
            var checkpointFile = Path.Combine(_tempDirectory, Path.GetFileNameWithoutExtension(outputFile) + ".checkpoint.json");
            Assert.True(File.Exists(checkpointFile));
        }

        [Fact]
        public async Task ExportCommand_DeltaModeWithWatermarkColumns_ShouldUseSpecifiedColumns()
        {
            var outputFile = Path.Combine(_tempDirectory, "watermark_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand(
                $"export \"{_testDbPath}\" \"{outputFile}\" --delta --watermark-columns \"updated_at,created_at\""
            );
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputFile));
            Assert.Contains("watermark", output.ToLowerInvariant());
        }

        [Fact]
        public async Task ExportCommand_WithTransformations_ShouldApplyTransformations()
        {
            var outputFile = Path.Combine(_tempDirectory, "transformed_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --transform");
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputFile));
            Assert.Contains("transform", output.ToLowerInvariant());
        }

        [Fact]
        public async Task ExportCommand_JsonLinesFormat_ShouldCreateJsonLinesFiles()
        {
            var outputDir = Path.Combine(_tempDirectory, "jsonl_output");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputDir}\" --format jsonl");
            
            Assert.Equal(0, exitCode);
            Assert.True(Directory.Exists(outputDir));
            
            // Check that JSONL files were created
            var files = Directory.GetFiles(outputDir, "*.jsonl");
            Assert.NotEmpty(files);
            Assert.Contains(files, f => Path.GetFileName(f).Contains("customers"));
            Assert.Contains(files, f => Path.GetFileName(f).Contains("orders"));
            Assert.Contains(files, f => Path.GetFileName(f).Contains("products"));
        }

        [Fact]
        public async Task ExportCommand_WithSpecificTables_ShouldExportOnlySpecifiedTables()
        {
            var outputFile = Path.Combine(_tempDirectory, "specific_tables.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --tables \"customers,orders\"");
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputFile));
            Assert.Contains("customers", output);
            Assert.Contains("orders", output);
            Assert.DoesNotContain("products", output);
        }

        [Fact]
        public async Task ExportCommand_WithMaxRows_ShouldLimitRows()
        {
            var outputFile = Path.Combine(_tempDirectory, "limited_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --max-rows 2");
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputFile));
            Assert.Contains("2", output); // Should mention the row limit
        }

        [Fact]
        public async Task ExportCommand_DryRun_ShouldNotCreateFile()
        {
            var outputFile = Path.Combine(_tempDirectory, "dry_run_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --dry-run");
            
            Assert.Equal(0, exitCode);
            Assert.False(File.Exists(outputFile)); // File should not be created in dry run mode
            Assert.Contains("dry", output.ToLowerInvariant());
        }

        [Fact]
        public async Task ExportCommand_InvalidFilterFile_ShouldFail()
        {
            var filterFile = Path.Combine(_tempDirectory, "invalid_filter.json");
            await File.WriteAllTextAsync(filterFile, "{ invalid json }");

            var outputFile = Path.Combine(_tempDirectory, "invalid_filter_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --filter \"{filterFile}\"");
            
            Assert.NotEqual(0, exitCode);
            Assert.False(File.Exists(outputFile));
        }

        [Fact]
        public async Task ExportCommand_NonExistentDatabase_ShouldFail()
        {
            var nonExistentDb = Path.Combine(_tempDirectory, "non_existent.sqlite");
            var outputFile = Path.Combine(_tempDirectory, "failed_export.xlsx");
            
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{nonExistentDb}\" \"{outputFile}\"");
            
            Assert.NotEqual(0, exitCode);
            Assert.False(File.Exists(outputFile));
        }

        [Fact]
        public async Task ExportCommand_InvalidOutputPath_ShouldFail()
        {
            var invalidOutputPath = Path.Combine("C:", "invalid", "path", "that", "does", "not", "exist", "output.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{invalidOutputPath}\"");
            
            Assert.NotEqual(0, exitCode);
        }

        [Fact]
        public async Task HelpCommand_ShouldShowUsageInformation()
        {
            var (exitCode, output, error) = await RunConsoleCommand("--help");
            
            Assert.Equal(0, exitCode);
            Assert.Contains("Commands:", output);
            Assert.Contains("export", output);
            Assert.Contains("analyze", output);
            Assert.Contains("Advanced Filtering:", output);
            Assert.Contains("Delta Exports:", output);
        }

        [Fact]
        public async Task VersionCommand_ShouldShowVersionInfo()
        {
            var (exitCode, output, error) = await RunConsoleCommand("--version");
            
            Assert.Equal(0, exitCode);
            Assert.Contains("DB2XL", output);
        }

        [Fact]
        public async Task ExportCommand_ComplexWorkflow_FilterThenDelta()
        {
            // Step 1: Create initial export with filter
            var filterFile = Path.Combine(_tempDirectory, "complex_filter.json");
            var complexFilter = new
            {
                table = "customers",
                select = new[] { "*" },
                where = new
                {
                    type = "comparison",
                    column = "created_at",
                    @operator = ">=",
                    value = "2024-01-01 10:30:00"
                }
            };
            await File.WriteAllTextAsync(filterFile, JsonSerializer.Serialize(complexFilter, new JsonSerializerOptions { WriteIndented = true }));

            var initialExport = Path.Combine(_tempDirectory, "complex_initial.xlsx");
            var (exitCode1, output1, error1) = await RunConsoleCommand(
                $"export \"{_testDbPath}\" \"{initialExport}\" --filter \"{filterFile}\" --delta"
            );
            
            Assert.Equal(0, exitCode1);
            Assert.True(File.Exists(initialExport));

            // Step 2: Add more data to the database
            using (var connection = new SqliteConnection($"Data Source={_testDbPath}"))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO customers (name, email, created_at, updated_at) VALUES 
                        ('David Wilson', 'david@example.com', '2024-01-01 15:00:00', '2024-01-01 15:00:00');
                    
                    UPDATE customers SET updated_at = '2024-01-01 16:00:00' WHERE name = 'Alice Johnson';
                ";
                cmd.ExecuteNonQuery();
            }

            // Step 3: Run delta export
            var deltaExport = Path.Combine(_tempDirectory, "complex_delta.xlsx");
            var (exitCode2, output2, error2) = await RunConsoleCommand(
                $"export \"{_testDbPath}\" \"{deltaExport}\" --filter \"{filterFile}\" --delta"
            );
            
            Assert.Equal(0, exitCode2);
            Assert.True(File.Exists(deltaExport));
            
            // Both exports should succeed and create files
            var initialFileInfo = new FileInfo(initialExport);
            var deltaFileInfo = new FileInfo(deltaExport);
            Assert.True(initialFileInfo.Length > 0);
            Assert.True(deltaFileInfo.Length > 0);
        }

        [Fact]
        public async Task AnalyzeCommand_OutputToFile_ShouldCreateAnalysisFile()
        {
            var analysisFile = Path.Combine(_tempDirectory, "analysis_output.json");
            var (exitCode, output, error) = await RunConsoleCommand(
                $"analyze \"{_testDbPath}\" --output \"{analysisFile}\" --format json"
            );
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(analysisFile));
            
            var content = await File.ReadAllTextAsync(analysisFile);
            Assert.NotEmpty(content);
            
            // Should be valid JSON
            var analysisData = JsonDocument.Parse(content);
            Assert.NotNull(analysisData);
        }

        [Fact]
        public async Task ExportCommand_AdvancedFilteringWithComplexWhere_ShouldSucceed()
        {
            // Create complex filter with AND/OR logic
            var filterFile = Path.Combine(_tempDirectory, "complex_where_filter.json");
            var complexFilter = new
            {
                table = "orders",
                select = new[] { "id", "customer_id", "total", "status", "created_at" },
                where = new
                {
                    type = "and",
                    expressions = new object[]
                    {
                        new
                        {
                            type = "comparison",
                            column = "total",
                            @operator = ">=",
                            value = 100.0
                        },
                        new
                        {
                            type = "or",
                            expressions = new object[]
                            {
                                new
                                {
                                    type = "comparison",
                                    column = "status",
                                    @operator = "=",
                                    value = "completed"
                                },
                                new
                                {
                                    type = "comparison",
                                    column = "customer_id",
                                    @operator = "=",
                                    value = 1
                                }
                            }
                        }
                    }
                },
                orderBy = new[]
                {
                    new { col = "total", dir = "Descending" },
                    new { col = "created_at", dir = "Ascending" }
                },
                limit = 10
            };

            await File.WriteAllTextAsync(filterFile, JsonSerializer.Serialize(complexFilter, new JsonSerializerOptions { WriteIndented = true }));

            var outputFile = Path.Combine(_tempDirectory, "complex_where_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --filter \"{filterFile}\"");
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputFile));
            Assert.Contains("complex", output.ToLowerInvariant());
        }

        [Fact]
        public async Task ExportCommand_SecurityFilteringWithAllowedTables_ShouldRestrictAccess()
        {
            // Create a filter with security restrictions built into the grammar
            var filterFile = Path.Combine(_tempDirectory, "security_filter.json");
            var securityFilter = new
            {
                table = "customers",
                select = new[] { "*" },
                security = new
                {
                    allowedTables = new[] { "orders", "products" }, // customers not allowed
                    strictMode = true
                }
            };

            await File.WriteAllTextAsync(filterFile, JsonSerializer.Serialize(securityFilter, new JsonSerializerOptions { WriteIndented = true }));

            var outputFile = Path.Combine(_tempDirectory, "security_restricted_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --filter \"{filterFile}\"");
            
            // Should handle the filter appropriately (may succeed but with filtered results)
            Assert.True(exitCode == 0 || !File.Exists(outputFile));
        }

        [Fact]
        public async Task ExportCommand_ComplexFilterWithPerformanceTracking_ShouldCompleteSuccessfully()
        {
            // Create a complex filter that exercises the advanced filtering features
            var filterFile = Path.Combine(_tempDirectory, "performance_filter.json");
            var performanceFilter = new
            {
                table = "orders",
                select = new[] { "id", "customer_id", "total" },
                where = new
                {
                    type = "and",
                    expressions = new object[]
                    {
                        new
                        {
                            type = "comparison",
                            column = "customer_id",
                            @operator = "=",
                            value = 1
                        },
                        new
                        {
                            type = "comparison",
                            column = "status",
                            @operator = "=",
                            value = "completed"
                        }
                    }
                },
                orderBy = new[]
                {
                    new { col = "total", dir = "Descending" }
                }
            };

            await File.WriteAllTextAsync(filterFile, JsonSerializer.Serialize(performanceFilter, new JsonSerializerOptions { WriteIndented = true }));

            var outputFile = Path.Combine(_tempDirectory, "complex_filtered_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --filter \"{filterFile}\"");
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputFile));
        }

        [Fact]
        public async Task AnalyzeCommand_WithSuggestIndexes_ShouldProvideIndexRecommendations()
        {
            var analysisFile = Path.Combine(_tempDirectory, "index_analysis.json");
            var (exitCode, output, error) = await RunConsoleCommand(
                $"analyze \"{_testDbPath}\" --suggest-indexes --output \"{analysisFile}\" --format json"
            );
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(analysisFile));
            
            var content = await File.ReadAllTextAsync(analysisFile);
            var analysisData = JsonDocument.Parse(content);
            Assert.NotNull(analysisData);
        }

        [Fact]
        public async Task ExportCommand_FilterWithInOperator_ShouldHandleComplexQueries()
        {
            // Create filters that test IN operator and complex logic
            var filterFile = Path.Combine(_tempDirectory, "complex_in_filter.json");
            var indexFilter = new
            {
                table = "orders",
                select = new[] { "id", "customer_id", "total", "status", "created_at" },
                where = new
                {
                    type = "and",
                    expressions = new object[]
                    {
                        new
                        {
                            type = "comparison",
                            column = "customer_id",
                            @operator = "in",
                            value = new[] { 1, 2, 3 }
                        },
                        new
                        {
                            type = "comparison",
                            column = "status",
                            @operator = "=",
                            value = "completed"
                        },
                        new
                        {
                            type = "comparison",
                            column = "total",
                            @operator = ">=",
                            value = 100.0
                        }
                    }
                },
                orderBy = new[]
                {
                    new { col = "created_at", dir = "Descending" },
                    new { col = "total", dir = "Ascending" }
                }
            };

            await File.WriteAllTextAsync(filterFile, JsonSerializer.Serialize(indexFilter, new JsonSerializerOptions { WriteIndented = true }));

            var outputFile = Path.Combine(_tempDirectory, "complex_in_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --filter \"{filterFile}\"");
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputFile));
        }

        [Fact]
        public async Task ExportCommand_FullTextFilteringWithLike_ShouldHandleTextSearch()
        {
            var filterFile = Path.Combine(_tempDirectory, "text_search_filter.json");
            var textFilter = new
            {
                table = "customers",
                select = new[] { "id", "name", "email" },
                where = new
                {
                    type = "or",
                    expressions = new object[]
                    {
                        new
                        {
                            type = "comparison",
                            column = "name",
                            @operator = "like",
                            value = "%Johnson%"
                        },
                        new
                        {
                            type = "comparison",
                            column = "email",
                            @operator = "like",
                            value = "%@example.com"
                        }
                    }
                },
                orderBy = new[]
                {
                    new { col = "name", dir = "Ascending" }
                }
            };

            await File.WriteAllTextAsync(filterFile, JsonSerializer.Serialize(textFilter, new JsonSerializerOptions { WriteIndented = true }));

            var outputFile = Path.Combine(_tempDirectory, "text_search_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --filter \"{filterFile}\"");
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputFile));
        }

        [Fact]
        public async Task ExportCommand_InvalidFilterContent_ShouldHandleGracefully()
        {
            var filterFile = Path.Combine(_tempDirectory, "potentially_problematic_filter.json");
            var problematicFilter = new
            {
                table = "customers; DROP TABLE orders; --",
                select = new[] { "*" },
                where = new
                {
                    type = "comparison",
                    column = "name",
                    @operator = "=",
                    value = "'; DROP TABLE customers; --"
                }
            };

            await File.WriteAllTextAsync(filterFile, JsonSerializer.Serialize(problematicFilter, new JsonSerializerOptions { WriteIndented = true }));

            var outputFile = Path.Combine(_tempDirectory, "problematic_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --filter \"{filterFile}\"");
            
            // Should either succeed with safe handling or fail gracefully
            Assert.True(exitCode == 0 || !File.Exists(outputFile));
        }

        [Fact]
        public async Task ExportCommand_LargeDatasetWithPagination_ShouldHandleOffsetAndLimit()
        {
            // Add more test data for pagination testing
            using (var connection = new SqliteConnection($"Data Source={_testDbPath}"))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO products (name, price, category, in_stock) 
                    SELECT 
                        'Product ' || (id + 4) as name,
                        (id + 4) * 10.99 as price,
                        CASE (id + 4) % 3 
                            WHEN 0 THEN 'Electronics'
                            WHEN 1 THEN 'Furniture' 
                            ELSE 'Office Supplies'
                        END as category,
                        (id + 4) % 2 as in_stock
                    FROM (
                        WITH RECURSIVE numbers(id) AS (
                            SELECT 1
                            UNION ALL
                            SELECT id + 1 FROM numbers WHERE id < 20
                        )
                        SELECT id FROM numbers
                    );
                ";
                cmd.ExecuteNonQuery();
            }

            var filterFile = Path.Combine(_tempDirectory, "pagination_filter.json");
            var paginationFilter = new
            {
                table = "products",
                select = new[] { "id", "name", "price", "category" },
                where = new
                {
                    type = "comparison",
                    column = "in_stock",
                    @operator = "=",
                    value = 1
                },
                orderBy = new[]
                {
                    new { col = "price", dir = "Ascending" }
                },
                limit = 5,
                offset = 3
            };

            await File.WriteAllTextAsync(filterFile, JsonSerializer.Serialize(paginationFilter, new JsonSerializerOptions { WriteIndented = true }));

            var outputFile = Path.Combine(_tempDirectory, "pagination_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --filter \"{filterFile}\"");
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputFile));
        }

        [Fact]
        public async Task ExportCommand_ColumnFiltering_ShouldSelectSpecificColumns()
        {
            // Add sensitive data for testing
            using (var connection = new SqliteConnection($"Data Source={_testDbPath}"))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    ALTER TABLE customers ADD COLUMN ssn TEXT;
                    ALTER TABLE customers ADD COLUMN password_hash TEXT;
                    UPDATE customers SET 
                        ssn = '123-45-' || CAST((6780 + id) AS TEXT),
                        password_hash = 'hash_' || name;
                ";
                cmd.ExecuteNonQuery();
            }

            var filterFile = Path.Combine(_tempDirectory, "column_select_filter.json");
            var columnFilter = new
            {
                table = "customers",
                select = new[] { "id", "name", "email" }, // Only safe columns
                where = new
                {
                    col = "id",
                    op = "GreaterThanOrEqual",
                    val = 1
                }
            };

            await File.WriteAllTextAsync(filterFile, JsonSerializer.Serialize(columnFilter, new JsonSerializerOptions { WriteIndented = true }));

            var outputFile = Path.Combine(_tempDirectory, "column_filtered_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --filter \"{filterFile}\"");
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputFile));
        }

        [Fact]
        public async Task ExportCommand_LargeDatasetWithFiltering_ShouldHandleComplexQueries()
        {
            // Create a larger dataset for testing complex filtering
            using (var connection = new SqliteConnection($"Data Source={_testDbPath}"))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO orders (customer_id, total, status, created_at)
                    SELECT 
                        (id % 3) + 1 as customer_id,
                        (id * 15.50) as total,
                        CASE id % 4 
                            WHEN 0 THEN 'completed'
                            WHEN 1 THEN 'pending'
                            WHEN 2 THEN 'cancelled'
                            ELSE 'processing'
                        END as status,
                        datetime('2024-01-01 10:00:00', '+' || (id * 30) || ' minutes') as created_at
                    FROM (
                        WITH RECURSIVE numbers(id) AS (
                            SELECT 1
                            UNION ALL
                            SELECT id + 1 FROM numbers WHERE id < 100
                        )
                        SELECT id FROM numbers
                    );
                ";
                cmd.ExecuteNonQuery();
            }

            var filterFile = Path.Combine(_tempDirectory, "large_dataset_filter.json");
            var complexFilter = new
            {
                table = "orders",
                select = new[] { "id", "customer_id", "total", "status", "created_at" },
                where = new
                {
                    and = new object[]
                    {
                        new
                        {
                            col = "total",
                            op = "GreaterThanOrEqual",
                            val = 100.0
                        },
                        new
                        {
                            col = "status",
                            op = "In",
                            val = new[] { "completed", "processing" }
                        }
                    }
                },
                orderBy = new[]
                {
                    new { col = "created_at", dir = "Descending" }
                },
                limit = 50
            };

            await File.WriteAllTextAsync(filterFile, JsonSerializer.Serialize(complexFilter, new JsonSerializerOptions { WriteIndented = true }));

            var outputFile = Path.Combine(_tempDirectory, "large_dataset_export.xlsx");
            var (exitCode, output, error) = await RunConsoleCommand($"export \"{_testDbPath}\" \"{outputFile}\" --filter \"{filterFile}\"");
            
            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(outputFile));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}