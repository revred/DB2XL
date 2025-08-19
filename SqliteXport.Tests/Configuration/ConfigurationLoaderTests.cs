using DB2XL.Configuration;
using Xunit;
using System.Text.Json;

namespace SqliteXport.Tests.Configuration;

public class ConfigurationLoaderTests
{
    [Fact]
    public void CreateDefaultConfig_ShouldCreateValidConfiguration()
    {
        // Act
        var config = ConfigurationLoader.CreateDefaultConfig();

        // Assert
        Assert.NotNull(config);
        Assert.Equal("1.0", config.Version);
        Assert.True(config.Global.EnableTransformations);
        Assert.Equal(ErrorHandling.LogAndContinue, config.Global.ErrorHandling);
        Assert.Equal(100, config.Global.MaxErrors);
        Assert.Equal(10000, config.Global.Performance.BatchSize);
        
        // Should have sample tables and transformers
        Assert.True(config.Tables.Count > 0);
        Assert.True(config.GlobalTransformers.Count > 0);
    }

    [Fact]
    public void LoadFromJson_ShouldParseValidJson()
    {
        // Arrange
        var json = @"{
            ""version"": ""1.0"",
            ""global"": {
                ""enableTransformations"": true,
                ""errorHandling"": ""LogAndContinue"",
                ""maxErrors"": 50,
                ""performance"": {
                    ""batchSize"": 5000,
                    ""enableParallelProcessing"": false
                }
            },
            ""tables"": {
                ""users"": {
                    ""enableTransformations"": true,
                    ""columns"": {
                        ""email"": [
                            {
                                ""name"": ""mask"",
                                ""config"": {
                                    ""type"": ""email""
                                },
                                ""priority"": 10,
                                ""enabled"": true
                            }
                        ]
                    }
                }
            }
        }";

        // Act
        var config = ConfigurationLoader.LoadFromJson(json);

        // Assert
        Assert.Equal("1.0", config.Version);
        Assert.True(config.Global.EnableTransformations);
        Assert.Equal(50, config.Global.MaxErrors);
        Assert.Equal(5000, config.Global.Performance.BatchSize);
        Assert.False(config.Global.Performance.EnableParallelProcessing);
        
        Assert.True(config.Tables.ContainsKey("users"));
        var usersTable = config.Tables["users"];
        Assert.True(usersTable.EnableTransformations);
        Assert.True(usersTable.Columns.ContainsKey("email"));
        
        var emailTransformers = usersTable.Columns["email"];
        Assert.Single(emailTransformers);
        Assert.Equal("mask", emailTransformers[0].Name);
        Assert.Equal("email", emailTransformers[0].Config["type"]);
        Assert.Equal(10, emailTransformers[0].Priority);
        Assert.True(emailTransformers[0].Enabled);
    }

    [Fact]
    public void LoadFromYaml_ShouldParseValidYaml()
    {
        // Arrange
        var yaml = @"
version: '1.0'
global:
  enableTransformations: true
  errorHandling: StopOnError
  maxErrors: 25
  performance:
    batchSize: 2500
    enableParallelProcessing: true
    maxDegreeOfParallelism: 4
tables:
  products:
    enableTransformations: true
    columns:
      price:
        - name: 'coalesce'
          config:
            default: '0.00'
          priority: 5
          enabled: true
    filters:
      maxRows: 1000
      excludeColumns:
        - temp_field
        - internal_id
";

        // Act
        var config = ConfigurationLoader.LoadFromYaml(yaml);

        // Assert
        Assert.Equal("1.0", config.Version);
        Assert.True(config.Global.EnableTransformations);
        Assert.Equal(ErrorHandling.StopOnError, config.Global.ErrorHandling);
        Assert.Equal(25, config.Global.MaxErrors);
        Assert.Equal(2500, config.Global.Performance.BatchSize);
        Assert.True(config.Global.Performance.EnableParallelProcessing);
        Assert.Equal(4, config.Global.Performance.MaxDegreeOfParallelism);
        
        Assert.True(config.Tables.ContainsKey("products"));
        var productsTable = config.Tables["products"];
        Assert.True(productsTable.EnableTransformations);
        Assert.Equal(1000, productsTable.Filters.MaxRows);
        Assert.Contains("temp_field", productsTable.Filters.ExcludeColumns);
        Assert.Contains("internal_id", productsTable.Filters.ExcludeColumns);
    }

    [Fact]
    public void SaveToJson_ShouldProduceValidJson()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Version = "1.0",
            Global = new GlobalSettings
            {
                EnableTransformations = true,
                ErrorHandling = ErrorHandling.LogAndContinue
            }
        };

        // Act
        var json = ConfigurationLoader.SaveToJson(config);

        // Assert
        Assert.NotNull(json);
        Assert.Contains("\"version\": \"1.0\"", json);
        Assert.Contains("\"enableTransformations\": true", json);
        
        // Should be able to parse it back
        var parsedConfig = ConfigurationLoader.LoadFromJson(json);
        Assert.Equal("1.0", parsedConfig.Version);
        Assert.True(parsedConfig.Global.EnableTransformations);
    }

    [Fact]
    public void SaveToYaml_ShouldProduceValidYaml()
    {
        // Arrange
        var config = new TransformationConfig
        {
            Version = "1.0",
            Global = new GlobalSettings
            {
                EnableTransformations = false,
                ErrorHandling = ErrorHandling.SkipErrors,
                MaxErrors = 10
            }
        };

        // Act
        var yaml = ConfigurationLoader.SaveToYaml(config);

        // Assert
        Assert.NotNull(yaml);
        Assert.Contains("version: 1.0", yaml);
        Assert.Contains("enableTransformations: false", yaml);
        Assert.Contains("errorHandling: SkipErrors", yaml);
        
        // Should be able to parse it back
        var parsedConfig = ConfigurationLoader.LoadFromYaml(yaml);
        Assert.Equal("1.0", parsedConfig.Version);
        Assert.False(parsedConfig.Global.EnableTransformations);
        Assert.Equal(ErrorHandling.SkipErrors, parsedConfig.Global.ErrorHandling);
        Assert.Equal(10, parsedConfig.Global.MaxErrors);
    }

    [Fact]
    public void LoadFromFile_ShouldDetectJsonFormat()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".json";
        var config = ConfigurationLoader.CreateDefaultConfig();
        
        try
        {
            ConfigurationLoader.SaveToFile(config, tempFile);

            // Act
            var loadedConfig = ConfigurationLoader.LoadFromFile(tempFile);

            // Assert
            Assert.Equal(config.Version, loadedConfig.Version);
            Assert.Equal(config.Global.EnableTransformations, loadedConfig.Global.EnableTransformations);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadFromFile_ShouldDetectYamlFormat()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".yaml";
        var config = new TransformationConfig
        {
            Version = "1.0",
            Global = new GlobalSettings { EnableTransformations = false }
        };
        
        try
        {
            ConfigurationLoader.SaveToFile(config, tempFile);

            // Act
            var loadedConfig = ConfigurationLoader.LoadFromFile(tempFile);

            // Assert
            Assert.Equal("1.0", loadedConfig.Version);
            Assert.False(loadedConfig.Global.EnableTransformations);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadFromFile_ShouldThrowOnNonExistentFile()
    {
        // Act & Assert
        var ex = Assert.Throws<FileNotFoundException>(() =>
            ConfigurationLoader.LoadFromFile("non-existent-file.json"));
        
        Assert.Contains("Configuration file not found", ex.Message);
    }

    [Fact]
    public void LoadFromFile_ShouldThrowOnUnsupportedFormat()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".xml";
        
        try
        {
            File.WriteAllText(tempFile, "<config></config>");

            // Act & Assert
            var ex = Assert.Throws<ConfigurationException>(() =>
                ConfigurationLoader.LoadFromFile(tempFile));
            
            Assert.Contains("Unsupported configuration file format", ex.Message);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadFromJson_ShouldThrowOnInvalidJson()
    {
        // Arrange
        var invalidJson = @"{ ""version"": ""1.0"", invalid }";

        // Act & Assert
        var ex = Assert.Throws<ConfigurationException>(() =>
            ConfigurationLoader.LoadFromJson(invalidJson));
        
        Assert.Contains("Invalid JSON configuration", ex.Message);
    }

    [Fact]
    public void LoadFromYaml_ShouldThrowOnInvalidYaml()
    {
        // Arrange
        var invalidYaml = @"
version: 1.0
global: [
  - this is invalid yaml syntax with mismatched brackets
  - another item
  {
invalid: structure
";

        // Act & Assert
        var ex = Assert.Throws<ConfigurationException>(() =>
            ConfigurationLoader.LoadFromYaml(invalidYaml));
        
        Assert.Contains("Invalid YAML configuration", ex.Message);
    }

    [Theory]
    [InlineData(-1, "Global.MaxErrors must be >= 0")]
    [InlineData(-5, "Global.MaxErrors must be >= 0")]
    public void LoadFromJson_ShouldValidateGlobalMaxErrors(int maxErrors, string expectedError)
    {
        // Arrange
        var json = JsonSerializer.Serialize(new TransformationConfig
        {
            Version = "1.0",
            Global = new GlobalSettings { MaxErrors = maxErrors }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        // Act & Assert
        var ex = Assert.Throws<ConfigurationException>(() =>
            ConfigurationLoader.LoadFromJson(json));
        
        Assert.Contains(expectedError, ex.Message);
    }

    [Theory]
    [InlineData(0, "Global.Performance.BatchSize must be > 0")]
    [InlineData(-1, "Global.Performance.BatchSize must be > 0")]
    public void LoadFromJson_ShouldValidateBatchSize(int batchSize, string expectedError)
    {
        // Arrange
        var json = JsonSerializer.Serialize(new TransformationConfig
        {
            Version = "1.0",
            Global = new GlobalSettings
            {
                Performance = new PerformanceSettings { BatchSize = batchSize }
            }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        // Act & Assert
        var ex = Assert.Throws<ConfigurationException>(() =>
            ConfigurationLoader.LoadFromJson(json));
        
        Assert.Contains(expectedError, ex.Message);
    }

    [Fact]
    public void LoadFromJson_ShouldValidateEmptyTransformerName()
    {
        // Arrange
        var json = @"{
            ""version"": ""1.0"",
            ""globalTransformers"": [
                {
                    ""name"": """",
                    ""config"": {}
                }
            ]
        }";

        // Act & Assert
        var ex = Assert.Throws<ConfigurationException>(() =>
            ConfigurationLoader.LoadFromJson(json));
        
        Assert.Contains("Transformer name is required", ex.Message);
    }

    [Fact]
    public void LoadFromJson_ShouldValidateNegativePriority()
    {
        // Arrange
        var json = @"{
            ""version"": ""1.0"",
            ""globalTransformers"": [
                {
                    ""name"": ""test"",
                    ""priority"": -1,
                    ""config"": {}
                }
            ]
        }";

        // Act & Assert
        var ex = Assert.Throws<ConfigurationException>(() =>
            ConfigurationLoader.LoadFromJson(json));
        
        Assert.Contains("Priority must be >= 0", ex.Message);
    }

    [Fact]
    public void SaveToFile_ShouldCreateDirectoryIfNeeded()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tempFile = Path.Combine(tempDir, "config.json");
        var config = new TransformationConfig { Version = "1.0" };
        
        try
        {
            Assert.False(Directory.Exists(tempDir));

            // Act
            ConfigurationLoader.SaveToFile(config, tempFile);

            // Assert
            Assert.True(Directory.Exists(tempDir));
            Assert.True(File.Exists(tempFile));
            
            var loadedConfig = ConfigurationLoader.LoadFromFile(tempFile);
            Assert.Equal("1.0", loadedConfig.Version);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}