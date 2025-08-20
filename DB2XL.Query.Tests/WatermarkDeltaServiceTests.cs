using DB2XL.DeltaExport;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DB2XL.Query.Tests
{
    public class WatermarkDeltaServiceTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly WatermarkDeltaService _service;

        public WatermarkDeltaServiceTests()
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
            _service = new WatermarkDeltaService();
            
            SetupTestDatabase();
        }

        private void SetupTestDatabase()
        {
            var sql = @"
                CREATE TABLE users (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    email TEXT,
                    created_at TEXT DEFAULT (datetime('now')),
                    updated_at TEXT DEFAULT (datetime('now'))
                );

                CREATE TABLE orders (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER,
                    amount REAL,
                    status TEXT DEFAULT 'pending',
                    created_at TEXT DEFAULT (datetime('now'))
                );

                CREATE TABLE logs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    level TEXT,
                    message TEXT,
                    timestamp TEXT DEFAULT (datetime('now'))
                );

                -- Insert initial test data
                INSERT INTO users (name, email, created_at, updated_at) VALUES 
                    ('Alice', 'alice@test.com', '2024-01-01 10:00:00', '2024-01-01 10:00:00'),
                    ('Bob', 'bob@test.com', '2024-01-01 11:00:00', '2024-01-01 11:00:00'),
                    ('Charlie', 'charlie@test.com', '2024-01-01 12:00:00', '2024-01-01 12:00:00');

                INSERT INTO orders (user_id, amount, created_at) VALUES
                    (1, 100.00, '2024-01-01 10:30:00'),
                    (2, 200.00, '2024-01-01 11:30:00'),
                    (1, 150.00, '2024-01-01 12:30:00');

                INSERT INTO logs (level, message, timestamp) VALUES
                    ('INFO', 'System started', '2024-01-01 09:00:00'),
                    ('DEBUG', 'User login', '2024-01-01 10:00:00'),
                    ('ERROR', 'Connection failed', '2024-01-01 11:00:00');
            ";

            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        [Fact]
        public void DiscoverWatermarkColumns_ShouldFindTimestampColumns()
        {
            // Test that watermark columns are discovered
            var columns = _service.DiscoverWatermarkColumns(_connection, "users");
            
            Assert.NotEmpty(columns);
            // Should find timestamp columns like updated_at or created_at
            Assert.Contains(columns, c => c.Contains("at"));
        }

        [Fact]
        public void DiscoverWatermarkColumns_ShouldHandleTimestampColumn()
        {
            // Test fallback to timestamp column when standard names don't exist
            var columns = _service.DiscoverWatermarkColumns(_connection, "logs");
            
            Assert.NotEmpty(columns);
            // Should find the timestamp column
            Assert.Contains(columns, c => c.Contains("timestamp") || c.Contains("id"));
        }

        [Fact]
        public void DiscoverWatermarkColumns_NonExistentTable_ShouldReturnEmpty()
        {
            var columns = _service.DiscoverWatermarkColumns(_connection, "non_existent_table");
            
            Assert.Empty(columns);
        }

        [Fact]
        public async Task ExecuteDeltaExportAsync_ShouldCreateValidResult()
        {
            var config = new DeltaExportConfig
            {
                Strategy = DeltaStrategy.Watermark,
                WatermarkColumns = new[] { "updated_at" },
                MaxRows = 1000
            };

            var result = await _service.ExecuteDeltaExportAsync(_connection, "users", config, null);
            
            Assert.NotNull(result);
            Assert.True(result.RowsExported >= 0);
            Assert.NotNull(result.Checkpoint);
            Assert.Equal(DeltaStrategy.Watermark, result.Checkpoint.Strategy);
        }

        [Fact]
        public void ValidateWatermarkColumns_ValidColumns_ShouldReturnSuccess()
        {
            var columns = new[] { "updated_at" };
            var validationResult = _service.ValidateWatermarkColumns(_connection, "users", columns);
            
            Assert.True(validationResult.IsValid);
            Assert.Empty(validationResult.Errors);
        }

        [Fact]
        public void ValidateWatermarkColumns_InvalidColumn_ShouldReturnError()
        {
            var columns = new[] { "non_existent_column" };
            var validationResult = _service.ValidateWatermarkColumns(_connection, "users", columns);
            
            Assert.False(validationResult.IsValid);
            Assert.NotEmpty(validationResult.Errors);
        }

        [Fact]
        public void ValidateWatermarkColumns_InvalidTable_ShouldReturnError()
        {
            var columns = new[] { "id" };
            var validationResult = _service.ValidateWatermarkColumns(_connection, "non_existent_table", columns);
            
            Assert.False(validationResult.IsValid);
            Assert.NotEmpty(validationResult.Errors);
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }
}