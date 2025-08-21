using Xunit;
using DB2XL.Query;

namespace DB2XL.Query.Tests;

public class SecurityFilterTests
{
    [Fact]
    public void ValidateTable_WithEmptyTableName_ShouldReturnDeny()
    {
        // Arrange
        var config = new SecurityFilterConfig();
        var filter = new SecurityFilter(config);

        // Act
        var result = filter.ValidateTable("");

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("cannot be null or empty", result.DenialReason);
    }

    [Fact]
    public void ValidateTable_WithDeniedTable_ShouldReturnDeny()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            DeniedTables = { "secret_data" }
        };
        var filter = new SecurityFilter(config);

        // Act
        var result = filter.ValidateTable("secret_data");

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("explicitly denied", result.DenialReason);
    }

    [Fact]
    public void ValidateTable_WithDeniedPattern_ShouldReturnDeny()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            DeniedTables = { "admin_*" }
        };
        var filter = new SecurityFilter(config);

        // Act
        var result = filter.ValidateTable("admin_users");

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("explicitly denied", result.DenialReason);
    }

    [Fact]
    public void ValidateTable_WithAllowedTable_ShouldReturnAllow()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            AllowedTables = { "users", "orders" }
        };
        var filter = new SecurityFilter(config);

        // Act
        var result = filter.ValidateTable("users");

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Null(result.DenialReason);
    }

    [Fact]
    public void ValidateTable_NotInAllowedList_ShouldReturnDeny()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            AllowedTables = { "users", "orders" }
        };
        var filter = new SecurityFilter(config);

        // Act
        var result = filter.ValidateTable("secret_data");

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("not in the allowed tables list", result.DenialReason);
    }

    [Fact]
    public void ValidateTable_WithStrictMode_NotExplicitlyAllowed_ShouldReturnDeny()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            StrictMode = true,
            AllowedTables = { "users" }
        };
        var filter = new SecurityFilter(config);

        // Act
        var result = filter.ValidateTable("orders");

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("not in the allowed tables list (strict mode)", result.DenialReason);
    }

    [Fact]
    public void ValidateColumn_WithEmptyColumnName_ShouldReturnDeny()
    {
        // Arrange
        var config = new SecurityFilterConfig();
        var filter = new SecurityFilter(config);

        // Act
        var result = filter.ValidateColumn("users", "");

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("cannot be null or empty", result.DenialReason);
    }

    [Fact]
    public void ValidateColumn_WithGlobalDeniedPattern_ShouldReturnDeny()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            GlobalDeniedColumnPatterns = { "*password*", "*secret*" }
        };
        var filter = new SecurityFilter(config);

        // Act
        var result = filter.ValidateColumn("users", "user_password");

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("matches global denied pattern", result.DenialReason);
    }

    [Fact]
    public void ValidateColumn_WithTableSpecificDeniedColumn_ShouldReturnDeny()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            DeniedColumns = 
            {
                { "users", new HashSet<string> { "ssn", "credit_card" } }
            }
        };
        var filter = new SecurityFilter(config);

        // Act
        var result = filter.ValidateColumn("users", "ssn");

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("explicitly denied", result.DenialReason);
    }

    [Fact]
    public void ValidateColumn_WithTableSpecificAllowedColumn_ShouldReturnAllow()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            AllowedColumns = 
            {
                { "users", new HashSet<string> { "id", "username", "email" } }
            }
        };
        var filter = new SecurityFilter(config);

        // Act
        var result = filter.ValidateColumn("users", "username");

        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ValidateColumn_NotInAllowedColumnsList_ShouldReturnDeny()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            AllowedColumns = 
            {
                { "users", new HashSet<string> { "id", "username" } }
            }
        };
        var filter = new SecurityFilter(config);

        // Act
        var result = filter.ValidateColumn("users", "email");

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("not in the allowed columns list", result.DenialReason);
    }

    [Fact]
    public void ValidateColumn_DeniedTableAccess_ShouldReturnDeny()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            DeniedTables = { "secret_table" }
        };
        var filter = new SecurityFilter(config);

        // Act
        var result = filter.ValidateColumn("secret_table", "any_column");

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("explicitly denied", result.DenialReason);
    }

    [Fact]
    public void ValidateSelectionGrammar_WithValidGrammar_ShouldReturnAllow()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            AllowedTables = { "users" },
            AllowedColumns = 
            {
                { "users", new HashSet<string> { "id", "username", "email" } }
            }
        };
        var filter = new SecurityFilter(config);
        var grammar = new SelectionGrammar
        {
            Table = "users",
            Select = new[] { "username", "email" },
            Where = new ComparisonExpression
            {
                Column = "id",
                Operator = ComparisonOperator.Equal,
                Value = 1
            }
        };

        // Act
        var result = filter.ValidateSelectionGrammar(grammar);

        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ValidateSelectionGrammar_WithDeniedTable_ShouldReturnDeny()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            DeniedTables = { "secret_table" }
        };
        var filter = new SecurityFilter(config);
        var grammar = new SelectionGrammar
        {
            Table = "secret_table",
            Select = new[] { "*" }
        };

        // Act
        var result = filter.ValidateSelectionGrammar(grammar);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("explicitly denied", result.DenialReason);
    }

    [Fact]
    public void ValidateSelectionGrammar_WithDeniedSelectColumn_ShouldReturnDeny()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            AllowedTables = { "users" },
            DeniedColumns = 
            {
                { "users", new HashSet<string> { "password" } }
            }
        };
        var filter = new SecurityFilter(config);
        var grammar = new SelectionGrammar
        {
            Table = "users",
            Select = new[] { "username", "password" }
        };

        // Act
        var result = filter.ValidateSelectionGrammar(grammar);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("explicitly denied", result.DenialReason);
    }

    [Fact]
    public void ValidateSelectionGrammar_WithDeniedWhereColumn_ShouldReturnDeny()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            AllowedTables = { "users" },
            DeniedColumns = 
            {
                { "users", new HashSet<string> { "ssn" } }
            }
        };
        var filter = new SecurityFilter(config);
        var grammar = new SelectionGrammar
        {
            Table = "users",
            Select = new[] { "username" },
            Where = new ComparisonExpression
            {
                Column = "ssn",
                Operator = ComparisonOperator.Equal,
                Value = "123-45-6789"
            }
        };

        // Act
        var result = filter.ValidateSelectionGrammar(grammar);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("explicitly denied", result.DenialReason);
    }

    [Fact]
    public void ValidateSelectionGrammar_WithDeniedOrderByColumn_ShouldReturnDeny()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            AllowedTables = { "users" },
            DeniedColumns = 
            {
                { "users", new HashSet<string> { "salary" } }
            }
        };
        var filter = new SecurityFilter(config);
        var grammar = new SelectionGrammar
        {
            Table = "users",
            Select = new[] { "username" },
            OrderBy = new[]
            {
                new OrderByClause { Column = "salary", Direction = SortDirection.Descending }
            }
        };

        // Act
        var result = filter.ValidateSelectionGrammar(grammar);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("explicitly denied", result.DenialReason);
    }

    [Fact]
    public void ValidateSelectionGrammar_WithComplexWhereExpression_ShouldValidateAllColumns()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            AllowedTables = { "users" },
            DeniedColumns = 
            {
                { "users", new HashSet<string> { "secret_field" } }
            }
        };
        var filter = new SecurityFilter(config);
        var grammar = new SelectionGrammar
        {
            Table = "users",
            Select = new[] { "username" },
            Where = new AndExpression
            {
                Expressions = new[]
                {
                    new ComparisonExpression
                    {
                        Column = "active",
                        Operator = ComparisonOperator.Equal,
                        Value = true
                    },
                    new ComparisonExpression
                    {
                        Column = "secret_field",
                        Operator = ComparisonOperator.Equal,
                        Value = "test"
                    }
                }
            }
        };

        // Act
        var result = filter.ValidateSelectionGrammar(grammar);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Contains("explicitly denied", result.DenialReason);
    }

    [Fact]
    public void FilterAllowedColumns_ShouldReturnOnlyAllowedColumns()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            AllowedColumns = 
            {
                { "users", new HashSet<string> { "id", "username", "email" } }
            }
        };
        var filter = new SecurityFilter(config);
        var allColumns = new[] { "id", "username", "password", "email", "ssn" };

        // Act
        var result = filter.FilterAllowedColumns("users", allColumns);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains("id", result);
        Assert.Contains("username", result);
        Assert.Contains("email", result);
        Assert.DoesNotContain("password", result);
        Assert.DoesNotContain("ssn", result);
    }

    [Fact]
    public void ValidateTable_CaseInsensitive_ShouldWork()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            AllowedTables = { "Users" }
        };
        var filter = new SecurityFilter(config);

        // Act
        var result = filter.ValidateTable("users");

        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ValidateColumn_CaseInsensitive_ShouldWork()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            AllowedColumns = 
            {
                { "Users", new HashSet<string> { "UserName" } }
            }
        };
        var filter = new SecurityFilter(config);

        // Act
        var result = filter.ValidateColumn("users", "username");

        // Assert
        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void ValidateTable_WildcardPattern_ShouldMatchCorrectly()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            AllowedTables = { "user_*", "order_*" },
            DeniedTables = { "*_secret" }
        };
        var filter = new SecurityFilter(config);

        // Act & Assert
        Assert.True(filter.ValidateTable("user_profiles").IsAllowed);
        Assert.True(filter.ValidateTable("order_history").IsAllowed);
        Assert.False(filter.ValidateTable("admin_secret").IsAllowed);
        Assert.False(filter.ValidateTable("products").IsAllowed); // Not in allowed patterns
    }

    [Fact]
    public void ValidateColumn_GlobalDeniedPatternWithWildcards_ShouldMatchCorrectly()
    {
        // Arrange
        var config = new SecurityFilterConfig
        {
            GlobalDeniedColumnPatterns = { "*password*", "*_secret", "ssn_*" }
        };
        var filter = new SecurityFilter(config);

        // Act & Assert
        Assert.False(filter.ValidateColumn("users", "user_password").IsAllowed);
        Assert.False(filter.ValidateColumn("users", "password_hash").IsAllowed);
        Assert.False(filter.ValidateColumn("users", "admin_secret").IsAllowed);
        Assert.False(filter.ValidateColumn("users", "ssn_encrypted").IsAllowed);
        Assert.True(filter.ValidateColumn("users", "username").IsAllowed);
    }
}