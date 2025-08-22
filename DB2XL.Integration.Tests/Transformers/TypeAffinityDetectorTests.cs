using Microsoft.Data.Sqlite;
using DB2XL.Transform.Interfaces;
using DB2XL.Transform.TypeDetection;
using Xunit;

namespace DB2XL.Integration.Tests.Transformers;

/// <summary>
/// Comprehensive tests for TypeAffinityDetector to achieve >60% coverage
/// </summary>
public class TypeAffinityDetectorTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _connection;

    public TypeAffinityDetectorTests()
    {
        _connectionString = "Data Source=:memory:";
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
        
        // Create test table with various data types
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE test_types (
                id INTEGER PRIMARY KEY,
                text_column TEXT,
                integer_column INTEGER,
                real_column REAL,
                blob_column BLOB,
                null_column TEXT
            )";
        cmd.ExecuteNonQuery();

        // Insert test data
        cmd.CommandText = @"
            INSERT INTO test_types (text_column, integer_column, real_column, blob_column, null_column)
            VALUES ('test', 42, 3.14, X'48656C6C6F', NULL)";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }

    [Fact]
    public void GetSqliteAffinity_WithNullValue_ReturnsNull()
    {
        // Arrange
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT null_column FROM test_types WHERE id = 1";
        using var reader = cmd.ExecuteReader();
        reader.Read();

        // Act
        var result = TypeAffinityDetector.GetSqliteAffinity(reader, 0);

        // Assert
        Assert.Equal(SqliteAffinity.Null, result);
    }

    [Fact]
    public void GetSqliteAffinity_WithIntegerValue_ReturnsInteger()
    {
        // Arrange
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT integer_column FROM test_types WHERE id = 1";
        using var reader = cmd.ExecuteReader();
        reader.Read();

        // Act
        var result = TypeAffinityDetector.GetSqliteAffinity(reader, 0);

        // Assert
        Assert.Equal(SqliteAffinity.Integer, result);
    }

    [Fact]
    public void GetSqliteAffinity_WithRealValue_ReturnsReal()
    {
        // Arrange
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT real_column FROM test_types WHERE id = 1";
        using var reader = cmd.ExecuteReader();
        reader.Read();

        // Act
        var result = TypeAffinityDetector.GetSqliteAffinity(reader, 0);

        // Assert
        Assert.Equal(SqliteAffinity.Real, result);
    }

    [Fact]
    public void GetSqliteAffinity_WithTextValue_ReturnsText()
    {
        // Arrange
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT text_column FROM test_types WHERE id = 1";
        using var reader = cmd.ExecuteReader();
        reader.Read();

        // Act
        var result = TypeAffinityDetector.GetSqliteAffinity(reader, 0);

        // Assert
        Assert.Equal(SqliteAffinity.Text, result);
    }

    [Fact]
    public void GetSqliteAffinity_WithBlobValue_ReturnsBlob()
    {
        // Arrange
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT blob_column FROM test_types WHERE id = 1";
        using var reader = cmd.ExecuteReader();
        reader.Read();

        // Act
        var result = TypeAffinityDetector.GetSqliteAffinity(reader, 0);

        // Assert
        Assert.Equal(SqliteAffinity.Blob, result);
    }

    [Theory]
    [InlineData("INTEGER", SqliteAffinity.Integer)]
    [InlineData("INT", SqliteAffinity.Integer)]
    [InlineData("BIGINT", SqliteAffinity.Integer)]
    [InlineData("SMALLINT", SqliteAffinity.Integer)]
    [InlineData("TINYINT", SqliteAffinity.Integer)]
    public void ParseColumnType_WithIntegerTypes_ReturnsInteger(string columnType, SqliteAffinity expected)
    {
        // Act
        var result = TypeAffinityDetector.ParseColumnType(columnType);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("TEXT", SqliteAffinity.Text)]
    [InlineData("VARCHAR", SqliteAffinity.Text)]
    [InlineData("CHARACTER", SqliteAffinity.Text)]
    [InlineData("CHAR", SqliteAffinity.Text)]
    [InlineData("CLOB", SqliteAffinity.Text)]
    [InlineData("VARCHAR(50)", SqliteAffinity.Text)]
    public void ParseColumnType_WithTextTypes_ReturnsText(string columnType, SqliteAffinity expected)
    {
        // Act
        var result = TypeAffinityDetector.ParseColumnType(columnType);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("REAL", SqliteAffinity.Real)]
    [InlineData("DOUBLE", SqliteAffinity.Real)]
    [InlineData("FLOAT", SqliteAffinity.Real)]
    [InlineData("NUMERIC", SqliteAffinity.Real)]
    [InlineData("DECIMAL", SqliteAffinity.Real)]
    [InlineData("DECIMAL(10,2)", SqliteAffinity.Real)]
    public void ParseColumnType_WithRealTypes_ReturnsReal(string columnType, SqliteAffinity expected)
    {
        // Act
        var result = TypeAffinityDetector.ParseColumnType(columnType);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("BLOB", SqliteAffinity.Blob)]
    [InlineData("", SqliteAffinity.Blob)]
    public void ParseColumnType_WithBlobTypes_ReturnsBlob(string columnType, SqliteAffinity expected)
    {
        // Act
        var result = TypeAffinityDetector.ParseColumnType(columnType);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseColumnType_WithNullInput_ReturnsText()
    {
        // Act
        var result = TypeAffinityDetector.ParseColumnType(null);

        // Assert
        Assert.Equal(SqliteAffinity.Text, result);
    }

    [Theory]
    [InlineData("UNKNOWN_TYPE", SqliteAffinity.Text)]
    [InlineData("CUSTOM", SqliteAffinity.Text)]
    [InlineData("WEIRD_TYPE", SqliteAffinity.Text)]
    public void ParseColumnType_WithUnknownTypes_ReturnsText(string columnType, SqliteAffinity expected)
    {
        // Act
        var result = TypeAffinityDetector.ParseColumnType(columnType);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(SqliteAffinity.Integer, "INTEGER")]
    [InlineData(SqliteAffinity.Real, "REAL")]
    [InlineData(SqliteAffinity.Text, "TEXT")]
    [InlineData(SqliteAffinity.Blob, "BLOB")]
    [InlineData(SqliteAffinity.Null, "NULL")]
    public void AffinityToString_WithValidAffinity_ReturnsCorrectString(SqliteAffinity affinity, string expected)
    {
        // Act
        var result = TypeAffinityDetector.AffinityToString(affinity);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AffinityToString_WithInvalidAffinity_ReturnsUnknown()
    {
        // Act
        var result = TypeAffinityDetector.AffinityToString((SqliteAffinity)999);

        // Assert
        Assert.Equal("UNKNOWN", result);
    }

    [Theory]
    [InlineData("user_name", "name", true)]
    [InlineData("USER_NAME", "name", true)]
    [InlineData("UserName", "Name", true)]
    [InlineData("email", "mail", true)]
    [InlineData("password", "name", false)]
    [InlineData("id", "email", false)]
    public void ColumnNameMatches_WithVariousPatterns_ReturnsCorrectResult(string columnName, string pattern, bool expected)
    {
        // Act
        var result = TypeAffinityDetector.ColumnNameMatches(columnName, pattern);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("created_at", true)]
    [InlineData("updated_at", true)]
    [InlineData("timestamp", true)]
    [InlineData("created_time", true)]
    [InlineData("modification_date", true)]
    [InlineData("when_occurred", true)]
    [InlineData("logged_at", true)]
    [InlineData("user_name", false)]
    [InlineData("id", false)]
    [InlineData("email", false)]
    public void IsLikelyTimestampColumn_WithVariousColumnNames_ReturnsCorrectResult(string columnName, bool expected)
    {
        // Act
        var result = TypeAffinityDetector.IsLikelyTimestampColumn(columnName);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("user_data", true)]
    [InlineData("metadata", true)]
    [InlineData("json_payload", true)]
    [InlineData("config_settings", true)]
    [InlineData("properties", true)]
    [InlineData("attributes", true)]
    [InlineData("user_name", false)]
    [InlineData("id", false)]
    [InlineData("email", false)]
    public void IsLikelyJsonColumn_WithVariousColumnNames_ReturnsCorrectResult(string columnName, bool expected)
    {
        // Act
        var result = TypeAffinityDetector.IsLikelyJsonColumn(columnName);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("VARCHAR(255)", SqliteAffinity.Text)]
    [InlineData("DECIMAL(10,2)", SqliteAffinity.Real)]
    [InlineData("INT NOT NULL", SqliteAffinity.Integer)]
    [InlineData("TEXT DEFAULT ''", SqliteAffinity.Text)]
    public void ParseColumnType_WithComplexTypeDefinitions_ParsesCorrectly(string columnType, SqliteAffinity expected)
    {
        // Act
        var result = TypeAffinityDetector.ParseColumnType(columnType);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseColumnType_CaseInsensitive_WorksCorrectly()
    {
        // Arrange & Act
        var lowerResult = TypeAffinityDetector.ParseColumnType("integer");
        var upperResult = TypeAffinityDetector.ParseColumnType("INTEGER");
        var mixedResult = TypeAffinityDetector.ParseColumnType("Integer");

        // Assert
        Assert.Equal(SqliteAffinity.Integer, lowerResult);
        Assert.Equal(SqliteAffinity.Integer, upperResult);
        Assert.Equal(SqliteAffinity.Integer, mixedResult);
    }

    [Theory]
    [InlineData("floatval", SqliteAffinity.Real)]
    [InlineData("doublevalue", SqliteAffinity.Real)]
    [InlineData("numericdata", SqliteAffinity.Real)]
    public void ParseColumnType_WithPartialMatches_ReturnsCorrectAffinity(string columnType, SqliteAffinity expected)
    {
        // Act
        var result = TypeAffinityDetector.ParseColumnType(columnType);

        // Assert
        Assert.Equal(expected, result);
    }
}