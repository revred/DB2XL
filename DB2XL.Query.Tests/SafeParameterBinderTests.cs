using Xunit;
using Microsoft.Data.Sqlite;
using DB2XL.Query;

namespace DB2XL.Query.Tests;

public class SafeParameterBinderTests
{
    [Fact]
    public void ValidateParameters_ValidParameters_ReturnsValid()
    {
        // Arrange
        var parameters = new Dictionary<string, object?>
        {
            ["param_0"] = "test",
            ["param_1"] = 123,
            ["param_2"] = null
        };
        var sql = "SELECT * FROM users WHERE name = @param_0 AND id = @param_1 AND deleted_at = @param_2";
        
        // Act
        var result = SafeParameterBinder.ValidateParameters(parameters, sql);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
    
    [Fact]
    public void ValidateParameters_MissingParameters_ReturnsInvalid()
    {
        // Arrange
        var parameters = new Dictionary<string, object?>
        {
            ["param_0"] = "test"
        };
        var sql = "SELECT * FROM users WHERE name = @param_0 AND id = @param_1";
        
        // Act
        var result = SafeParameterBinder.ValidateParameters(parameters, sql);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Missing parameters: param_1", result.Errors);
    }
    
    [Fact]
    public void ValidateParameters_DangerousParameterName_ReturnsInvalid()
    {
        // Arrange
        var parameters = new Dictionary<string, object?>
        {
            ["param_SELECT"] = "test"
        };
        var sql = "SELECT * FROM users WHERE name = @param_SELECT";
        
        // Act
        var result = SafeParameterBinder.ValidateParameters(parameters, sql);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Parameter name contains SQL keywords: param_SELECT", result.Errors);
    }
    
    [Fact]
    public void ValidateParameters_ParameterWithQuotes_ReturnsInvalid()
    {
        // Arrange
        var parameters = new Dictionary<string, object?>
        {
            ["param'0"] = "test"
        };
        var sql = "SELECT * FROM users WHERE name = @param'0";
        
        // Act
        var result = SafeParameterBinder.ValidateParameters(parameters, sql);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Parameter name contains dangerous characters: param'0", result.Errors);
    }
    
    [Fact]
    public void ValidateParameters_ExtremelyLargeString_ReturnsInvalid()
    {
        // Arrange
        var largeString = new string('x', 100_001);
        var parameters = new Dictionary<string, object?>
        {
            ["param_0"] = largeString
        };
        var sql = "SELECT * FROM users WHERE name = @param_0";
        
        // Act
        var result = SafeParameterBinder.ValidateParameters(parameters, sql);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("exceeds maximum string length", result.Errors[0]);
    }
    
    [Fact]
    public void ValidateParameters_UnsupportedType_ReturnsInvalid()
    {
        // Arrange
        var parameters = new Dictionary<string, object?>
        {
            ["param_0"] = new List<string> { "test" }
        };
        var sql = "SELECT * FROM users WHERE name = @param_0";
        
        // Act
        var result = SafeParameterBinder.ValidateParameters(parameters, sql);
        
        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Parameter param_0 has unsupported type: List`1", result.Errors);
    }
    
    [Fact]
    public void ValidateParameters_SupportedTypes_ReturnsValid()
    {
        // Arrange
        var parameters = new Dictionary<string, object?>
        {
            ["str_param"] = "string",
            ["int_param"] = 42,
            ["long_param"] = 42L,
            ["double_param"] = 3.14,
            ["float_param"] = 2.71f,
            ["bool_param"] = true,
            ["date_param"] = DateTime.Now,
            ["bytes_param"] = new byte[] { 1, 2, 3 },
            ["decimal_param"] = 123.45m,
            ["guid_param"] = Guid.NewGuid(),
            ["null_param"] = null
        };
        var sql = @"SELECT * FROM users 
                    WHERE name = @str_param 
                    AND id = @int_param 
                    AND bigid = @long_param 
                    AND score = @double_param 
                    AND rating = @float_param 
                    AND active = @bool_param 
                    AND created = @date_param 
                    AND data = @bytes_param 
                    AND price = @decimal_param 
                    AND uuid = @guid_param 
                    AND deleted = @null_param";
        
        // Act
        var result = SafeParameterBinder.ValidateParameters(parameters, sql);
        
        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
    
    [Fact]
    public void BindParameters_ValidParameters_BindsSuccessfully()
    {
        // Arrange
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT @param_0, @param_1, @param_2";
        
        var parameters = new Dictionary<string, object?>
        {
            ["param_0"] = "test",
            ["param_1"] = 42,
            ["param_2"] = null
        };
        
        // Act & Assert (should not throw)
        SafeParameterBinder.BindParameters(command, parameters);
        
        // Verify parameters were bound
        Assert.Equal(3, command.Parameters.Count);
        Assert.Equal("@param_0", command.Parameters[0].ParameterName);
        Assert.Equal("test", command.Parameters[0].Value);
        Assert.Equal("@param_1", command.Parameters[1].ParameterName);
        Assert.Equal(42, command.Parameters[1].Value);
        Assert.Equal("@param_2", command.Parameters[2].ParameterName);
        Assert.Equal(DBNull.Value, command.Parameters[2].Value);
    }
    
    [Fact]
    public void BindParameters_InvalidParameters_ThrowsException()
    {
        // Arrange
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT @param_0";
        
        var parameters = new Dictionary<string, object?>
        {
            ["param_SELECT"] = "test"
        };
        
        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SafeParameterBinder.BindParameters(command, parameters));
        
        Assert.Contains("Parameter validation failed", exception.Message);
    }
    
    [Fact]
    public void ExtractParameterNames_ComplexSql_ExtractsAllParameters()
    {
        // This tests the private method indirectly through ValidateParameters
        var parameters = new Dictionary<string, object?>
        {
            ["start_date"] = "2023-01-01",
            ["end_date"] = "2023-12-31",
            ["user_id"] = 123,
            ["status"] = "active"
        };
        
        var sql = @"
            SELECT u.name, o.total 
            FROM users u 
            JOIN orders o ON u.id = o.user_id 
            WHERE o.created_at >= @start_date 
                AND o.created_at <= @end_date 
                AND u.id = @user_id 
                AND u.status = @status
            ORDER BY o.created_at DESC";
        
        // Act
        var result = SafeParameterBinder.ValidateParameters(parameters, sql);
        
        // Assert
        Assert.True(result.IsValid);
    }
}

public class SqliteCommandExtensionsTests
{
    [Fact]
    public void ExecuteScalarSafe_ValidQuery_ReturnsResult()
    {
        // Arrange
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT @value";
        
        var parameters = new Dictionary<string, object?> { ["value"] = 42 };
        
        // Act
        var result = command.ExecuteScalarSafe(parameters);
        
        // Assert
        Assert.Equal(42L, result); // SQLite returns integers as long
    }
    
    [Fact]
    public void ExecuteReaderSafe_ValidQuery_ReturnsData()
    {
        // Arrange
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        
        // Create test table
        using var createCommand = connection.CreateCommand();
        createCommand.CommandText = "CREATE TABLE test (id INTEGER, name TEXT)";
        createCommand.ExecuteNonQuery();
        
        using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText = "INSERT INTO test VALUES (1, 'Alice'), (2, 'Bob')";
        insertCommand.ExecuteNonQuery();
        
        // Test parameterized query
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM test WHERE id = @id";
        
        var parameters = new Dictionary<string, object?> { ["id"] = 1 };
        
        // Act
        using var reader = command.ExecuteReaderSafe(parameters);
        
        // Assert
        Assert.True(reader.Read());
        Assert.Equal(1L, reader["id"]);
        Assert.Equal("Alice", reader["name"]);
        Assert.False(reader.Read());
    }
    
    [Fact]
    public void ExecuteNonQuerySafe_ValidUpdate_AffectsRows()
    {
        // Arrange
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        
        // Create test table
        using var createCommand = connection.CreateCommand();
        createCommand.CommandText = "CREATE TABLE test (id INTEGER, name TEXT)";
        createCommand.ExecuteNonQuery();
        
        using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText = "INSERT INTO test VALUES (1, 'Alice')";
        insertCommand.ExecuteNonQuery();
        
        // Test parameterized update
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE test SET name = @name WHERE id = @id";
        
        var parameters = new Dictionary<string, object?>
        {
            ["name"] = "Updated Alice",
            ["id"] = 1
        };
        
        // Act
        var rowsAffected = command.ExecuteNonQuerySafe(parameters);
        
        // Assert
        Assert.Equal(1, rowsAffected);
    }
}