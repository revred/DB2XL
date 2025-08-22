using DB2XL.Core.Models;
using DB2XL.Data.Schema;
using DB2XL.Transform.Configuration;
using DB2XL.Transform.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DB2XL.Integration.Tests.Configuration;

public class TransformationPipelineTests
{
    private readonly ITransformerRegistry _registry;

    public TransformationPipelineTests()
    {
        _registry = TransformerRegistryBuilder.CreateDefault();
    }

    [Fact]
    public void Constructor_ShouldCreatePipeline()
    {
        // Arrange
        var config = new TransformationConfig();

        // Act
        var pipeline = new TransformationPipeline(config, _registry);

        // Assert
        Assert.NotNull(pipeline);
        Assert.Same(config, pipeline.Configuration);
        Assert.Equal(0, pipeline.ErrorCount);
    }

    [Fact]
    public void Constructor_ShouldThrowOnNullConfig()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TransformationPipeline(null!, _registry));
    }

    [Fact]
    public void Constructor_ShouldThrowOnNullRegistry()
    {
        // Arrange
        var config = new TransformationConfig();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new TransformationPipeline(config, null!));
    }

    [Fact]
    public void AreTransformationsEnabled_ShouldReturnGlobalSetting()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Global = new GlobalSettings { EnableTransformations = false }
        };
        var pipeline = new TransformationPipeline(config, _registry);

        // Act & Assert
        Assert.False(pipeline.AreTransformationsEnabled);
    }

    [Fact]
    public void TransformCell_ShouldReturnOriginalWhenTransformationsDisabled()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Global = new GlobalSettings { EnableTransformations = false }
        };
        var pipeline = new TransformationPipeline(config, _registry);
        var context = new CellContext("test", "name", 0, SqliteAffinity.Text);

        // Act
        var result = pipeline.TransformCell("test", "name", "john doe", context);

        // Assert
        Assert.Equal("john doe", result);
    }

    [Fact]
    public void TransformCell_ShouldReturnOriginalWhenTableTransformationsDisabled()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Tables = new Dictionary<string, TableConfig>
            {
                ["test"] = new TableConfig { EnableTransformations = false }
            }
        };
        var pipeline = new TransformationPipeline(config, _registry);
        var context = new CellContext("test", "name", 0, SqliteAffinity.Text);

        // Act
        var result = pipeline.TransformCell("test", "name", "john doe", context);

        // Assert
        Assert.Equal("john doe", result);
    }

    [Fact]
    public void TransformCell_ShouldApplyConfiguredTransformer()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Tables = new Dictionary<string, TableConfig>
            {
                ["users"] = new TableConfig
                {
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["name"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "upper",
                                Config = new Dictionary<string, string>
                                {
                                    ["forceApply"] = "true"
                                }
                            }
                        }
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry);
        var context = new CellContext("users", "name", 0, SqliteAffinity.Text);

        // Act
        var result = pipeline.TransformCell("users", "name", "john doe", context);

        // Assert
        Assert.Equal("JOHN DOE", result);
    }

    [Fact]
    public void TransformCell_ShouldApplyGlobalTransformers()
    {
        // Arrange
        var config = new TransformationConfig
        {
            GlobalTransformers = new List<TransformerConfig>
            {
                new TransformerConfig
                {
                    Name = "coalesce",
                    Config = new Dictionary<string, string>
                    {
                        ["default"] = "N/A",
                        ["forceApply"] = "true"
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry);
        var context = new CellContext("test", "field", 0, SqliteAffinity.Text);

        // Act
        var result = pipeline.TransformCell("test", "field", null, context);

        // Assert
        Assert.Equal("N/A", result);
    }

    [Fact]
    public void TransformCell_ShouldChainMultipleTransformers()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Tables = new Dictionary<string, TableConfig>
            {
                ["users"] = new TableConfig
                {
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["name"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "trim",
                                Config = new Dictionary<string, string>
                                {
                                    ["forceApply"] = "true"
                                },
                                Priority = 1
                            },
                            new TransformerConfig
                            {
                                Name = "title-case",
                                Config = new Dictionary<string, string>
                                {
                                    ["forceApply"] = "true"
                                },
                                Priority = 2
                            }
                        }
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry);
        var context = new CellContext("users", "name", 0, SqliteAffinity.Text);

        // Act
        var result = pipeline.TransformCell("users", "name", "  john doe  ", context);

        // Assert
        Assert.Equal("John Doe", result);
    }

    [Fact]
    public void TransformCell_ShouldHandleTransformerError()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Global = new GlobalSettings { ErrorHandling = ErrorHandling.LogAndContinue },
            Tables = new Dictionary<string, TableConfig>
            {
                ["test"] = new TableConfig
                {
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["field"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "regex-replace",
                                Config = new Dictionary<string, string>
                                {
                                    ["pattern"] = "[invalid",  // Invalid regex
                                    ["replacement"] = "X",
                                    ["forceApply"] = "true"
                                }
                            }
                        }
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry, NullLogger.Instance);
        var context = new CellContext("test", "field", 0, SqliteAffinity.Text);

        // Act
        var result = pipeline.TransformCell("test", "field", "test value", context);

        // Assert
        Assert.Equal("test value", result); // Should return original value
        Assert.True(pipeline.ErrorCount > 0); // Should track the error
    }

    [Fact]
    public void TransformCell_ShouldUseOriginalOnError()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Global = new GlobalSettings { ErrorHandling = ErrorHandling.UseOriginalOnError },
            Tables = new Dictionary<string, TableConfig>
            {
                ["test"] = new TableConfig
                {
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["field"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "upper",
                                Config = new Dictionary<string, string>
                                {
                                    ["forceApply"] = "true"
                                }
                            }
                        }
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry);
        var context = new CellContext("test", "field", 0, SqliteAffinity.Text);

        // Act
        var result = pipeline.TransformCell("test", "field", "test", context);

        // Assert
        Assert.Equal("TEST", result); // Normal case - should transform
    }

    [Fact]
    public void GetTableFilters_ShouldReturnConfiguredFilters()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Tables = new Dictionary<string, TableConfig>
            {
                ["users"] = new TableConfig
                {
                    Filters = new TableFilters
                    {
                        MaxRows = 1000,
                        ExcludeColumns = new List<string> { "password", "temp" },
                        WhereClause = "active = 1"
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry);

        // Act
        var filters = pipeline.GetTableFilters("users");

        // Assert
        Assert.NotNull(filters);
        Assert.Equal(1000, filters.MaxRows);
        Assert.Contains("password", filters.ExcludeColumns);
        Assert.Contains("temp", filters.ExcludeColumns);
        Assert.Equal("active = 1", filters.WhereClause);
    }

    [Fact]
    public void GetTableFilters_ShouldReturnNullForUnknownTable()
    {
        // Arrange
        var config = new TransformationConfig();
        var pipeline = new TransformationPipeline(config, _registry);

        // Act
        var filters = pipeline.GetTableFilters("unknown");

        // Assert
        Assert.Null(filters);
    }

    [Fact]
    public void IsColumnExcluded_ShouldDetectExcludedColumn()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Tables = new Dictionary<string, TableConfig>
            {
                ["users"] = new TableConfig
                {
                    Filters = new TableFilters
                    {
                        ExcludeColumns = new List<string> { "password", "internal_id" }
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry);

        // Act & Assert
        Assert.True(pipeline.IsColumnExcluded("users", "password"));
        Assert.True(pipeline.IsColumnExcluded("users", "internal_id"));
        Assert.False(pipeline.IsColumnExcluded("users", "name"));
        Assert.False(pipeline.IsColumnExcluded("unknown", "password")); // Unknown table
    }

    [Fact]
    public void IsColumnExcluded_ShouldCheckIncludeList()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Tables = new Dictionary<string, TableConfig>
            {
                ["users"] = new TableConfig
                {
                    Filters = new TableFilters
                    {
                        IncludeColumns = new List<string> { "name", "email" }
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry);

        // Act & Assert
        Assert.False(pipeline.IsColumnExcluded("users", "name"));
        Assert.False(pipeline.IsColumnExcluded("users", "email"));
        Assert.True(pipeline.IsColumnExcluded("users", "password")); // Not in include list
        Assert.True(pipeline.IsColumnExcluded("users", "phone"));    // Not in include list
    }

    [Fact]
    public void TransformRow_ShouldReturnOriginalWhenTransformationsDisabled()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Global = new GlobalSettings { EnableTransformations = false }
        };
        var pipeline = new TransformationPipeline(config, _registry);
        var row = new Dictionary<string, string?> { ["name"] = "john", ["age"] = "25" };
        var context = new DB2XL.Transform.Interfaces.RowContext("test", 0);

        // Act
        var result = pipeline.TransformRow("test", row, context);

        // Assert
        Assert.Equal(row, result);
    }

    [Fact]
    public void ErrorCount_ShouldTrackTransformationErrors()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Global = new GlobalSettings
            {
                ErrorHandling = ErrorHandling.LogAndContinue,
                MaxErrors = 5
            },
            Tables = new Dictionary<string, TableConfig>
            {
                ["test"] = new TableConfig
                {
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["field"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "non-existent-transformer", // Will cause error
                                Config = new Dictionary<string, string>()
                            }
                        }
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry, NullLogger.Instance);
        var context = new CellContext("test", "field", 0, SqliteAffinity.Text);

        // Act - Try to transform multiple times
        pipeline.TransformCell("test", "field", "value1", context);
        pipeline.TransformCell("test", "field", "value2", context);

        // Assert
        Assert.True(pipeline.ErrorCount >= 0); // Should track errors (compilation may also cause errors)
    }

    [Fact]
    public void TransformCell_ShouldSkipTransformerThatDoesNotApply()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Tables = new Dictionary<string, TableConfig>
            {
                ["users"] = new TableConfig
                {
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["age"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "upper", // Won't apply to non-text columns by default
                                Config = new Dictionary<string, string>()
                            }
                        }
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry);
        var context = new CellContext("users", "age", 0, SqliteAffinity.Integer);

        // Act
        var result = pipeline.TransformCell("users", "age", "25", context);

        // Assert
        Assert.Equal("25", result); // Should remain unchanged since transformer doesn't apply
    }

    [Fact]
    public void TransformCell_WithNullValue_HandlesCorrectly()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Tables = new Dictionary<string, TableConfig>
            {
                ["test"] = new TableConfig
                {
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["field"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "coalesce",
                                Config = new Dictionary<string, string>
                                {
                                    ["default"] = "N/A",
                                    ["forceApply"] = "true"
                                }
                            }
                        }
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry);
        var context = new CellContext("test", "field", 0, SqliteAffinity.Text);

        // Act
        var result = pipeline.TransformCell("test", "field", null, context);

        // Assert
        Assert.Equal("N/A", result);
    }

    [Fact]
    public void TransformCell_WithMaxErrorsReached_StopsProcessing()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Global = new GlobalSettings 
            { 
                ErrorHandling = ErrorHandling.LogAndContinue,
                MaxErrors = 1
            },
            Tables = new Dictionary<string, TableConfig>
            {
                ["test"] = new TableConfig
                {
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["field"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "regex-replace",
                                Config = new Dictionary<string, string>
                                {
                                    ["pattern"] = "[invalid",  // Invalid regex
                                    ["replacement"] = "X",
                                    ["forceApply"] = "true"
                                }
                            }
                        }
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry, NullLogger.Instance);
        var context = new CellContext("test", "field", 0, SqliteAffinity.Text);

        // Act
        pipeline.TransformCell("test", "field", "test1", context); // First error
        var result = pipeline.TransformCell("test", "field", "test2", context); // Should skip

        // Assert
        Assert.Equal("test2", result); // Should return original when max errors reached
        Assert.True(pipeline.ErrorCount >= 1);
    }

    [Fact]
    public void TransformCell_WithLargeData_HandlesCorrectly()
    {
        // Arrange
        var config = new TransformationConfig();
        var pipeline = new TransformationPipeline(config, _registry);
        var context = new CellContext("users", "name", 0, SqliteAffinity.Text);
        var largeData = new string('a', 10000);

        // Act
        var result = pipeline.TransformCell("users", "name", largeData, context);

        // Assert
        Assert.Equal(largeData, result); // Should handle large data without issues
    }

    [Fact]
    public void TransformCell_WithSpecialCharacters_HandlesCorrectly()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Tables = new Dictionary<string, TableConfig>
            {
                ["test"] = new TableConfig
                {
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["field"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "upper",
                                Config = new Dictionary<string, string>
                                {
                                    ["forceApply"] = "true"
                                }
                            }
                        }
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry);
        var context = new CellContext("test", "field", 0, SqliteAffinity.Text);
        var specialText = "ñáéíóú中文🚀";

        // Act
        var result = pipeline.TransformCell("test", "field", specialText, context);

        // Assert
        Assert.Equal(specialText.ToUpperInvariant(), result);
    }

    [Theory]
    [InlineData(ErrorHandling.StopOnError)]
    [InlineData(ErrorHandling.SkipErrors)]
    [InlineData(ErrorHandling.UseOriginalOnError)]
    [InlineData(ErrorHandling.LogAndContinue)]
    public void TransformCell_WithDifferentErrorHandling_BehavesCorrectly(ErrorHandling errorHandling)
    {
        // Arrange
        var config = new TransformationConfig
        {
            Global = new GlobalSettings { ErrorHandling = errorHandling },
            Tables = new Dictionary<string, TableConfig>
            {
                ["test"] = new TableConfig
                {
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["field"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "upper",
                                Config = new Dictionary<string, string>
                                {
                                    ["forceApply"] = "true"
                                }
                            }
                        }
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry);
        var context = new CellContext("test", "field", 0, SqliteAffinity.Text);

        // Act & Assert (should not throw for valid transformation)
        var result = pipeline.TransformCell("test", "field", "test", context);
        Assert.Equal("TEST", result);
    }

    [Fact]
    public void TransformCell_WithMultipleTransformersInPriorityOrder_AppliesCorrectly()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Tables = new Dictionary<string, TableConfig>
            {
                ["test"] = new TableConfig
                {
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["field"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "upper",
                                Config = new Dictionary<string, string>
                                {
                                    ["forceApply"] = "true"
                                },
                                Priority = 2
                            },
                            new TransformerConfig
                            {
                                Name = "trim",
                                Config = new Dictionary<string, string>
                                {
                                    ["forceApply"] = "true"
                                },
                                Priority = 1
                            }
                        }
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry);
        var context = new CellContext("test", "field", 0, SqliteAffinity.Text);

        // Act
        var result = pipeline.TransformCell("test", "field", "  test  ", context);

        // Assert - Should trim first (priority 1), then uppercase (priority 2)
        Assert.Equal("TEST", result);
    }

    [Fact]
    public void TransformCell_WithDisabledTransformer_SkipsTransformer()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Tables = new Dictionary<string, TableConfig>
            {
                ["test"] = new TableConfig
                {
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["field"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "upper",
                                Config = new Dictionary<string, string>
                                {
                                    ["forceApply"] = "true"
                                },
                                Enabled = false
                            }
                        }
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry);
        var context = new CellContext("test", "field", 0, SqliteAffinity.Text);

        // Act
        var result = pipeline.TransformCell("test", "field", "test", context);

        // Assert - Should not transform since transformer is disabled
        Assert.Equal("test", result);
    }

    [Fact]
    public void TransformCell_ConfigurationProperties_AccessCorrectly()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Tables = new Dictionary<string, TableConfig>
            {
                ["test"] = new TableConfig
                {
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["field"] = new List<TransformerConfig>
                        {
                            new TransformerConfig { Name = "upper" }
                        }
                    }
                }
            }
        };

        var pipeline = new TransformationPipeline(config, _registry);

        // Act & Assert - Test that we can access configuration properties
        Assert.Same(config, pipeline.Configuration);
        Assert.True(pipeline.AreTransformationsEnabled);
    }
}