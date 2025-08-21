using DB2XL.Core.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DB2XL.Export.Bundle.Services;

/// <summary>
/// Loads PII redaction configurations from YAML/JSON files.
/// Supports standard formats for privacy policy definitions.
/// </summary>
public static class PiiConfigurationLoader
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Load PII redaction configuration from JSON file.
    /// </summary>
    /// <param name="filePath">Path to configuration file</param>
    /// <returns>Loaded configuration</returns>
    public static async Task<PiiRedactionConfig> LoadConfigurationAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"PII configuration file not found: {filePath}");
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        return extension switch
        {
            ".json" => await LoadJsonConfigurationAsync(filePath),
            ".yaml" or ".yml" => await LoadYamlConfigurationAsync(filePath),
            _ => throw new ArgumentException($"Unsupported configuration file format: {extension}")
        };
    }

    /// <summary>
    /// Save PII redaction configuration to JSON file.
    /// </summary>
    /// <param name="config">Configuration to save</param>
    /// <param name="filePath">Output file path</param>
    public static async Task SaveConfigurationAsync(PiiRedactionConfig config, string filePath)
    {
        var configDto = MapToConfigurationDto(config);
        var json = JsonSerializer.Serialize(configDto, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Create a default PII redaction policy for common compliance frameworks.
    /// </summary>
    /// <param name="framework">Compliance framework (GDPR, CCPA, HIPAA)</param>
    /// <returns>Default policy configuration</returns>
    public static PiiRedactionPolicy CreateDefaultPolicy(string framework)
    {
        return framework.ToUpperInvariant() switch
        {
            "GDPR" => CreateGdprPolicy(),
            "CCPA" => CreateCcpaPolicy(),
            "HIPAA" => CreateHipaaPolicy(),
            "STRICT" => CreateStrictPolicy(),
            _ => CreateBalancedPolicy()
        };
    }

    /// <summary>
    /// Generate sample PII configuration file for reference.
    /// </summary>
    /// <param name="outputPath">Output file path</param>
    public static async Task GenerateSampleConfigurationAsync(string outputPath)
    {
        var sampleConfig = CreateSampleConfiguration();
        await SaveConfigurationAsync(sampleConfig, outputPath);
    }

    private static async Task<PiiRedactionConfig> LoadJsonConfigurationAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var configDto = JsonSerializer.Deserialize<PiiConfigurationDto>(json, _jsonOptions);
        
        if (configDto == null)
        {
            throw new InvalidOperationException("Failed to deserialize PII configuration");
        }

        return MapFromConfigurationDto(configDto);
    }

    private static async Task<PiiRedactionConfig> LoadYamlConfigurationAsync(string filePath)
    {
        // For now, throw not implemented. In production, would use YamlDotNet
        throw new NotImplementedException("YAML configuration loading not yet implemented. Please use JSON format.");
    }

    private static PiiConfigurationDto MapToConfigurationDto(PiiRedactionConfig config)
    {
        return new PiiConfigurationDto
        {
            GlobalSettings = new PiiGlobalSettingsDto
            {
                Enabled = config.GlobalSettings.Enabled,
                DefaultStrategy = config.GlobalSettings.DefaultStrategy.ToString(),
                PreserveFormat = config.GlobalSettings.PreserveFormat,
                AuditLevel = config.GlobalSettings.AuditLevel.ToString(),
                HashSalt = config.GlobalSettings.HashSalt
            },
            TableRules = config.TableRules.ToDictionary(
                kvp => kvp.Key,
                kvp => new PiiTableRulesDto
                {
                    TableName = kvp.Value.TableName,
                    Enabled = kvp.Value.Enabled,
                    DefaultStrategy = kvp.Value.DefaultStrategy?.ToString(),
                    ExcludeColumns = kvp.Value.ExcludeColumns.ToList(),
                    ConditionalRedaction = kvp.Value.ConditionalRedaction
                }
            ),
            ColumnRules = config.ColumnRules.ToDictionary(
                kvp => kvp.Key,
                kvp => new PiiColumnRuleDto
                {
                    ColumnIdentifier = kvp.Value.ColumnIdentifier,
                    Strategy = kvp.Value.Strategy.ToString(),
                    CustomFunctionName = kvp.Value.CustomFunctionName,
                    Parameters = kvp.Value.Parameters.ToDictionary(p => p.Key, p => p.Value),
                    Enabled = kvp.Value.Enabled
                }
            )
        };
    }

    private static PiiRedactionConfig MapFromConfigurationDto(PiiConfigurationDto dto)
    {
        var globalSettings = new PiiGlobalSettings
        {
            Enabled = dto.GlobalSettings?.Enabled ?? true,
            DefaultStrategy = Enum.Parse<PiiRedactionStrategy>(dto.GlobalSettings?.DefaultStrategy ?? "Mask"),
            PreserveFormat = dto.GlobalSettings?.PreserveFormat ?? true,
            AuditLevel = Enum.Parse<PiiAuditLevel>(dto.GlobalSettings?.AuditLevel ?? "Summary"),
            HashSalt = dto.GlobalSettings?.HashSalt ?? "DB2XL_DEFAULT_SALT"
        };

        var tableRules = dto.TableRules?.ToDictionary(
            kvp => kvp.Key,
            kvp => new PiiTableRedactionRules
            {
                TableName = kvp.Value.TableName,
                Enabled = kvp.Value.Enabled,
                DefaultStrategy = string.IsNullOrEmpty(kvp.Value.DefaultStrategy) 
                    ? null 
                    : Enum.Parse<PiiRedactionStrategy>(kvp.Value.DefaultStrategy),
                ExcludeColumns = kvp.Value.ExcludeColumns?.AsReadOnly() ?? Array.Empty<string>(),
                ConditionalRedaction = kvp.Value.ConditionalRedaction
            }
        ) ?? new Dictionary<string, PiiTableRedactionRules>();

        var columnRules = dto.ColumnRules?.ToDictionary(
            kvp => kvp.Key,
            kvp => new PiiColumnRedactionRule
            {
                ColumnIdentifier = kvp.Value.ColumnIdentifier,
                Strategy = Enum.Parse<PiiRedactionStrategy>(kvp.Value.Strategy),
                CustomFunctionName = kvp.Value.CustomFunctionName,
                Parameters = kvp.Value.Parameters?.ToReadOnlyDictionary() ?? new Dictionary<string, object>(),
                Enabled = kvp.Value.Enabled
            }
        ) ?? new Dictionary<string, PiiColumnRedactionRule>();

        return new PiiRedactionConfig
        {
            GlobalSettings = globalSettings,
            TableRules = tableRules,
            ColumnRules = columnRules
        };
    }

    private static PiiRedactionConfig CreateSampleConfiguration()
    {
        return new PiiRedactionConfig
        {
            GlobalSettings = new PiiGlobalSettings
            {
                Enabled = true,
                DefaultStrategy = PiiRedactionStrategy.Mask,
                PreserveFormat = true,
                AuditLevel = PiiAuditLevel.Summary,
                HashSalt = "your-custom-salt-here"
            },
            TableRules = new Dictionary<string, PiiTableRedactionRules>
            {
                ["users"] = new PiiTableRedactionRules
                {
                    TableName = "users",
                    Enabled = true,
                    DefaultStrategy = PiiRedactionStrategy.PartialMask,
                    ExcludeColumns = new[] { "id", "created_at" }
                },
                ["orders"] = new PiiTableRedactionRules
                {
                    TableName = "orders",
                    Enabled = true,
                    ConditionalRedaction = "status != 'public'"
                }
            },
            ColumnRules = new Dictionary<string, PiiColumnRedactionRule>
            {
                ["users.email"] = new PiiColumnRedactionRule
                {
                    ColumnIdentifier = "users.email",
                    Strategy = PiiRedactionStrategy.PartialMask,
                    Enabled = true
                },
                ["users.phone"] = new PiiColumnRedactionRule
                {
                    ColumnIdentifier = "users.phone",
                    Strategy = PiiRedactionStrategy.PartialMask,
                    Enabled = true
                },
                ["users.ssn"] = new PiiColumnRedactionRule
                {
                    ColumnIdentifier = "users.ssn",
                    Strategy = PiiRedactionStrategy.Hash,
                    Enabled = true
                },
                ["orders.billing_address"] = new PiiColumnRedactionRule
                {
                    ColumnIdentifier = "orders.billing_address",
                    Strategy = PiiRedactionStrategy.Substitute,
                    Parameters = new Dictionary<string, object> { ["substitutePattern"] = "123 Main St, Anytown, ST 12345" },
                    Enabled = true
                }
            }
        };
    }

    private static PiiRedactionPolicy CreateGdprPolicy()
    {
        return new PiiRedactionPolicy
        {
            Name = "GDPR Compliance",
            Description = "European GDPR privacy regulations compliance",
            DefaultStrategies = new Dictionary<PiiDataType, PiiRedactionStrategy>
            {
                [PiiDataType.Email] = PiiRedactionStrategy.Hash,
                [PiiDataType.PhoneNumber] = PiiRedactionStrategy.Hash,
                [PiiDataType.PersonName] = PiiRedactionStrategy.Hash,
                [PiiDataType.Address] = PiiRedactionStrategy.Mask,
                [PiiDataType.IpAddress] = PiiRedactionStrategy.PartialMask,
                [PiiDataType.DateOfBirth] = PiiRedactionStrategy.Mask
            },
            RiskLevels = new Dictionary<PiiDataType, PiiRiskLevel>
            {
                [PiiDataType.Email] = PiiRiskLevel.High,
                [PiiDataType.PhoneNumber] = PiiRiskLevel.High,
                [PiiDataType.PersonName] = PiiRiskLevel.High,
                [PiiDataType.Address] = PiiRiskLevel.High,
                [PiiDataType.IpAddress] = PiiRiskLevel.Medium,
                [PiiDataType.DateOfBirth] = PiiRiskLevel.High
            },
            ComplianceFrameworks = new[] { "GDPR" }
        };
    }

    private static PiiRedactionPolicy CreateCcpaPolicy()
    {
        return new PiiRedactionPolicy
        {
            Name = "CCPA Compliance",
            Description = "California Consumer Privacy Act compliance",
            DefaultStrategies = new Dictionary<PiiDataType, PiiRedactionStrategy>
            {
                [PiiDataType.Email] = PiiRedactionStrategy.PartialMask,
                [PiiDataType.PhoneNumber] = PiiRedactionStrategy.PartialMask,
                [PiiDataType.PersonName] = PiiRedactionStrategy.Substitute,
                [PiiDataType.Address] = PiiRedactionStrategy.Mask,
                [PiiDataType.SocialSecurityNumber] = PiiRedactionStrategy.Hash,
                [PiiDataType.CreditCardNumber] = PiiRedactionStrategy.Mask
            },
            ComplianceFrameworks = new[] { "CCPA" }
        };
    }

    private static PiiRedactionPolicy CreateHipaaPolicy()
    {
        return new PiiRedactionPolicy
        {
            Name = "HIPAA Compliance",
            Description = "Healthcare privacy regulations compliance",
            DefaultStrategies = new Dictionary<PiiDataType, PiiRedactionStrategy>
            {
                [PiiDataType.PersonName] = PiiRedactionStrategy.Hash,
                [PiiDataType.DateOfBirth] = PiiRedactionStrategy.Mask,
                [PiiDataType.SocialSecurityNumber] = PiiRedactionStrategy.Hash,
                [PiiDataType.Email] = PiiRedactionStrategy.Hash,
                [PiiDataType.PhoneNumber] = PiiRedactionStrategy.Hash,
                [PiiDataType.Address] = PiiRedactionStrategy.Mask
            },
            ComplianceFrameworks = new[] { "HIPAA" }
        };
    }

    private static PiiRedactionPolicy CreateStrictPolicy()
    {
        return new PiiRedactionPolicy
        {
            Name = "Strict Privacy",
            Description = "Maximum privacy protection",
            DefaultStrategies = Enum.GetValues<PiiDataType>()
                .ToDictionary(type => type, _ => PiiRedactionStrategy.Hash),
            ComplianceFrameworks = new[] { "STRICT" }
        };
    }

    private static PiiRedactionPolicy CreateBalancedPolicy()
    {
        return new PiiRedactionPolicy
        {
            Name = "Balanced Privacy",
            Description = "Balanced approach to privacy protection",
            DefaultStrategies = new Dictionary<PiiDataType, PiiRedactionStrategy>
            {
                [PiiDataType.SocialSecurityNumber] = PiiRedactionStrategy.Hash,
                [PiiDataType.CreditCardNumber] = PiiRedactionStrategy.Mask,
                [PiiDataType.Email] = PiiRedactionStrategy.PartialMask,
                [PiiDataType.PhoneNumber] = PiiRedactionStrategy.PartialMask,
                [PiiDataType.PersonName] = PiiRedactionStrategy.Substitute,
                [PiiDataType.Address] = PiiRedactionStrategy.Mask
            },
            ComplianceFrameworks = new[] { "BALANCED" }
        };
    }
}

// DTOs for JSON serialization
internal sealed record PiiConfigurationDto
{
    public PiiGlobalSettingsDto? GlobalSettings { get; init; }
    public Dictionary<string, PiiTableRulesDto>? TableRules { get; init; }
    public Dictionary<string, PiiColumnRuleDto>? ColumnRules { get; init; }
}

internal sealed record PiiGlobalSettingsDto
{
    public bool Enabled { get; init; } = true;
    public string DefaultStrategy { get; init; } = "Mask";
    public bool PreserveFormat { get; init; } = true;
    public string AuditLevel { get; init; } = "Summary";
    public string? EncryptionKey { get; init; }
    public string HashSalt { get; init; } = "DB2XL_DEFAULT_SALT";
}

internal sealed record PiiTableRulesDto
{
    public string TableName { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public string? DefaultStrategy { get; init; }
    public List<string>? ExcludeColumns { get; init; }
    public string? ConditionalRedaction { get; init; }
}

internal sealed record PiiColumnRuleDto
{
    public string ColumnIdentifier { get; init; } = string.Empty;
    public string Strategy { get; init; } = "Mask";
    public string? CustomFunctionName { get; init; }
    public Dictionary<string, object>? Parameters { get; init; }
    public bool Enabled { get; init; } = true;
}

// Extension method for dictionary conversion
internal static class DictionaryExtensions
{
    public static IReadOnlyDictionary<TKey, TValue> ToReadOnlyDictionary<TKey, TValue>(
        this Dictionary<TKey, TValue>? dictionary) where TKey : notnull
    {
        return dictionary ?? new Dictionary<TKey, TValue>();
    }
}