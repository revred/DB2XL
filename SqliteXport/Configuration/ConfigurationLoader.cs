using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DB2XL.Configuration;

/// <summary>
/// Loads transformation configurations from JSON or YAML files
/// </summary>
public static class ConfigurationLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    /// <summary>
    /// Loads configuration from a file, auto-detecting format based on extension
    /// </summary>
    /// <param name="filePath">Path to the configuration file</param>
    /// <returns>Parsed transformation configuration</returns>
    /// <exception cref="FileNotFoundException">Configuration file not found</exception>
    /// <exception cref="ConfigurationException">Invalid configuration format</exception>
    public static TransformationConfig LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Configuration file not found: {filePath}");
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var content = File.ReadAllText(filePath);

        try
        {
            return extension switch
            {
                ".json" => LoadFromJson(content),
                ".yaml" or ".yml" => LoadFromYaml(content),
                _ => throw new ConfigurationException($"Unsupported configuration file format: {extension}")
            };
        }
        catch (Exception ex) when (!(ex is ConfigurationException))
        {
            throw new ConfigurationException($"Failed to parse configuration file '{filePath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Loads configuration from JSON string
    /// </summary>
    /// <param name="json">JSON configuration content</param>
    /// <returns>Parsed transformation configuration</returns>
    public static TransformationConfig LoadFromJson(string json)
    {
        try
        {
            var config = JsonSerializer.Deserialize<TransformationConfig>(json, JsonOptions);
            if (config == null)
            {
                throw new ConfigurationException("Configuration deserialized to null");
            }

            ValidateConfiguration(config);
            return config;
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"Invalid JSON configuration: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Loads configuration from YAML string
    /// </summary>
    /// <param name="yaml">YAML configuration content</param>
    /// <returns>Parsed transformation configuration</returns>
    public static TransformationConfig LoadFromYaml(string yaml)
    {
        try
        {
            var config = YamlDeserializer.Deserialize<TransformationConfig>(yaml);
            if (config == null)
            {
                throw new ConfigurationException("Configuration deserialized to null");
            }

            ValidateConfiguration(config);
            return config;
        }
        catch (Exception ex) when (!(ex is ConfigurationException))
        {
            throw new ConfigurationException($"Invalid YAML configuration: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Saves configuration to a file
    /// </summary>
    /// <param name="config">Configuration to save</param>
    /// <param name="filePath">Target file path</param>
    public static void SaveToFile(TransformationConfig config, string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var content = extension switch
        {
            ".json" => SaveToJson(config),
            ".yaml" or ".yml" => SaveToYaml(config),
            _ => throw new ConfigurationException($"Unsupported configuration file format: {extension}")
        };

        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, content);
    }

    /// <summary>
    /// Serializes configuration to JSON string
    /// </summary>
    /// <param name="config">Configuration to serialize</param>
    /// <returns>JSON representation</returns>
    public static string SaveToJson(TransformationConfig config)
    {
        return JsonSerializer.Serialize(config, JsonOptions);
    }

    /// <summary>
    /// Serializes configuration to YAML string
    /// </summary>
    /// <param name="config">Configuration to serialize</param>
    /// <returns>YAML representation</returns>
    public static string SaveToYaml(TransformationConfig config)
    {
        return YamlSerializer.Serialize(config);
    }

    /// <summary>
    /// Creates a default configuration template
    /// </summary>
    /// <returns>Default configuration with examples</returns>
    public static TransformationConfig CreateDefaultConfig()
    {
        return new TransformationConfig
        {
            Version = "1.0",
            Global = new GlobalSettings
            {
                EnableTransformations = true,
                ErrorHandling = ErrorHandling.LogAndContinue,
                MaxErrors = 100,
                Performance = new PerformanceSettings
                {
                    BatchSize = 10000,
                    EnableParallelProcessing = true,
                    MaxDegreeOfParallelism = 0
                }
            },
            GlobalTransformers = new List<TransformerConfig>
            {
                new TransformerConfig
                {
                    Name = "coalesce",
                    Config = new Dictionary<string, string>
                    {
                        ["default"] = "N/A",
                        ["treatEmptyAsNull"] = "true"
                    },
                    Conditions = new TransformerConditions
                    {
                        ColumnPatterns = new List<string> { "*_optional", "*_nullable" }
                    },
                    Priority = 200,
                    Enabled = false // Example disabled transformer
                }
            },
            Tables = new Dictionary<string, TableConfig>
            {
                ["users"] = new TableConfig
                {
                    EnableTransformations = true,
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["email"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "mask",
                                Config = new Dictionary<string, string>
                                {
                                    ["type"] = "email",
                                    ["maskChar"] = "*"
                                }
                            }
                        },
                        ["phone"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "mask",
                                Config = new Dictionary<string, string>
                                {
                                    ["type"] = "phone"
                                }
                            }
                        },
                        ["full_name"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "title-case",
                                Config = new Dictionary<string, string>
                                {
                                    ["culture"] = "current"
                                }
                            }
                        }
                    },
                    Filters = new TableFilters
                    {
                        MaxRows = 0, // No limit
                        ExcludeColumns = new List<string> { "internal_id", "temp_field" }
                    }
                },
                ["log_entries"] = new TableConfig
                {
                    EnableTransformations = true,
                    Columns = new Dictionary<string, List<TransformerConfig>>
                    {
                        ["timestamp"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "epoch",
                                Config = new Dictionary<string, string>
                                {
                                    ["unit"] = "s",
                                    ["format"] = "yyyy-MM-dd HH:mm:ss",
                                    ["timezone"] = "UTC"
                                }
                            }
                        },
                        ["json_data"] = new List<TransformerConfig>
                        {
                            new TransformerConfig
                            {
                                Name = "json-pretty",
                                Config = new Dictionary<string, string>
                                {
                                    ["indent"] = "  ",
                                    ["maxDepth"] = "10"
                                }
                            }
                        }
                    }
                }
            }
        };
    }

    /// <summary>
    /// Validates configuration for common issues
    /// </summary>
    /// <param name="config">Configuration to validate</param>
    /// <exception cref="ConfigurationException">Validation failed</exception>
    private static void ValidateConfiguration(TransformationConfig config)
    {
        var errors = new List<string>();

        // Validate version
        if (string.IsNullOrEmpty(config.Version))
        {
            errors.Add("Configuration version is required");
        }

        // Validate global settings
        if (config.Global.MaxErrors < 0)
        {
            errors.Add("Global.MaxErrors must be >= 0");
        }

        if (config.Global.Performance.BatchSize <= 0)
        {
            errors.Add("Global.Performance.BatchSize must be > 0");
        }

        if (config.Global.Performance.MaxDegreeOfParallelism < 0)
        {
            errors.Add("Global.Performance.MaxDegreeOfParallelism must be >= 0");
        }

        // Validate transformers
        foreach (var transformer in config.GlobalTransformers)
        {
            ValidateTransformer(transformer, "GlobalTransformers", errors);
        }

        // Validate table configurations
        foreach (var (tableName, tableConfig) in config.Tables)
        {
            foreach (var (columnName, transformers) in tableConfig.Columns)
            {
                for (int i = 0; i < transformers.Count; i++)
                {
                    ValidateTransformer(transformers[i], $"Tables[{tableName}].Columns[{columnName}][{i}]", errors);
                }
            }

            foreach (var (i, rowTransformer) in tableConfig.RowTransformers.Select((rt, i) => (i, rt)))
            {
                ValidateRowTransformer(rowTransformer, $"Tables[{tableName}].RowTransformers[{i}]", errors);
            }
        }

        if (errors.Count > 0)
        {
            throw new ConfigurationException($"Configuration validation failed:\n- {string.Join("\n- ", errors)}");
        }
    }

    private static void ValidateTransformer(TransformerConfig transformer, string path, List<string> errors)
    {
        if (string.IsNullOrEmpty(transformer.Name))
        {
            errors.Add($"{path}: Transformer name is required");
        }

        if (transformer.Priority < 0)
        {
            errors.Add($"{path}: Priority must be >= 0");
        }
    }

    private static void ValidateRowTransformer(RowTransformerConfig transformer, string path, List<string> errors)
    {
        if (string.IsNullOrEmpty(transformer.Name))
        {
            errors.Add($"{path}: Row transformer name is required");
        }

        if (transformer.Priority < 0)
        {
            errors.Add($"{path}: Priority must be >= 0");
        }
    }
}

/// <summary>
/// Exception thrown when configuration loading or validation fails
/// </summary>
public class ConfigurationException : Exception
{
    public ConfigurationException(string message) : base(message) { }
    public ConfigurationException(string message, Exception innerException) : base(message, innerException) { }
}