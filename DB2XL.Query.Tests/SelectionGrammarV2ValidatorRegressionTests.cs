using DB2XL.Query;
using Xunit;

namespace DB2XL.Query.Tests;

/// <summary>
/// Comprehensive regression tests for SelectionGrammarV2Validator to detect critical validation failures
/// </summary>
public class SelectionGrammarV2ValidatorRegressionTests
{
    private readonly SelectionGrammarV2Validator _validator;
    
    public SelectionGrammarV2ValidatorRegressionTests()
    {
        _validator = new SelectionGrammarV2Validator();
    }

    #region Basic Validation Tests

    [Fact]
    public void ValidateJson_ValidBasicQuery_ReturnsValid()
    {
        // Regression: Basic valid queries must pass validation
        var json = """
        {
            "table": "users"
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateJson_MissingTable_ReturnsInvalid()
    {
        // Critical: Missing table should be caught
        var json = """
        {
            "select": ["id", "name"]
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Missing or empty required property: 'table'"));
    }

    [Fact]
    public void ValidateJson_EmptyTable_ReturnsInvalid()
    {
        // Regression: Empty table names should be rejected
        var json = """
        {
            "table": ""
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Missing or empty required property: 'table'"));
    }

    [Fact]
    public void ValidateJson_InvalidJson_ReturnsInvalid()
    {
        // Critical: Malformed JSON should be caught
        var json = """
        {
            "table": "users"
            "invalid": json
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Invalid JSON structure"));
    }

    #endregion

    #region Attach Property Tests

    [Fact]
    public void ValidateJson_ValidAttach_ReturnsValid()
    {
        // Regression: Valid attach configurations should pass
        var json = """
        {
            "table": "main_table",
            "attach": [
                {
                    "alias": "external_db",
                    "type": "sqlite",
                    "path": "/path/to/external.db"
                }
            ]
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateJson_AttachNotArray_ReturnsInvalid()
    {
        // Regression: Attach must be an array
        var json = """
        {
            "table": "main_table",
            "attach": {
                "alias": "external_db",
                "type": "sqlite",
                "path": "/path/to/external.db"
            }
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Property 'attach' must be an array"));
    }

    [Fact]
    public void ValidateJson_AttachMissingRequiredProperties_ReturnsInvalid()
    {
        // Critical: Missing required attach properties should be caught
        var json = """
        {
            "table": "main_table",
            "attach": [
                {
                    "alias": "external_db"
                }
            ]
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Attach element missing required property: 'type'"));
        Assert.Contains(result.Errors, e => e.Contains("Attach element missing required property: 'path'"));
    }

    [Fact]
    public void ValidateJson_AttachInvalidType_ReturnsInvalid()
    {
        // Regression: Only supported attach types should be allowed
        var json = """
        {
            "table": "main_table",
            "attach": [
                {
                    "alias": "external_db",
                    "type": "mysql",
                    "path": "/path/to/external.db"
                }
            ]
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Unsupported attach type: 'mysql'"));
    }

    [Fact]
    public void ValidateJson_AttachInvalidAlias_ReturnsInvalid()
    {
        // Security: Invalid SQL identifiers in alias should be rejected
        var json = """
        {
            "table": "main_table",
            "attach": [
                {
                    "alias": "123invalid",
                    "type": "sqlite",
                    "path": "/path/to/external.db"
                }
            ]
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Invalid SQLite identifier"));
    }

    #endregion

    #region Join Property Tests

    [Fact]
    public void ValidateJson_ValidJoin_ReturnsValid()
    {
        // Regression: Valid join configurations should pass
        var json = """
        {
            "table": "orders",
            "joins": [
                {
                    "type": "inner",
                    "left": {
                        "table": "orders",
                        "col": "customer_id"
                    },
                    "right": {
                        "table": "customers",
                        "col": "id"
                    }
                }
            ]
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateJson_JoinInvalidType_ReturnsInvalid()
    {
        // Regression: Invalid join types should be rejected
        var json = """
        {
            "table": "orders",
            "joins": [
                {
                    "type": "cross",
                    "left": {
                        "table": "orders",
                        "col": "customer_id"
                    },
                    "right": {
                        "table": "customers",
                        "col": "id"
                    }
                }
            ]
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Invalid join type: 'cross'"));
    }

    [Fact]
    public void ValidateJson_JoinMissingTableReferences_ReturnsInvalid()
    {
        // Critical: Missing join table references should be caught
        var json = """
        {
            "table": "orders",
            "joins": [
                {
                    "type": "inner"
                }
            ]
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Join element missing required property: 'left'"));
        Assert.Contains(result.Errors, e => e.Contains("Join element missing required property: 'right'"));
    }

    [Fact]
    public void ValidateJson_JoinMissingColumns_ReturnsInvalid()
    {
        // Regression: Missing join columns should be caught
        var json = """
        {
            "table": "orders",
            "joins": [
                {
                    "type": "inner",
                    "left": {
                        "table": "orders"
                    },
                    "right": {
                        "table": "customers"
                    }
                }
            ]
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Join left reference missing required property: 'col'"));
        Assert.Contains(result.Errors, e => e.Contains("Join right reference missing required property: 'col'"));
    }

    #endregion

    #region Select Property Tests

    [Fact]
    public void ValidateJson_ValidSelect_ReturnsValid()
    {
        // Regression: Valid select configurations should pass
        var json = """
        {
            "table": "users",
            "select": ["id", "name", "email"]
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateJson_SelectWildcard_ReturnsValid()
    {
        // Regression: Wildcard select should be allowed
        var json = """
        {
            "table": "users",
            "select": ["*"]
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateJson_SelectNotArray_ReturnsInvalid()
    {
        // Regression: Select must be an array
        var json = """
        {
            "table": "users",
            "select": "id"
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Property 'select' must be an array"));
    }

    [Fact]
    public void ValidateJson_SelectEmptyColumn_ReturnsInvalid()
    {
        // Critical: Empty select columns should be rejected
        var json = """
        {
            "table": "users",
            "select": ["id", "", "name"]
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Select column cannot be empty"));
    }

    #endregion

    #region Where Property Tests

    [Fact]
    public void ValidateJson_ValidWhereComparison_ReturnsValid()
    {
        // Regression: Valid where comparisons should pass
        var json = """
        {
            "table": "users",
            "where": {
                "col": "age",
                "op": ">=",
                "val": 18
            }
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateJson_ValidWhereLogical_ReturnsValid()
    {
        // Regression: Valid logical expressions should pass
        var json = """
        {
            "table": "users",
            "where": {
                "and": [
                    {
                        "col": "age",
                        "op": ">=",
                        "val": 18
                    },
                    {
                        "col": "status",
                        "op": "=",
                        "val": "active"
                    }
                ]
            }
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateJson_WhereInvalidOperator_ReturnsInvalid()
    {
        // Security: Invalid operators should be rejected
        var json = """
        {
            "table": "users",
            "where": {
                "col": "name",
                "op": "EXEC",
                "val": "malicious"
            }
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Invalid comparison operator: 'EXEC'"));
    }

    [Fact]
    public void ValidateJson_WhereMissingRequiredProperties_ReturnsInvalid()
    {
        // Critical: Missing required where properties should be caught
        var json = """
        {
            "table": "users",
            "where": {
                "op": "=",
                "val": "test"
            }
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Comparison expression missing required property: 'col'"));
    }

    [Fact]
    public void ValidateJson_WhereInOperatorInvalidValue_ReturnsInvalid()
    {
        // Regression: IN operator must have array value
        var json = """
        {
            "table": "users",
            "where": {
                "col": "status",
                "op": "in",
                "val": "active"
            }
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Operator 'in' requires an array value"));
    }

    [Fact]
    public void ValidateJson_WhereBetweenInvalidValue_ReturnsInvalid()
    {
        // Regression: BETWEEN operator must have exactly 2 values
        var json = """
        {
            "table": "users",
            "where": {
                "col": "age",
                "op": "between",
                "val": [18]
            }
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Operator 'between' requires an array with exactly 2 values"));
    }

    [Fact]
    public void ValidateJson_WhereLogicalInsufficientExpressions_ReturnsInvalid()
    {
        // Regression: Logical operators need at least 2 expressions
        var json = """
        {
            "table": "users",
            "where": {
                "and": [
                    {
                        "col": "age",
                        "op": ">=",
                        "val": 18
                    }
                ]
            }
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Logical operator 'and' must contain at least 2 expressions"));
    }

    #endregion

    #region OrderBy Property Tests

    [Fact]
    public void ValidateJson_ValidOrderBy_ReturnsValid()
    {
        // Regression: Valid orderBy should pass
        var json = """
        {
            "table": "users",
            "orderBy": [
                {
                    "col": "name",
                    "dir": "asc"
                }
            ]
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateJson_OrderByInvalidDirection_ReturnsInvalid()
    {
        // Regression: Invalid sort directions should be rejected
        var json = """
        {
            "table": "users",
            "orderBy": [
                {
                    "col": "name",
                    "dir": "random"
                }
            ]
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Invalid sort direction: 'random'"));
    }

    [Fact]
    public void ValidateJson_OrderByMissingColumn_ReturnsInvalid()
    {
        // Critical: Missing orderBy column should be caught
        var json = """
        {
            "table": "users",
            "orderBy": [
                {
                    "dir": "asc"
                }
            ]
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("OrderBy element missing required property: 'col'"));
    }

    #endregion

    #region Pagination Tests

    [Fact]
    public void ValidateJson_ValidPagination_ReturnsValid()
    {
        // Regression: Valid pagination should pass
        var json = """
        {
            "table": "users",
            "limit": 100,
            "offset": 50
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateJson_InvalidLimitType_ReturnsInvalid()
    {
        // Regression: Limit must be positive integer
        var json = """
        {
            "table": "users",
            "limit": "100"
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Property 'limit' must be a positive integer"));
    }

    [Fact]
    public void ValidateJson_NegativeLimit_ReturnsInvalid()
    {
        // Critical: Negative limits should be rejected
        var json = """
        {
            "table": "users",
            "limit": -10
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Property 'limit' must be a positive integer"));
    }

    [Fact]
    public void ValidateJson_OffsetWithoutLimit_ReturnsInvalid()
    {
        // Regression: Offset requires limit
        var json = """
        {
            "table": "users",
            "offset": 50
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Property 'offset' requires 'limit' to be specified"));
    }

    [Fact]
    public void ValidateJson_LargeLimitWarning_ReturnsWarning()
    {
        // Performance: Large limits should generate warnings
        var json = """
        {
            "table": "users",
            "limit": 2000000
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, w => w.Contains("Large limit value") && w.Contains("may impact performance"));
    }

    #endregion

    #region Security Tests

    [Fact]
    public void ValidateJson_InvalidTableIdentifier_ReturnsInvalid()
    {
        // Security: Invalid SQL identifiers should be rejected
        var json = """
        {
            "table": "users; DROP TABLE users; --"
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Invalid SQLite identifier"));
    }

    [Fact]
    public void ValidateJson_LongTableName_ReturnsInvalid()
    {
        // Security: Excessively long identifiers should be rejected
        var longName = new string('a', 65); // 65 characters
        var json = $$"""
        {
            "table": "{{longName}}"
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("exceeds maximum length of 64 characters"));
    }

    [Fact]
    public void ValidateJson_SqlKeywordAsTable_ReturnsValid()
    {
        // Regression: SQL keywords should be allowed as identifiers (will be quoted)
        var json = """
        {
            "table": "select"
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ValidateJson_ComplexNestedQuery_ReturnsValid()
    {
        // Regression: Complex valid queries should pass
        var json = """
        {
            "table": "orders",
            "attach": [
                {
                    "alias": "customer_db",
                    "type": "sqlite",
                    "path": "/path/to/customers.db"
                }
            ],
            "joins": [
                {
                    "type": "left",
                    "left": {
                        "table": "orders",
                        "col": "customer_id"
                    },
                    "right": {
                        "table": "customers",
                        "col": "id"
                    }
                }
            ],
            "select": ["orders.id", "orders.total", "customers.name"],
            "where": {
                "and": [
                    {
                        "col": "orders.total",
                        "op": ">=",
                        "val": 100
                    },
                    {
                        "or": [
                            {
                                "col": "customers.status",
                                "op": "=",
                                "val": "premium"
                            },
                            {
                                "col": "orders.priority",
                                "op": "=",
                                "val": "high"
                            }
                        ]
                    }
                ]
            },
            "orderBy": [
                {
                    "col": "orders.total",
                    "dir": "desc"
                }
            ],
            "limit": 50,
            "offset": 0
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateJson_EmptyJsonObject_ReturnsInvalid()
    {
        // Edge case: Empty JSON should be invalid
        var json = "{}";
        
        var result = _validator.ValidateJson(json);
        
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Missing or empty required property: 'table'"));
    }

    [Fact]
    public void ValidateJson_NullOperatorValue_HandlesCorrectly()
    {
        // Edge case: Null-checking operators should handle missing values
        var json = """
        {
            "table": "users",
            "where": {
                "col": "deleted_at",
                "op": "isNull"
            }
        }
        """;
        
        var result = _validator.ValidateJson(json);
        
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    #endregion
}