using DB2XL;
using DB2XL.Configuration;
using DB2XL.Transformers;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;
using ClosedXML.Excel;

namespace SqliteXport.Tests;

public class TransformationIntegrationTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly string _tempExcelPath;

    public TransformationIntegrationTests()
    {
        _tempDbPath = Path.GetTempFileName() + ".db";
        _tempExcelPath = Path.GetTempFileName() + ".xlsx";
    }

    public void Dispose()
    {
        try
        {
            // Force garbage collection to ensure database connections are released
            GC.Collect();
            GC.WaitForPendingFinalizers();
            
            if (File.Exists(_tempDbPath))
                File.Delete(_tempDbPath);
            if (File.Exists(_tempExcelPath))
                File.Delete(_tempExcelPath);
        }
        catch (IOException)
        {
            // Ignore file deletion errors in tests - files might still be locked
            // This is acceptable for temporary test files
        }
    }

    [Fact]
    public void Export_WithTransformations_ShouldApplyTransformationsToExcelOutput()
    {
        // Arrange: Create test database
        CreateTestDatabase();

        // Create transformation configuration
        var transformationConfig = new TransformationConfig
        {
            Version = "1.0",
            Global = new GlobalSettings
            {
                EnableTransformations = true,
                ErrorHandling = ErrorHandling.LogAndContinue
            },
            Tables = new Dictionary<string, TableConfig>
            {
                ["users"] = new TableConfig
                {
                    EnableTransformations = true,
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
                        },
                        ["email"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "mask",
                                Config = new Dictionary<string, string>
                                {
                                    ["type"] = "email",
                                    ["forceApply"] = "true"
                                }
                            }
                        }
                    }
                }
            }
        };

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        // Act: Export with transformations
        SqliteToExcel.Export(_tempDbPath, _tempExcelPath, options);

        // Assert: Verify transformations were applied
        using var workbook = new XLWorkbook(_tempExcelPath);
        
        // Check that users sheet exists
        Assert.True(workbook.Worksheets.Any(ws => ws.Name == "users"));
        var usersSheet = workbook.Worksheet("users");

        // Verify header row
        Assert.Equal("id", usersSheet.Cell(1, 1).Value.ToString());
        Assert.Equal("name", usersSheet.Cell(1, 2).Value.ToString());
        Assert.Equal("email", usersSheet.Cell(1, 3).Value.ToString());
        Assert.Equal("age", usersSheet.Cell(1, 4).Value.ToString());

        // Verify transformed data
        // Name should be uppercase (upper transformer)
        Assert.Equal("JOHN DOE", usersSheet.Cell(2, 2).Value.ToString());
        Assert.Equal("JANE SMITH", usersSheet.Cell(3, 2).Value.ToString());

        // Email should be masked (mask transformer)
        var email1 = usersSheet.Cell(2, 3).Value.ToString();
        var email2 = usersSheet.Cell(3, 3).Value.ToString();
        
        // Basic email masking checks (exact format depends on implementation)
        Assert.Contains("@", email1);
        Assert.Contains("@", email2);
        Assert.DoesNotContain("john.doe@example.com", email1); // Original should be masked
        Assert.DoesNotContain("jane.smith@test.org", email2); // Original should be masked

        // Age should be unchanged (no transformer applied)
        Assert.Equal("30", usersSheet.Cell(2, 4).Value.ToString());
        Assert.Equal("25", usersSheet.Cell(3, 4).Value.ToString());
    }

    [Fact]
    public void Export_WithTransformationsDisabled_ShouldNotApplyTransformations()
    {
        // Arrange: Create test database
        CreateTestDatabase();

        // Create transformation configuration with transformations disabled
        var transformationConfig = new TransformationConfig
        {
            Version = "1.0",
            Global = new GlobalSettings
            {
                EnableTransformations = false, // Disabled
                ErrorHandling = ErrorHandling.LogAndContinue
            },
            Tables = new Dictionary<string, TableConfig>
            {
                ["users"] = new TableConfig
                {
                    EnableTransformations = true,
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["name"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "upper",
                                Config = new Dictionary<string, string>()
                            }
                        }
                    }
                }
            }
        };

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        // Act: Export with transformations disabled
        SqliteToExcel.Export(_tempDbPath, _tempExcelPath, options);

        // Assert: Verify transformations were NOT applied
        using var workbook = new XLWorkbook(_tempExcelPath);
        var usersSheet = workbook.Worksheet("users");

        // Name should remain original (transformations disabled)
        Assert.Equal("John Doe", usersSheet.Cell(2, 2).Value.ToString());
        Assert.Equal("Jane Smith", usersSheet.Cell(3, 2).Value.ToString());
    }

    [Fact]
    public void Export_WithoutTransformationConfig_ShouldWorkNormally()
    {
        // Arrange: Create test database
        CreateTestDatabase();

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true
            // No TransformationConfig provided
        };

        // Act: Export without transformations
        SqliteToExcel.Export(_tempDbPath, _tempExcelPath, options);

        // Assert: Verify normal export functionality
        using var workbook = new XLWorkbook(_tempExcelPath);
        
        Assert.True(workbook.Worksheets.Any(ws => ws.Name == "users"));
        var usersSheet = workbook.Worksheet("users");

        // Data should be original
        Assert.Equal("John Doe", usersSheet.Cell(2, 2).Value.ToString());
        Assert.Equal("jane.smith@test.org", usersSheet.Cell(3, 3).Value.ToString());
    }

    [Fact]
    public void Export_WithTransformations_ShouldIncludeTransformationMetadata()
    {
        // Arrange: Create test database
        CreateTestDatabase();

        var transformationConfig = new TransformationConfig
        {
            Version = "1.0",
            Global = new GlobalSettings
            {
                EnableTransformations = true,
                ErrorHandling = ErrorHandling.LogAndContinue
            }
        };

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            IncludeMetadataSheet = true,
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        // Act: Export with transformations
        SqliteToExcel.Export(_tempDbPath, _tempExcelPath, options);

        // Assert: Verify metadata sheet includes transformation info
        using var workbook = new XLWorkbook(_tempExcelPath);
        
        Assert.True(workbook.Worksheets.Any(ws => ws.Name == "_Export_Metadata"));
        var metaSheet = workbook.Worksheet("_Export_Metadata");

        // Look for transformation-related metadata
        bool foundTransformationEnabled = false;
        bool foundTransformationErrors = false;
        bool foundConfigVersion = false;

        var lastRow = metaSheet.LastRowUsed()?.RowNumber() ?? 0;
        for (int row = 1; row <= lastRow; row++)
        {
            var labelCell = metaSheet.Cell(row, 1).Value.ToString();
            if (labelCell.Contains("Transformations Enabled"))
                foundTransformationEnabled = true;
            else if (labelCell.Contains("Transformation Errors"))
                foundTransformationErrors = true;
            else if (labelCell.Contains("Configuration Version"))
                foundConfigVersion = true;
        }

        Assert.True(foundTransformationEnabled, "Metadata should include 'Transformations Enabled' information");
        Assert.True(foundTransformationErrors, "Metadata should include 'Transformation Errors' count");
        Assert.True(foundConfigVersion, "Metadata should include 'Configuration Version' information");
    }

    [Fact]
    public void Export_WithColumnFiltering_ShouldRespectExcludedColumns()
    {
        // Arrange: Create test database
        CreateTestDatabase();

        var transformationConfig = new TransformationConfig
        {
            Version = "1.0",
            Global = new GlobalSettings
            {
                EnableTransformations = true,
                ErrorHandling = ErrorHandling.LogAndContinue
            },
            Tables = new Dictionary<string, TableConfig>
            {
                ["users"] = new TableConfig
                {
                    EnableTransformations = true,
                    Filters = new TableFilters
                    {
                        ExcludeColumns = new List<string> { "email" } // Exclude email from transformations
                    },
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
                        },
                        ["email"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "mask", // This should be ignored due to column exclusion
                                Config = new Dictionary<string, string>
                                {
                                    ["type"] = "email",
                                    ["forceApply"] = "true"
                                }
                            }
                        }
                    }
                }
            }
        };

        var options = new SqliteToExcelOptions
        {
            WriteAllAsText = true,
            TransformationConfig = transformationConfig,
            TransformerRegistry = TransformerRegistryBuilder.CreateDefault()
        };

        // Act: Export with column filtering
        SqliteToExcel.Export(_tempDbPath, _tempExcelPath, options);

        // Assert: Verify excluded columns are not transformed
        using var workbook = new XLWorkbook(_tempExcelPath);
        var usersSheet = workbook.Worksheet("users");

        // Name should be transformed (not excluded)
        Assert.Equal("JOHN DOE", usersSheet.Cell(2, 2).Value.ToString());

        // Email should NOT be transformed (excluded)
        Assert.Equal("john.doe@example.com", usersSheet.Cell(2, 3).Value.ToString());
        Assert.Equal("jane.smith@test.org", usersSheet.Cell(3, 3).Value.ToString());
    }

    private void CreateTestDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_tempDbPath};");
        connection.Open();

        // Create users table
        using var command = connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE users (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                email TEXT NOT NULL,
                age INTEGER
            );
            
            INSERT INTO users (name, email, age) VALUES 
            ('John Doe', 'john.doe@example.com', 30),
            ('Jane Smith', 'jane.smith@test.org', 25);
        ";
        command.ExecuteNonQuery();
    }
}