using DB2XL.Transformers;
using Microsoft.Data.Sqlite;
using Xunit;
using System.Data;

namespace SqliteXport.Tests.Transformers;

public class SqliteTypeHelperTests
{
    [Theory]
    [InlineData("INTEGER", SqliteAffinity.Integer)]
    [InlineData("INT", SqliteAffinity.Integer)]
    [InlineData("BIGINT", SqliteAffinity.Integer)]
    [InlineData("SMALLINT", SqliteAffinity.Integer)]
    [InlineData("TINYINT", SqliteAffinity.Integer)]
    [InlineData("integer", SqliteAffinity.Integer)]
    [InlineData("int", SqliteAffinity.Integer)]
    public void ParseColumnType_ShouldDetectIntegerTypes(string columnType, SqliteAffinity expected)
    {
        // Act
        var result = SqliteTypeHelper.ParseColumnType(columnType);
        
        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("REAL", SqliteAffinity.Real)]
    [InlineData("FLOAT", SqliteAffinity.Real)]
    [InlineData("DOUBLE", SqliteAffinity.Real)]
    [InlineData("DOUBLE PRECISION", SqliteAffinity.Real)]
    [InlineData("NUMERIC", SqliteAffinity.Real)]
    [InlineData("real", SqliteAffinity.Real)]
    [InlineData("float", SqliteAffinity.Real)]
    public void ParseColumnType_ShouldDetectRealTypes(string columnType, SqliteAffinity expected)
    {
        // Act
        var result = SqliteTypeHelper.ParseColumnType(columnType);
        
        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("TEXT", SqliteAffinity.Text)]
    [InlineData("VARCHAR", SqliteAffinity.Text)]
    [InlineData("CHAR", SqliteAffinity.Text)]
    [InlineData("CHARACTER", SqliteAffinity.Text)]
    [InlineData("CLOB", SqliteAffinity.Text)]
    [InlineData("STRING", SqliteAffinity.Text)]
    [InlineData("text", SqliteAffinity.Text)]
    [InlineData("varchar(255)", SqliteAffinity.Text)]
    public void ParseColumnType_ShouldDetectTextTypes(string columnType, SqliteAffinity expected)
    {
        // Act
        var result = SqliteTypeHelper.ParseColumnType(columnType);
        
        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("BLOB", SqliteAffinity.Blob)]
    [InlineData("blob", SqliteAffinity.Blob)]
    [InlineData("", SqliteAffinity.Blob)] // SQLite default for untyped
    public void ParseColumnType_ShouldDetectBlobTypes(string columnType, SqliteAffinity expected)
    {
        // Act
        var result = SqliteTypeHelper.ParseColumnType(columnType);
        
        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, SqliteAffinity.Text)]
    [InlineData("", SqliteAffinity.Blob)]
    [InlineData("UNKNOWN_TYPE", SqliteAffinity.Text)]
    [InlineData("CUSTOM", SqliteAffinity.Text)]
    [InlineData("MONEY", SqliteAffinity.Text)] // Custom type falls back to TEXT
    public void ParseColumnType_ShouldHandleEdgeCases(string? columnType, SqliteAffinity expected)
    {
        // Act
        var result = SqliteTypeHelper.ParseColumnType(columnType!);
        
        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(SqliteAffinity.Integer, "INTEGER")]
    [InlineData(SqliteAffinity.Real, "REAL")]
    [InlineData(SqliteAffinity.Text, "TEXT")]
    [InlineData(SqliteAffinity.Blob, "BLOB")]
    [InlineData(SqliteAffinity.Null, "NULL")]
    public void ToString_ShouldReturnCorrectStringRepresentation(SqliteAffinity type, string expected)
    {
        // Act
        var result = SqliteTypeHelper.ToString(type);
        
        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ToString_ShouldHandleUnknownValue()
    {
        // Act
        var result = SqliteTypeHelper.ToString((SqliteAffinity)999);
        
        // Assert
        Assert.Equal("UNKNOWN", result);
    }

    [Fact]
    public async Task GetSqliteType_ShouldDetectTypesFromActualData()
    {
        // Arrange - Create an in-memory database with various data types
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE test_types (
                id INTEGER PRIMARY KEY,
                int_col INTEGER,
                real_col REAL,
                text_col TEXT,
                blob_col BLOB,
                null_col TEXT
            );
            INSERT INTO test_types (id, int_col, real_col, text_col, blob_col, null_col)
            VALUES (1, 42, 3.14, 'hello', X'48656C6C6F', NULL);
        ";
        await cmd.ExecuteNonQueryAsync();

        // Act - Read the data and test type detection
        cmd.CommandText = "SELECT id, int_col, real_col, text_col, blob_col, null_col FROM test_types";
        using var reader = cmd.ExecuteReader();
        
        Assert.True(reader.Read());
        
        // Assert - Test each column type detection
        Assert.Equal(SqliteAffinity.Integer, SqliteTypeHelper.GetSqliteType(reader, 0)); // id
        Assert.Equal(SqliteAffinity.Integer, SqliteTypeHelper.GetSqliteType(reader, 1)); // int_col
        Assert.Equal(SqliteAffinity.Real, SqliteTypeHelper.GetSqliteType(reader, 2)); // real_col
        Assert.Equal(SqliteAffinity.Text, SqliteTypeHelper.GetSqliteType(reader, 3)); // text_col
        Assert.Equal(SqliteAffinity.Blob, SqliteTypeHelper.GetSqliteType(reader, 4)); // blob_col
        Assert.Equal(SqliteAffinity.Null, SqliteTypeHelper.GetSqliteType(reader, 5)); // null_col
    }

    [Fact]
    public async Task GetSqliteType_ShouldHandleAllNullRow()
    {
        // Arrange
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE all_nulls (col1 TEXT, col2 INTEGER, col3 REAL);
            INSERT INTO all_nulls VALUES (NULL, NULL, NULL);
        ";
        await cmd.ExecuteNonQueryAsync();

        // Act
        cmd.CommandText = "SELECT col1, col2, col3 FROM all_nulls";
        using var reader = cmd.ExecuteReader();
        
        Assert.True(reader.Read());
        
        // Assert - All should be detected as NULL type
        Assert.Equal(SqliteAffinity.Null, SqliteTypeHelper.GetSqliteType(reader, 0));
        Assert.Equal(SqliteAffinity.Null, SqliteTypeHelper.GetSqliteType(reader, 1));
        Assert.Equal(SqliteAffinity.Null, SqliteTypeHelper.GetSqliteType(reader, 2));
    }

    [Fact]
    public async Task GetSqliteType_ShouldHandleTypeAffinityCorrectly()
    {
        // Arrange - SQLite allows storing different types in same column
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE mixed_types (flexible_col TEXT);
            INSERT INTO mixed_types VALUES ('text_value');
            INSERT INTO mixed_types VALUES (42);
            INSERT INTO mixed_types VALUES (3.14);
        ";
        await cmd.ExecuteNonQueryAsync();

        // Act & Assert
        cmd.CommandText = "SELECT flexible_col FROM mixed_types ORDER BY rowid";
        using var reader = cmd.ExecuteReader();
        
        // First row - text
        Assert.True(reader.Read());
        Assert.Equal(SqliteAffinity.Text, SqliteTypeHelper.GetSqliteType(reader, 0));
        
        // Second row - integer stored in TEXT column (SQLite returns as text)
        Assert.True(reader.Read());
        Assert.Equal(SqliteAffinity.Text, SqliteTypeHelper.GetSqliteType(reader, 0));
        
        // Third row - real stored in TEXT column (SQLite returns as text)
        Assert.True(reader.Read());
        Assert.Equal(SqliteAffinity.Text, SqliteTypeHelper.GetSqliteType(reader, 0));
    }

    [Theory]
    [InlineData("VARCHAR(255)", SqliteAffinity.Text)]
    [InlineData("DECIMAL(10,2)", SqliteAffinity.Real)]
    [InlineData("BOOLEAN", SqliteAffinity.Text)]
    [InlineData("TIMESTAMP", SqliteAffinity.Text)]
    [InlineData("UUID", SqliteAffinity.Text)]
    [InlineData("JSON", SqliteAffinity.Text)]
    public void ParseColumnType_ShouldHandleCommonSqlTypes(string columnType, SqliteAffinity expected)
    {
        // These are common SQL types that map to SQLite affinities
        // Act
        var result = SqliteTypeHelper.ParseColumnType(columnType);
        
        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseColumnType_ShouldBeCaseInsensitive()
    {
        // Arrange
        string[] integerVariants = { "INTEGER", "integer", "Integer", "InTeGeR" };
        string[] textVariants = { "TEXT", "text", "Text", "TeXt" };
        
        // Act & Assert
        foreach (var variant in integerVariants)
        {
            Assert.Equal(SqliteAffinity.Integer, SqliteTypeHelper.ParseColumnType(variant));
        }
        
        foreach (var variant in textVariants)
        {
            Assert.Equal(SqliteAffinity.Text, SqliteTypeHelper.ParseColumnType(variant));
        }
    }

    [Fact]
    public void AllMethods_ShouldHandleAllEnumValues()
    {
        // Arrange - Get all enum values
        var allTypes = Enum.GetValues<SqliteAffinity>();
        
        // Act & Assert - ToString should handle all enum values
        foreach (var type in allTypes)
        {
            var stringResult = SqliteTypeHelper.ToString(type);
            Assert.NotNull(stringResult);
            Assert.NotEmpty(stringResult);
            Assert.DoesNotContain("UNKNOWN", stringResult);
        }
    }

    [Fact]
    public void SqliteTypeEnum_ShouldHaveExpectedValues()
    {
        // Verify our enum has the expected SQLite types
        Assert.True(Enum.IsDefined(typeof(SqliteAffinity), SqliteAffinity.Integer));
        Assert.True(Enum.IsDefined(typeof(SqliteAffinity), SqliteAffinity.Real));
        Assert.True(Enum.IsDefined(typeof(SqliteAffinity), SqliteAffinity.Text));
        Assert.True(Enum.IsDefined(typeof(SqliteAffinity), SqliteAffinity.Blob));
        Assert.True(Enum.IsDefined(typeof(SqliteAffinity), SqliteAffinity.Null));
        
        // Should have exactly 5 values (the 5 SQLite affinities)
        var allValues = Enum.GetValues<SqliteAffinity>();
        Assert.Equal(5, allValues.Length);
    }
}