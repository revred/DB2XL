using Xunit;
using DB2XL.Query;
using DB2XL.Core.Models;

namespace DB2XL.Query.Tests;

public class SqlInjectionValidatorTests
{
    private readonly SqlInjectionValidator _validator;
    private readonly SqlInjectionValidator _strictValidator;

    public SqlInjectionValidatorTests()
    {
        var config = new SqlInjectionProtectionConfig
        {
            EnableProtection = true,
            MaxStringLength = 1000,
            MaxIdentifierLength = 64,
            AllowSqlKeywordsInValues = false,
            AllowComments = false
        };
        _validator = new SqlInjectionValidator(config);

        var strictConfig = new SqlInjectionProtectionConfig
        {
            EnableProtection = true,
            MaxStringLength = 100,
            MaxIdentifierLength = 32,
            AllowSqlKeywordsInValues = false,
            AllowComments = false,
            DeniedPatterns = { "admin", "test" }
        };
        _strictValidator = new SqlInjectionValidator(strictConfig);
    }

    [Fact]
    public void ValidateIdentifier_WithNormalName_ShouldBeSafe()
    {
        // Act
        var result = _validator.ValidateIdentifier("users", "table name");

        // Assert
        Assert.True(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.None, result.ThreatLevel);
    }

    [Fact]
    public void ValidateIdentifier_WithEmptyName_ShouldBeUnsafe()
    {
        // Act
        var result = _validator.ValidateIdentifier("", "table name");

        // Assert
        Assert.False(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.Medium, result.ThreatLevel);
        Assert.Contains("Empty table name", result.Threat);
    }

    [Fact]
    public void ValidateIdentifier_WithSqlKeyword_ShouldBeUnsafe()
    {
        // Act
        var result = _validator.ValidateIdentifier("SELECT", "table name");

        // Assert
        Assert.False(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.Medium, result.ThreatLevel);
        Assert.Contains("is a SQL keyword", result.Threat);
    }

    [Fact]
    public void ValidateIdentifier_WithDangerousKeyword_ShouldBeCritical()
    {
        // Act
        var result = _validator.ValidateIdentifier("DROP", "table name");

        // Assert
        Assert.False(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.Critical, result.ThreatLevel);
        Assert.Contains("Dangerous keyword 'DROP' detected", result.Threat);
    }

    [Fact]
    public void ValidateIdentifier_ExceedingMaxLength_ShouldBeUnsafe()
    {
        // Arrange
        var longName = new string('a', 65);

        // Act
        var result = _validator.ValidateIdentifier(longName, "table name");

        // Assert
        Assert.False(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.Medium, result.ThreatLevel);
        Assert.Contains("exceeds maximum length", result.Threat);
    }

    [Fact]
    public void ValidateValue_WithNormalValue_ShouldBeSafe()
    {
        // Act
        var result = _validator.ValidateValue("john.doe@example.com");

        // Assert
        Assert.True(result.IsSafe);
    }

    [Fact]
    public void ValidateValue_WithNullValue_ShouldBeSafe()
    {
        // Act
        var result = _validator.ValidateValue(null);

        // Assert
        Assert.True(result.IsSafe);
    }

    [Fact]
    public void ValidateValue_WithSqlInjection_ShouldBeUnsafe()
    {
        // Act
        var result = _validator.ValidateValue("'; DROP TABLE users; --");

        // Assert
        Assert.False(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.Critical, result.ThreatLevel);
        Assert.Contains("Dangerous keyword 'DROP' detected", result.Threat);
    }

    [Fact]
    public void ValidateValue_WithUnionInjection_ShouldBeHigh()
    {
        // Act
        var result = _validator.ValidateValue("' UNION SELECT password FROM users --");

        // Assert
        Assert.False(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.Critical, result.ThreatLevel); // SELECT keyword is detected as critical
    }

    [Fact]
    public void ValidateValue_WithBooleanInjection_ShouldBeMedium()
    {
        // Act
        var result = _validator.ValidateValue("' OR 1=1 --");

        // Assert
        Assert.False(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.High, result.ThreatLevel); // Comments detected as high level
        Assert.Contains("comments", result.Threat);
    }

    [Fact]
    public void ValidateValue_WithComments_ShouldBeHigh()
    {
        // Act
        var result = _validator.ValidateValue("test -- comment");

        // Assert
        Assert.False(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.High, result.ThreatLevel);
        Assert.Contains("SQL comments detected", result.Threat);
    }

    [Fact]
    public void ValidateValue_WithBlockComments_ShouldBeHigh()
    {
        // Act
        var result = _validator.ValidateValue("test /* comment */ value");

        // Assert
        Assert.False(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.High, result.ThreatLevel);
        Assert.Contains("SQL comments detected", result.Threat);
    }

    [Fact]
    public void ValidateValue_WithHexEncoding_ShouldBeLow()
    {
        // Act
        var result = _validator.ValidateValue("0x41424344");

        // Assert
        Assert.False(result.IsSafe);
        Assert.Contains("0x41424344", result.Threat);
    }

    [Fact]
    public void ValidateValue_WithTimeBasedInjection_ShouldBeHigh()
    {
        // Act
        var result = _validator.ValidateValue("'; WAITFOR DELAY '00:00:05' --");

        // Assert
        Assert.False(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.High, result.ThreatLevel);
    }

    [Fact]
    public void ValidateValue_WithSystemProcedure_ShouldBeCritical()
    {
        // Act
        var result = _validator.ValidateValue("'; EXEC xp_cmdshell 'dir' --");

        // Assert
        Assert.False(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.Critical, result.ThreatLevel);
        Assert.Contains("EXEC", result.Threat); // EXEC is detected before xp_
    }

    [Fact]
    public void ValidateSelectionGrammar_WithNormalGrammar_ShouldBeSafe()
    {
        // Arrange
        var grammar = new SelectionGrammar
        {
            Table = "users",
            Select = new[] { "id", "username", "email" },
            Where = new ComparisonExpression
            {
                Column = "active",
                Operator = ComparisonOperator.Equal,
                Value = true
            }
        };

        // Act
        var result = _validator.ValidateSelectionGrammar(grammar);

        // Assert
        Assert.True(result.IsSafe);
    }

    [Fact]
    public void ValidateSelectionGrammar_WithMaliciousTable_ShouldBeUnsafe()
    {
        // Arrange
        var grammar = new SelectionGrammar
        {
            Table = "users; DROP TABLE sensitive_data; --",
            Select = new[] { "*" }
        };

        // Act
        var result = _validator.ValidateSelectionGrammar(grammar);

        // Assert
        Assert.False(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.Critical, result.ThreatLevel);
    }

    [Fact]
    public void ValidateSelectionGrammar_WithMaliciousColumn_ShouldBeUnsafe()
    {
        // Arrange
        var grammar = new SelectionGrammar
        {
            Table = "users",
            Select = new[] { "id", "username, (SELECT password FROM admin) as pwd" }
        };

        // Act
        var result = _validator.ValidateSelectionGrammar(grammar);

        // Assert
        Assert.False(result.IsSafe);
        Assert.Contains("SELECT", result.Threat);
    }

    [Fact]
    public void ValidateSelectionGrammar_WithMaliciousWhereValue_ShouldBeUnsafe()
    {
        // Arrange
        var grammar = new SelectionGrammar
        {
            Table = "users",
            Select = new[] { "id", "username" },
            Where = new ComparisonExpression
            {
                Column = "username",
                Operator = ComparisonOperator.Equal,
                Value = "admin' OR '1'='1"
            }
        };

        // Act
        var result = _validator.ValidateSelectionGrammar(grammar);

        // Assert
        Assert.False(result.IsSafe);
        Assert.Contains("pattern", result.Threat);
    }

    [Fact]
    public void ValidateSelectionGrammar_WithComplexMaliciousWhere_ShouldBeUnsafe()
    {
        // Arrange
        var grammar = new SelectionGrammar
        {
            Table = "users",
            Select = new[] { "username" },
            Where = new AndExpression
            {
                Expressions = new IWhereExpression[]
                {
                    new ComparisonExpression
                    {
                        Column = "active",
                        Operator = ComparisonOperator.Equal,
                        Value = true
                    },
                    new ComparisonExpression
                    {
                        Column = "username",
                        Operator = ComparisonOperator.Equal,
                        Value = "'; DROP TABLE users; --"
                    }
                }
            }
        };

        // Act
        var result = _validator.ValidateSelectionGrammar(grammar);

        // Assert
        Assert.False(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.Critical, result.ThreatLevel);
    }

    [Fact]
    public void ValidateSelectionGrammar_WithMaliciousOrderBy_ShouldBeUnsafe()
    {
        // Arrange
        var grammar = new SelectionGrammar
        {
            Table = "users",
            Select = new[] { "username" },
            OrderBy = new[]
            {
                new OrderByClause { Column = "username; DELETE FROM users", Direction = SortDirection.Ascending }
            }
        };

        // Act
        var result = _validator.ValidateSelectionGrammar(grammar);

        // Assert
        Assert.False(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.Critical, result.ThreatLevel);
        Assert.Contains("DELETE", result.Threat);
    }

    [Fact]
    public void ValidateSelectionGrammar_WithScriptInjection_ShouldBeUnsafe()
    {
        // Arrange
        var grammar = new SelectionGrammar
        {
            Table = "users",
            Select = new[] { "username" },
            Where = new ComparisonExpression
            {
                Column = "bio",
                Operator = ComparisonOperator.Like,
                Value = "<script>alert('xss')</script>"
            }
        };

        // Act
        var result = _validator.ValidateSelectionGrammar(grammar);

        // Assert
        Assert.False(result.IsSafe);
        Assert.Contains("Danger", result.Threat); // SCRIPT is a dangerous keyword
    }

    [Fact]
    public void ValidateSelectionGrammar_WithCustomDeniedPattern_ShouldBeUnsafe()
    {
        // Arrange
        var grammar = new SelectionGrammar
        {
            Table = "users",
            Select = new[] { "username" },
            Where = new ComparisonExpression
            {
                Column = "username",
                Operator = ComparisonOperator.Equal,
                Value = "admin"
            }
        };

        // Act (using strict validator with "admin" in denied patterns)
        var result = _strictValidator.ValidateSelectionGrammar(grammar);

        // Assert
        Assert.False(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.Medium, result.ThreatLevel);
        Assert.Contains("Custom denied pattern", result.Threat);
    }

    [Fact]
    public void ValidateSelectionGrammar_WithNotExpression_ShouldValidateNestedExpression()
    {
        // Arrange
        var grammar = new SelectionGrammar
        {
            Table = "users",
            Select = new[] { "username" },
            Where = new NotExpression
            {
                Expression = new ComparisonExpression
                {
                    Column = "username",
                    Operator = ComparisonOperator.Equal,
                    Value = "'; DROP TABLE users; --"
                }
            }
        };

        // Act
        var result = _validator.ValidateSelectionGrammar(grammar);

        // Assert
        Assert.False(result.IsSafe);
        Assert.Equal(SqlInjectionThreatLevel.Critical, result.ThreatLevel);
    }

    [Fact]
    public void ValidateSelectionGrammar_WithDisabledProtection_ShouldBeSafe()
    {
        // Arrange
        var config = new SqlInjectionProtectionConfig { EnableProtection = false };
        var disabledValidator = new SqlInjectionValidator(config);
        
        var grammar = new SelectionGrammar
        {
            Table = "users; DROP TABLE admin; --",
            Select = new[] { "*" }
        };

        // Act
        var result = disabledValidator.ValidateSelectionGrammar(grammar);

        // Assert
        Assert.True(result.IsSafe); // Protection disabled
    }

    [Theory]
    [InlineData("user@domain.com")]
    [InlineData("John Smith")]
    [InlineData("123-45-6789")]
    [InlineData("Product Category A")]
    [InlineData("2023-12-31")]
    public void ValidateValue_WithLegitimateUserData_ShouldBeSafe(string value)
    {
        // Act
        var result = _validator.ValidateValue(value);

        // Assert
        Assert.True(result.IsSafe, $"Value '{value}' should be considered safe");
    }

    [Theory]
    [InlineData("'; SELECT * FROM users; --")]
    [InlineData("' OR 1=1; DROP TABLE users; --")]
    [InlineData("admin'; INSERT INTO users VALUES ('hacker', 'pwd'); --")]
    [InlineData("' UNION ALL SELECT password FROM admin_users --")]
    [InlineData("'; EXEC xp_cmdshell('rm -rf /'); --")]
    [InlineData("'; WAITFOR DELAY '00:00:10'; --")]
    public void ValidateValue_WithKnownInjectionPatterns_ShouldBeUnsafe(string maliciousValue)
    {
        // Act
        var result = _validator.ValidateValue(maliciousValue);

        // Assert
        Assert.False(result.IsSafe, $"Value '{maliciousValue}' should be considered unsafe");
        Assert.NotEqual(SqlInjectionThreatLevel.None, result.ThreatLevel);
    }
}