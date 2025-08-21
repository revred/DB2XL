namespace DB2XL.Core.Services;

/// <summary>
/// Configuration for PII redaction operations.
/// </summary>
public sealed record PiiRedactionConfig
{
    /// <summary>Global redaction settings.</summary>
    public PiiGlobalSettings GlobalSettings { get; init; } = new();
    
    /// <summary>Table-specific redaction rules.</summary>
    public IReadOnlyDictionary<string, PiiTableRedactionRules> TableRules { get; init; } = 
        new Dictionary<string, PiiTableRedactionRules>();
    
    /// <summary>Column-specific redaction rules.</summary>
    public IReadOnlyDictionary<string, PiiColumnRedactionRule> ColumnRules { get; init; } = 
        new Dictionary<string, PiiColumnRedactionRule>();
    
    /// <summary>Custom redaction functions.</summary>
    public IReadOnlyDictionary<string, PiiCustomRedactionFunction> CustomFunctions { get; init; } = 
        new Dictionary<string, PiiCustomRedactionFunction>();
}

/// <summary>
/// Global PII redaction settings.
/// </summary>
public sealed record PiiGlobalSettings
{
    /// <summary>Whether redaction is enabled.</summary>
    public bool Enabled { get; init; } = true;
    
    /// <summary>Default redaction strategy.</summary>
    public PiiRedactionStrategy DefaultStrategy { get; init; } = PiiRedactionStrategy.Mask;
    
    /// <summary>Whether to preserve data format.</summary>
    public bool PreserveFormat { get; init; } = true;
    
    /// <summary>Audit logging level.</summary>
    public PiiAuditLevel AuditLevel { get; init; } = PiiAuditLevel.Summary;
    
    /// <summary>Encryption key for reversible operations.</summary>
    public string? EncryptionKey { get; init; }
    
    /// <summary>Salt for hashing operations.</summary>
    public string HashSalt { get; init; } = "DB2XL_DEFAULT_SALT";
}

/// <summary>
/// PII audit logging levels.
/// </summary>
public enum PiiAuditLevel
{
    None,
    Summary,
    Detailed,
    Full
}

/// <summary>
/// Table-specific PII redaction rules.
/// </summary>
public sealed record PiiTableRedactionRules
{
    /// <summary>Table name.</summary>
    public required string TableName { get; init; }
    
    /// <summary>Whether to apply redaction to this table.</summary>
    public bool Enabled { get; init; } = true;
    
    /// <summary>Default strategy for columns in this table.</summary>
    public PiiRedactionStrategy? DefaultStrategy { get; init; }
    
    /// <summary>Columns to exclude from redaction.</summary>
    public IReadOnlyList<string> ExcludeColumns { get; init; } = Array.Empty<string>();
    
    /// <summary>Custom WHERE clause for conditional redaction.</summary>
    public string? ConditionalRedaction { get; init; }
}

/// <summary>
/// Column-specific PII redaction rule.
/// </summary>
public sealed record PiiColumnRedactionRule
{
    /// <summary>Table.Column identifier.</summary>
    public required string ColumnIdentifier { get; init; }
    
    /// <summary>Redaction strategy to apply.</summary>
    public required PiiRedactionStrategy Strategy { get; init; }
    
    /// <summary>Custom redaction function name.</summary>
    public string? CustomFunctionName { get; init; }
    
    /// <summary>Strategy-specific parameters.</summary>
    public IReadOnlyDictionary<string, object> Parameters { get; init; } = new Dictionary<string, object>();
    
    /// <summary>Whether this rule is enabled.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// Custom PII redaction function definition.
/// </summary>
public sealed record PiiCustomRedactionFunction
{
    /// <summary>Function name.</summary>
    public required string Name { get; init; }
    
    /// <summary>Function description.</summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>Function implementation delegate.</summary>
    public required Func<object?, IReadOnlyDictionary<string, object>, object?> Implementation { get; init; }
    
    /// <summary>Function parameters schema.</summary>
    public IReadOnlyDictionary<string, object> ParametersSchema { get; init; } = new Dictionary<string, object>();
}