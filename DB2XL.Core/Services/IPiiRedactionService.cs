using DB2XL.Core.Models;

namespace DB2XL.Core.Services;

/// <summary>
/// Service for detecting and redacting PII (Personally Identifiable Information) in database exports.
/// Provides configurable privacy protection for sensitive data.
/// </summary>
public interface IPiiRedactionService
{
    /// <summary>
    /// Analyze database schema to detect potential PII columns.
    /// </summary>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="options">Analysis options</param>
    /// <returns>PII analysis results</returns>
    Task<PiiAnalysisResult> AnalyzePiiAsync(string connectionString, PiiAnalysisOptions options);
    
    /// <summary>
    /// Apply PII redaction to exported data.
    /// </summary>
    /// <param name="data">Data to redact</param>
    /// <param name="config">Redaction configuration</param>
    /// <returns>Redacted data with metadata</returns>
    Task<PiiRedactionResult> RedactDataAsync(ExportDataSet data, PiiRedactionConfig config);
    
    /// <summary>
    /// Generate PII redaction configuration from analysis results.
    /// </summary>
    /// <param name="analysisResult">PII analysis results</param>
    /// <param name="policy">Redaction policy to apply</param>
    /// <returns>Generated redaction configuration</returns>
    PiiRedactionConfig GenerateRedactionConfig(PiiAnalysisResult analysisResult, PiiRedactionPolicy policy);
    
    /// <summary>
    /// Validate PII redaction configuration.
    /// </summary>
    /// <param name="config">Configuration to validate</param>
    /// <returns>Validation results</returns>
    PiiConfigValidationResult ValidateRedactionConfig(PiiRedactionConfig config);
}

/// <summary>
/// Options for PII analysis operations.
/// </summary>
public sealed record PiiAnalysisOptions
{
    /// <summary>Tables to analyze (null = all tables).</summary>
    public IReadOnlyList<string>? IncludeTables { get; init; }
    
    /// <summary>Tables to exclude from analysis.</summary>
    public IReadOnlyList<string> ExcludeTables { get; init; } = Array.Empty<string>();
    
    /// <summary>Sample size for data pattern analysis.</summary>
    public int SampleSize { get; init; } = 1000;
    
    /// <summary>Enable deep content analysis of text columns.</summary>
    public bool EnableContentAnalysis { get; init; } = true;
    
    /// <summary>Confidence threshold for PII detection (0.0-1.0).</summary>
    public double ConfidenceThreshold { get; init; } = 0.7;
    
    /// <summary>Custom PII patterns to detect.</summary>
    public IReadOnlyList<PiiPattern> CustomPatterns { get; init; } = Array.Empty<PiiPattern>();
}

/// <summary>
/// Results of PII analysis operation.
/// </summary>
public sealed record PiiAnalysisResult
{
    /// <summary>Whether analysis completed successfully.</summary>
    public bool IsSuccess { get; init; }
    
    /// <summary>Detected PII columns by table.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PiiColumnDetection>> DetectedPiiColumns { get; init; } = 
        new Dictionary<string, IReadOnlyList<PiiColumnDetection>>();
    
    /// <summary>Analysis statistics.</summary>
    public PiiAnalysisStats Statistics { get; init; } = new();
    
    /// <summary>Recommended redaction actions.</summary>
    public IReadOnlyList<PiiRedactionRecommendation> Recommendations { get; init; } = Array.Empty<PiiRedactionRecommendation>();
    
    /// <summary>Any errors encountered during analysis.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    
    /// <summary>Analysis duration.</summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// PII detection information for a specific column.
/// </summary>
public sealed record PiiColumnDetection
{
    /// <summary>Table name.</summary>
    public required string TableName { get; init; }
    
    /// <summary>Column name.</summary>
    public required string ColumnName { get; init; }
    
    /// <summary>Type of PII detected.</summary>
    public required PiiDataType PiiType { get; init; }
    
    /// <summary>Confidence score (0.0-1.0).</summary>
    public double Confidence { get; init; }
    
    /// <summary>Detection method used.</summary>
    public PiiDetectionMethod DetectionMethod { get; init; }
    
    /// <summary>Sample values that triggered detection (masked).</summary>
    public IReadOnlyList<string> SampleValues { get; init; } = Array.Empty<string>();
    
    /// <summary>Estimated percentage of rows containing PII.</summary>
    public double PiiPercentage { get; init; }
    
    /// <summary>Recommended redaction strategy.</summary>
    public PiiRedactionStrategy RecommendedStrategy { get; init; }
    
    /// <summary>Additional detection metadata.</summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Types of PII that can be detected.
/// </summary>
public enum PiiDataType
{
    Email,
    PhoneNumber,
    SocialSecurityNumber,
    CreditCardNumber,
    BankAccountNumber,
    DriversLicense,
    PassportNumber,
    PersonName,
    Address,
    PostalCode,
    DateOfBirth,
    IpAddress,
    MacAddress,
    CustomIdentifier,
    Unknown
}

/// <summary>
/// Methods used for PII detection.
/// </summary>
public enum PiiDetectionMethod
{
    ColumnNamePattern,
    DataFormatPattern,
    ContentAnalysis,
    CustomRule,
    ManualClassification
}

/// <summary>
/// PII redaction strategies.
/// </summary>
public enum PiiRedactionStrategy
{
    /// <summary>Replace with fixed mask (e.g., ***REDACTED***).</summary>
    Mask,
    
    /// <summary>Replace with hash of original value.</summary>
    Hash,
    
    /// <summary>Replace with format-preserving substitute.</summary>
    Substitute,
    
    /// <summary>Partial masking (e.g., email: j***@example.com).</summary>
    PartialMask,
    
    /// <summary>Remove the column entirely.</summary>
    Remove,
    
    /// <summary>Encrypt with reversible encryption.</summary>
    Encrypt,
    
    /// <summary>No redaction (keep original).</summary>
    None
}

/// <summary>
/// Custom PII detection pattern.
/// </summary>
public sealed record PiiPattern
{
    /// <summary>Pattern name/identifier.</summary>
    public required string Name { get; init; }
    
    /// <summary>Regular expression pattern.</summary>
    public required string Pattern { get; init; }
    
    /// <summary>PII type this pattern detects.</summary>
    public required PiiDataType PiiType { get; init; }
    
    /// <summary>Confidence score for matches (0.0-1.0).</summary>
    public double Confidence { get; init; } = 0.8;
    
    /// <summary>Whether to apply to column names.</summary>
    public bool ApplyToColumnNames { get; init; } = true;
    
    /// <summary>Whether to apply to data content.</summary>
    public bool ApplyToContent { get; init; } = true;
}

/// <summary>
/// Statistics from PII analysis.
/// </summary>
public sealed record PiiAnalysisStats
{
    /// <summary>Number of tables analyzed.</summary>
    public int TablesAnalyzed { get; init; }
    
    /// <summary>Number of columns analyzed.</summary>
    public int ColumnsAnalyzed { get; init; }
    
    /// <summary>Number of PII columns detected.</summary>
    public int PiiColumnsDetected { get; init; }
    
    /// <summary>Number of rows sampled for analysis.</summary>
    public long RowsSampled { get; init; }
    
    /// <summary>Most common PII types detected.</summary>
    public IReadOnlyDictionary<PiiDataType, int> PiiTypeDistribution { get; init; } = 
        new Dictionary<PiiDataType, int>();
    
    /// <summary>Detection method effectiveness.</summary>
    public IReadOnlyDictionary<PiiDetectionMethod, int> DetectionMethodStats { get; init; } = 
        new Dictionary<PiiDetectionMethod, int>();
}

/// <summary>
/// PII redaction recommendation.
/// </summary>
public sealed record PiiRedactionRecommendation
{
    /// <summary>Table name.</summary>
    public required string TableName { get; init; }
    
    /// <summary>Column name.</summary>
    public required string ColumnName { get; init; }
    
    /// <summary>Recommended redaction strategy.</summary>
    public required PiiRedactionStrategy Strategy { get; init; }
    
    /// <summary>Reason for recommendation.</summary>
    public required string Reason { get; init; }
    
    /// <summary>Risk level if not redacted.</summary>
    public PiiRiskLevel RiskLevel { get; init; }
    
    /// <summary>Additional configuration parameters.</summary>
    public IReadOnlyDictionary<string, object> Parameters { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// PII risk levels.
/// </summary>
public enum PiiRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Data set for export operations.
/// </summary>
public sealed record ExportDataSet
{
    /// <summary>Table data by table name.</summary>
    public required IReadOnlyDictionary<string, TableData> Tables { get; init; }
    
    /// <summary>Export metadata.</summary>
    public ExportMetadata Metadata { get; init; } = new();
}

/// <summary>
/// Table data for export.
/// </summary>
public sealed record TableData
{
    /// <summary>Table name.</summary>
    public required string Name { get; init; }
    
    /// <summary>Column definitions.</summary>
    public required IReadOnlyList<ColumnInfo> Columns { get; init; }
    
    /// <summary>Data rows.</summary>
    public required IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; init; }
}

/// <summary>
/// Export metadata.
/// </summary>
public sealed record ExportMetadata
{
    /// <summary>Export timestamp.</summary>
    public DateTime ExportTime { get; init; } = DateTime.UtcNow;
    
    /// <summary>Database source information.</summary>
    public string DatabaseSource { get; init; } = string.Empty;
    
    /// <summary>Export options used.</summary>
    public IReadOnlyDictionary<string, object> Options { get; init; } = new Dictionary<string, object>();
}

// PII Configuration types are defined in PiiConfigurationLoader.cs

/// <summary>
/// Result of PII redaction operation.
/// </summary>
public sealed record PiiRedactionResult
{
    /// <summary>Whether redaction completed successfully.</summary>
    public bool IsSuccess { get; init; }
    
    /// <summary>Redacted data set.</summary>
    public ExportDataSet? RedactedData { get; init; }
    
    /// <summary>Redaction audit log.</summary>
    public PiiRedactionAuditLog AuditLog { get; init; } = new();
    
    /// <summary>Any errors encountered.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    
    /// <summary>Redaction duration.</summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Audit log for PII redaction operations.
/// </summary>
public sealed record PiiRedactionAuditLog
{
    /// <summary>Redaction timestamp.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    
    /// <summary>Redaction summary statistics.</summary>
    public PiiRedactionStats Statistics { get; init; } = new();
    
    /// <summary>Detailed redaction actions performed.</summary>
    public IReadOnlyList<PiiRedactionAction> Actions { get; init; } = Array.Empty<PiiRedactionAction>();
    
    /// <summary>Configuration used for redaction.</summary>
    public PiiRedactionConfig Configuration { get; init; } = new();
}

/// <summary>
/// Statistics from PII redaction operation.
/// </summary>
public sealed record PiiRedactionStats
{
    /// <summary>Number of tables processed.</summary>
    public int TablesProcessed { get; init; }
    
    /// <summary>Number of columns redacted.</summary>
    public int ColumnsRedacted { get; init; }
    
    /// <summary>Number of rows processed.</summary>
    public long RowsProcessed { get; init; }
    
    /// <summary>Number of values redacted.</summary>
    public long ValuesRedacted { get; init; }
    
    /// <summary>Redaction strategy usage.</summary>
    public IReadOnlyDictionary<PiiRedactionStrategy, int> StrategyUsage { get; init; } = 
        new Dictionary<PiiRedactionStrategy, int>();
}

/// <summary>
/// Individual PII redaction action performed.
/// </summary>
public sealed record PiiRedactionAction
{
    /// <summary>Table name.</summary>
    public required string TableName { get; init; }
    
    /// <summary>Column name.</summary>
    public required string ColumnName { get; init; }
    
    /// <summary>Redaction strategy applied.</summary>
    public required PiiRedactionStrategy Strategy { get; init; }
    
    /// <summary>Number of values redacted.</summary>
    public long ValuesRedacted { get; init; }
    
    /// <summary>Original data hash (for audit).</summary>
    public string? OriginalDataHash { get; init; }
    
    /// <summary>Redaction parameters used.</summary>
    public IReadOnlyDictionary<string, object> Parameters { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// PII redaction policy definitions.
/// </summary>
public sealed record PiiRedactionPolicy
{
    /// <summary>Policy name.</summary>
    public required string Name { get; init; }
    
    /// <summary>Policy description.</summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>Default strategy mappings by PII type.</summary>
    public IReadOnlyDictionary<PiiDataType, PiiRedactionStrategy> DefaultStrategies { get; init; } = 
        new Dictionary<PiiDataType, PiiRedactionStrategy>();
    
    /// <summary>Risk level mappings.</summary>
    public IReadOnlyDictionary<PiiDataType, PiiRiskLevel> RiskLevels { get; init; } = 
        new Dictionary<PiiDataType, PiiRiskLevel>();
    
    /// <summary>Compliance requirements.</summary>
    public IReadOnlyList<string> ComplianceFrameworks { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Validation result for PII redaction configuration.
/// </summary>
public sealed record PiiConfigValidationResult
{
    /// <summary>Whether configuration is valid.</summary>
    public bool IsValid { get; init; }
    
    /// <summary>Validation errors.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    
    /// <summary>Validation warnings.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    
    /// <summary>Configuration recommendations.</summary>
    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();
}