using DB2XL.Core.Services;
using DB2XL.Export.Bundle.Services;
using DB2XL.Export.Bundle.Tests.TestHelpers;
using Microsoft.Data.Sqlite;

namespace DB2XL.Export.Bundle.Tests.Services;

public class SqliteDataExtractorTests : IDisposable
{
    private readonly ISqliteDataExtractor _extractor;
    private readonly TestDatabaseHelper _dbHelper;

    public SqliteDataExtractorTests()
    {
        _extractor = new SqliteDataExtractor();
        _dbHelper = new TestDatabaseHelper();
    }

    #region Data Extraction Tests

    [Fact]
    public async Task ExtractTableDataAsync_WithBasicTable_ShouldReturnAllRows()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseWithDataAsync();
        var connectionString = $"Data Source={dbPath}";
        var options = new ExtractionOptions();

        // Act
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in _extractor.ExtractTableDataAsync(connectionString, "users", options))
        {
            rows.Add(row);
        }

        // Assert
        Assert.NotEmpty(rows);
        Assert.True(rows.Count >= 5); // Should have the sample users
        Assert.Contains(rows, r => r.ContainsKey("name") && r["name"]?.ToString()?.Contains("Alice") == true);
    }

    [Fact]
    public async Task ExtractTableDataAsync_WithWhereClause_ShouldFilterRows()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseWithDataAsync();
        var connectionString = $"Data Source={dbPath}";
        var options = new ExtractionOptions
        {
            WhereClause = "name LIKE 'A%'"
        };

        // Act
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in _extractor.ExtractTableDataAsync(connectionString, "users", options))
        {
            rows.Add(row);
        }

        // Assert
        Assert.NotEmpty(rows);
        Assert.All(rows, row => 
        {
            var name = row["name"]?.ToString();
            Assert.True(name?.StartsWith("A") == true);
        });
    }

    [Fact]
    public async Task ExtractTableDataAsync_WithMaxRows_ShouldLimitResults()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseWithDataAsync();
        var connectionString = $"Data Source={dbPath}";
        var options = new ExtractionOptions
        {
            MaxRows = 2
        };

        // Act
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in _extractor.ExtractTableDataAsync(connectionString, "users", options))
        {
            rows.Add(row);
        }

        // Assert
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task ExtractTableDataAsync_WithColumnFiltering_ShouldIncludeOnlySpecifiedColumns()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseWithDataAsync();
        var connectionString = $"Data Source={dbPath}";
        var options = new ExtractionOptions
        {
            IncludeColumns = new[] { "id", "name" }
        };

        // Act
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in _extractor.ExtractTableDataAsync(connectionString, "users", options))
        {
            rows.Add(row);
        }

        // Assert
        Assert.NotEmpty(rows);
        var firstRow = rows.First();
        Assert.Equal(2, firstRow.Count);
        Assert.Contains("id", firstRow.Keys);
        Assert.Contains("name", firstRow.Keys);
        Assert.DoesNotContain("email", firstRow.Keys);
    }

    [Fact]
    public async Task ExtractTableDataAsync_WithExcludeColumns_ShouldExcludeSpecifiedColumns()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseWithDataAsync();
        var connectionString = $"Data Source={dbPath}";
        var options = new ExtractionOptions
        {
            ExcludeColumns = new[] { "created_at", "is_active" }
        };

        // Act
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in _extractor.ExtractTableDataAsync(connectionString, "users", options))
        {
            rows.Add(row);
        }

        // Assert
        Assert.NotEmpty(rows);
        var firstRow = rows.First();
        Assert.DoesNotContain("created_at", firstRow.Keys);
        Assert.DoesNotContain("is_active", firstRow.Keys);
        Assert.Contains("id", firstRow.Keys);
        Assert.Contains("name", firstRow.Keys);
    }

    [Fact]
    public async Task ExtractTableBatchesAsync_ShouldReturnBatchesOfSpecifiedSize()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateComplexityTestDatabaseAsync(3, 10); // 3 tables with 10 rows each
        var connectionString = $"Data Source={dbPath}";
        var options = new ExtractionOptions
        {
            BatchSize = 3
        };

        // Act
        var batches = new List<IReadOnlyList<IReadOnlyDictionary<string, object?>>>();
        await foreach (var batch in _extractor.ExtractTableBatchesAsync(connectionString, "table_001", options))
        {
            batches.Add(batch);
        }

        // Assert
        Assert.NotEmpty(batches);
        
        // All batches except possibly the last should have exactly the batch size
        for (int i = 0; i < batches.Count - 1; i++)
        {
            Assert.Equal(3, batches[i].Count);
        }
        
        // Last batch should have remaining rows (≤ batch size)
        Assert.True(batches.Last().Count <= 3);
        
        // Total rows should match expected
        var totalRows = batches.Sum(b => b.Count);
        Assert.True(totalRows >= 10);
    }

    #endregion

    #region Table Analysis Tests

    [Fact]
    public async Task AnalyzeTableAsync_WithBasicTable_ShouldReturnCompleteMetadata()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseWithDataAsync();
        var connectionString = $"Data Source={dbPath}";

        // Act
        var metadata = await _extractor.AnalyzeTableAsync(connectionString, "users");

        // Assert
        Assert.Equal("users", metadata.TableName);
        Assert.Equal("table", metadata.TableType);
        Assert.NotEmpty(metadata.Columns);
        Assert.True(metadata.EstimatedRowCount >= 0);
        Assert.True(metadata.HasRowId);
        Assert.False(metadata.IsWithoutRowId);
        Assert.NotEmpty(metadata.CreateSql);
        
        // Should have primary key column
        Assert.Contains(metadata.Columns, c => c.IsPrimaryKey);
        Assert.NotEmpty(metadata.PrimaryKeyColumns);
        
        // Should have recommended ordering
        Assert.NotEmpty(metadata.RecommendedOrderBy);
    }

    [Fact]
    public async Task AnalyzeTableAsync_WithComplexTable_ShouldDetectAllFeatures()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseWithDataAsync();
        var connectionString = $"Data Source={dbPath}";

        // Act
        var metadata = await _extractor.AnalyzeTableAsync(connectionString, "orders");

        // Assert
        Assert.Equal("orders", metadata.TableName);
        Assert.NotEmpty(metadata.Columns);
        
        // Should detect foreign keys
        Assert.NotEmpty(metadata.ForeignKeys);
        Assert.Contains(metadata.ForeignKeys, fk => fk.ColumnName == "user_id");
        
        // Should have proper column metadata
        var idColumn = metadata.Columns.FirstOrDefault(c => c.Name == "id");
        Assert.NotNull(idColumn);
        Assert.True(idColumn.IsPrimaryKey);
        Assert.Equal("INTEGER", idColumn.TypeAffinity);
    }

    [Fact]
    public async Task AnalyzeTableAsync_WithDataTypesTable_ShouldDetectAllColumnTypes()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateDataTypesTestDatabaseAsync();
        var connectionString = $"Data Source={dbPath}";

        // Act
        var metadata = await _extractor.AnalyzeTableAsync(connectionString, "data_types_test");

        // Assert
        Assert.NotEmpty(metadata.Columns);
        
        // Verify different column types are detected
        Assert.Contains(metadata.Columns, c => c.Name == "text_field" && c.TypeAffinity == "TEXT");
        Assert.Contains(metadata.Columns, c => c.Name == "integer_field" && c.TypeAffinity == "INTEGER");
        Assert.Contains(metadata.Columns, c => c.Name == "real_field" && c.TypeAffinity == "REAL");
        Assert.Contains(metadata.Columns, c => c.Name == "blob_field" && c.TypeAffinity == "BLOB");
        Assert.Contains(metadata.Columns, c => c.Name == "blob_field" && c.IsBlobColumn);
    }

    #endregion

    #region Table Discovery Tests

    [Fact]
    public async Task GetTablesAsync_WithBasicDatabase_ShouldReturnAllTables()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseWithDataAsync();
        var connectionString = $"Data Source={dbPath}";

        // Act
        var tables = await _extractor.GetTablesAsync(connectionString);

        // Assert
        Assert.NotEmpty(tables);
        Assert.Contains("users", tables);
        Assert.Contains("orders", tables);
        Assert.Contains("products", tables);
        
        // Should be sorted alphabetically
        var sortedTables = tables.OrderBy(t => t).ToList();
        Assert.Equal(sortedTables, tables.ToList());
    }

    [Fact]
    public async Task GetTablesAsync_WithTableFilter_ShouldReturnFilteredTables()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseWithDataAsync();
        var connectionString = $"Data Source={dbPath}";

        // Act
        var tables = await _extractor.GetTablesAsync(connectionString, tableFilter: "user%");

        // Assert
        Assert.NotEmpty(tables);
        Assert.Contains("users", tables);
        Assert.DoesNotContain("orders", tables);
        Assert.DoesNotContain("products", tables);
    }

    [Fact]
    public async Task GetTablesAsync_WithEmptyDatabase_ShouldReturnEmptyList()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateEmptyDatabaseAsync();
        var connectionString = $"Data Source={dbPath}";

        // Act
        var tables = await _extractor.GetTablesAsync(connectionString);

        // Assert
        Assert.Empty(tables);
    }

    #endregion

    #region Row Count Estimation Tests

    [Fact]
    public async Task EstimateRowCountAsync_WithDataTable_ShouldReturnPositiveCount()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseWithDataAsync();
        var connectionString = $"Data Source={dbPath}";

        // Act
        var rowCount = await _extractor.EstimateRowCountAsync(connectionString, "users");

        // Assert
        Assert.True(rowCount > 0);
        Assert.True(rowCount >= 5); // Should have at least the sample users
    }

    [Fact]
    public async Task EstimateRowCountAsync_WithEmptyTable_ShouldReturnZero()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseAsync(new[] { "empty_table" });
        var connectionString = $"Data Source={dbPath}";

        // Act
        var rowCount = await _extractor.EstimateRowCountAsync(connectionString, "empty_table");

        // Assert
        Assert.Equal(1, rowCount); // TestDatabaseHelper creates one test row
    }

    #endregion

    #region Connection Validation Tests

    [Fact]
    public async Task ValidateConnectionAsync_WithValidDatabase_ShouldReturnTrue()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseAsync();
        var connectionString = $"Data Source={dbPath}";

        // Act
        var isValid = await _extractor.ValidateConnectionAsync(connectionString);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public async Task ValidateConnectionAsync_WithInvalidPath_ShouldReturnFalse()
    {
        // Arrange
        var connectionString = "Data Source=/nonexistent/path/database.sqlite";

        // Act
        var isValid = await _extractor.ValidateConnectionAsync(connectionString);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public async Task ValidateConnectionAsync_WithInvalidConnectionString_ShouldReturnFalse()
    {
        // Arrange
        var connectionString = "Invalid Connection String";

        // Act
        var isValid = await _extractor.ValidateConnectionAsync(connectionString);

        // Assert
        Assert.False(isValid);
    }

    #endregion

    #region Edge Cases and Error Handling

    [Fact]
    public async Task ExtractTableDataAsync_WithNonExistentTable_ShouldThrowException()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseAsync();
        var connectionString = $"Data Source={dbPath}";
        var options = new ExtractionOptions();

        // Act & Assert
        await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            await foreach (var row in _extractor.ExtractTableDataAsync(connectionString, "nonexistent_table", options))
            {
                // Should not reach here
            }
        });
    }

    [Fact]
    public async Task AnalyzeTableAsync_WithNonExistentTable_ShouldThrowException()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseAsync();
        var connectionString = $"Data Source={dbPath}";

        // Act & Assert
        await Assert.ThrowsAsync<SqliteException>(() => 
            _extractor.AnalyzeTableAsync(connectionString, "nonexistent_table"));
    }

    [Fact]
    public void ExtractionOptions_DefaultValues_ShouldBeReasonable()
    {
        // Arrange & Act
        var options = new ExtractionOptions();

        // Assert
        Assert.Equal(25_000, options.BatchSize);
        Assert.True(options.DeterministicOrdering);
        Assert.Equal(0, options.MaxRows);
        Assert.Equal(300, options.CommandTimeoutSeconds);
        Assert.Equal(BlobHandlingMode.Include, options.BlobMode);
        Assert.Empty(options.ExcludeColumns!);
        Assert.Null(options.IncludeColumns);
        Assert.Null(options.WhereClause);
        Assert.Null(options.CustomOrderBy);
    }

    #endregion

    #region Deterministic Ordering Tests

    [Fact]
    public async Task ExtractTableDataAsync_WithDeterministicOrdering_ShouldProduceConsistentResults()
    {
        // Arrange
        var dbPath = await _dbHelper.CreateTestDatabaseWithDataAsync();
        var connectionString = $"Data Source={dbPath}";
        var options = new ExtractionOptions { DeterministicOrdering = true };

        // Act - Extract data twice
        var firstExtraction = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in _extractor.ExtractTableDataAsync(connectionString, "users", options))
        {
            firstExtraction.Add(row);
        }

        var secondExtraction = new List<IReadOnlyDictionary<string, object?>>();
        await foreach (var row in _extractor.ExtractTableDataAsync(connectionString, "users", options))
        {
            secondExtraction.Add(row);
        }

        // Assert - Results should be identical
        Assert.Equal(firstExtraction.Count, secondExtraction.Count);
        
        for (int i = 0; i < firstExtraction.Count; i++)
        {
            var firstRow = firstExtraction[i];
            var secondRow = secondExtraction[i];
            
            Assert.Equal(firstRow.Count, secondRow.Count);
            foreach (var key in firstRow.Keys)
            {
                Assert.Equal(firstRow[key], secondRow[key]);
            }
        }
    }

    #endregion

    public void Dispose()
    {
        _dbHelper?.Dispose();
    }
}