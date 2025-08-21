using DB2XL.Core.Models;

namespace DB2XL.Core.Services;

/// <summary>
/// Service for generating intelligent data samples from database tables.
/// Provides various sampling strategies for AI analysis, testing, and development.
/// </summary>
public interface ISampleGenerationService
{
    /// <summary>
    /// Generate samples from database tables using specified strategy.
    /// </summary>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="options">Sample generation options</param>
    /// <returns>Generated sample data with metadata</returns>
    Task<SampleGenerationResult> GenerateSamplesAsync(string connectionString, SampleGenerationOptions options);
    
    /// <summary>
    /// Analyze table characteristics to recommend optimal sampling strategy.
    /// </summary>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="tableName">Table to analyze</param>
    /// <returns>Sampling strategy recommendations</returns>
    Task<SamplingRecommendation> AnalyzeSamplingStrategyAsync(string connectionString, string tableName);
    
    /// <summary>
    /// Generate synthetic data based on existing table patterns.
    /// </summary>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="options">Synthetic data generation options</param>
    /// <returns>Generated synthetic data</returns>
    Task<SyntheticDataResult> GenerateSyntheticDataAsync(string connectionString, SyntheticDataOptions options);
    
    /// <summary>
    /// Create balanced samples for machine learning training.
    /// </summary>
    /// <param name="connectionString">Database connection string</param>
    /// <param name="options">ML sampling options</param>
    /// <returns>Balanced sample sets for training/validation/test</returns>
    Task<MlSampleResult> GenerateMlSamplesAsync(string connectionString, MlSamplingOptions options);
}

/// <summary>
/// Options for sample generation operations.
/// </summary>
public sealed record SampleGenerationOptions
{
    /// <summary>Tables to sample (null = all tables).</summary>
    public IReadOnlyList<string>? IncludeTables { get; init; }
    
    /// <summary>Tables to exclude from sampling.</summary>
    public IReadOnlyList<string> ExcludeTables { get; init; } = Array.Empty<string>();
    
    /// <summary>Default sampling strategy.</summary>
    public SamplingStrategy DefaultStrategy { get; init; } = SamplingStrategy.Intelligent;
    
    /// <summary>Maximum rows per table sample.</summary>
    public int MaxSampleSize { get; init; } = 10_000;
    
    /// <summary>Minimum rows per table sample.</summary>
    public int MinSampleSize { get; init; } = 10;
    
    /// <summary>Sample percentage for large tables.</summary>
    public double SamplePercentage { get; init; } = 0.01; // 1%
    
    /// <summary>Table-specific sampling configurations.</summary>
    public IReadOnlyDictionary<string, TableSamplingConfig> TableConfigs { get; init; } = 
        new Dictionary<string, TableSamplingConfig>();
    
    /// <summary>Output directory for sample files.</summary>
    public required string OutputDirectory { get; init; }
    
    /// <summary>Output format for samples.</summary>
    public SampleOutputFormat OutputFormat { get; init; } = SampleOutputFormat.Jsonl;
    
    /// <summary>Whether to preserve data relationships.</summary>
    public bool PreserveRelationships { get; init; } = true;
    
    /// <summary>Whether to apply PII redaction to samples.</summary>
    public bool ApplyPiiRedaction { get; init; } = true;
    
    /// <summary>PII redaction configuration.</summary>
    public PiiRedactionConfig? PiiConfig { get; init; }
    
    /// <summary>Random seed for reproducible sampling.</summary>
    public int? RandomSeed { get; init; }
    
    /// <summary>Whether to generate sample metadata.</summary>
    public bool GenerateMetadata { get; init; } = true;
}

/// <summary>
/// Sampling strategies available.
/// </summary>
public enum SamplingStrategy
{
    /// <summary>Intelligent sampling based on data characteristics.</summary>
    Intelligent,
    
    /// <summary>Random sampling across entire table.</summary>
    Random,
    
    /// <summary>Systematic sampling (every Nth row).</summary>
    Systematic,
    
    /// <summary>Stratified sampling based on key columns.</summary>
    Stratified,
    
    /// <summary>Top N rows (most recent or by key).</summary>
    Top,
    
    /// <summary>Bottom N rows.</summary>
    Bottom,
    
    /// <summary>Time-based sampling (recent data).</summary>
    TimeBased,
    
    /// <summary>Cluster-based sampling for representative data.</summary>
    Cluster,
    
    /// <summary>Edge case sampling (outliers, nulls, extremes).</summary>
    EdgeCase,
    
    /// <summary>Balanced sampling for ML training.</summary>
    Balanced
}

/// <summary>
/// Table-specific sampling configuration.
/// </summary>
public sealed record TableSamplingConfig
{
    /// <summary>Table name.</summary>
    public required string TableName { get; init; }
    
    /// <summary>Sampling strategy for this table.</summary>
    public SamplingStrategy Strategy { get; init; } = SamplingStrategy.Intelligent;
    
    /// <summary>Sample size for this table.</summary>
    public int? SampleSize { get; init; }
    
    /// <summary>Sample percentage for this table.</summary>
    public double? SamplePercentage { get; init; }
    
    /// <summary>Stratification column for stratified sampling.</summary>
    public string? StratificationColumn { get; init; }
    
    /// <summary>Time column for time-based sampling.</summary>
    public string? TimeColumn { get; init; }
    
    /// <summary>WHERE clause for filtering before sampling.</summary>
    public string? FilterClause { get; init; }
    
    /// <summary>ORDER BY clause for ordered sampling.</summary>
    public string? OrderByClause { get; init; }
    
    /// <summary>Custom SQL query for sampling.</summary>
    public string? CustomQuery { get; init; }
}

/// <summary>
/// Output formats for sample data.
/// </summary>
public enum SampleOutputFormat
{
    Jsonl,
    Csv,
    Parquet,
    Excel,
    Sql
}

/// <summary>
/// Result of sample generation operation.
/// </summary>
public sealed record SampleGenerationResult
{
    /// <summary>Whether sample generation completed successfully.</summary>
    public bool IsSuccess { get; init; }
    
    /// <summary>Generated samples by table.</summary>
    public IReadOnlyDictionary<string, TableSampleResult> TableSamples { get; init; } = 
        new Dictionary<string, TableSampleResult>();
    
    /// <summary>Sample generation statistics.</summary>
    public SampleGenerationStats Statistics { get; init; } = new();
    
    /// <summary>Generated sample files.</summary>
    public IReadOnlyList<SampleFileInfo> SampleFiles { get; init; } = Array.Empty<SampleFileInfo>();
    
    /// <summary>Sample manifest file path.</summary>
    public string? ManifestPath { get; init; }
    
    /// <summary>Any errors encountered.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    
    /// <summary>Any warnings generated.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    
    /// <summary>Generation duration.</summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Sample result for a specific table.
/// </summary>
public sealed record TableSampleResult
{
    /// <summary>Table name.</summary>
    public required string TableName { get; init; }
    
    /// <summary>Sampling strategy used.</summary>
    public SamplingStrategy StrategyUsed { get; init; }
    
    /// <summary>Number of rows in original table.</summary>
    public long OriginalRowCount { get; init; }
    
    /// <summary>Number of rows in sample.</summary>
    public long SampleRowCount { get; init; }
    
    /// <summary>Sample percentage achieved.</summary>
    public double ActualSamplePercentage { get; init; }
    
    /// <summary>Sample representativeness score (0.0-1.0).</summary>
    public double RepresentativenessScore { get; init; }
    
    /// <summary>Sample data quality metrics.</summary>
    public SampleQualityMetrics QualityMetrics { get; init; } = new();
    
    /// <summary>Sample generation metadata.</summary>
    public SampleMetadata Metadata { get; init; } = new();
    
    /// <summary>Generated sample file paths.</summary>
    public IReadOnlyList<string> FilePaths { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Quality metrics for generated samples.
/// </summary>
public sealed record SampleQualityMetrics
{
    /// <summary>Data completeness (percentage of non-null values).</summary>
    public double Completeness { get; init; }
    
    /// <summary>Data diversity (unique value ratio).</summary>
    public double Diversity { get; init; }
    
    /// <summary>Distribution similarity to original (KS test p-value).</summary>
    public double DistributionSimilarity { get; init; }
    
    /// <summary>Coverage of value ranges.</summary>
    public double RangeCoverage { get; init; }
    
    /// <summary>Outlier representation ratio.</summary>
    public double OutlierRepresentation { get; init; }
    
    /// <summary>Pattern preservation score.</summary>
    public double PatternPreservation { get; init; }
}

/// <summary>
/// Metadata for generated samples.
/// </summary>
public sealed record SampleMetadata
{
    /// <summary>Sample generation timestamp.</summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>Random seed used.</summary>
    public int? RandomSeed { get; init; }
    
    /// <summary>SQL query used for sampling.</summary>
    public string? SamplingQuery { get; init; }
    
    /// <summary>Sampling parameters used.</summary>
    public IReadOnlyDictionary<string, object> Parameters { get; init; } = new Dictionary<string, object>();
    
    /// <summary>Data characteristics detected.</summary>
    public DataCharacteristics DataCharacteristics { get; init; } = new();
    
    /// <summary>Relationships preserved.</summary>
    public IReadOnlyList<string> PreservedRelationships { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Characteristics of the sampled data.
/// </summary>
public sealed record DataCharacteristics
{
    /// <summary>Detected data types by column.</summary>
    public IReadOnlyDictionary<string, string> ColumnTypes { get; init; } = new Dictionary<string, string>();
    
    /// <summary>Null value percentages by column.</summary>
    public IReadOnlyDictionary<string, double> NullPercentages { get; init; } = new Dictionary<string, double>();
    
    /// <summary>Unique value counts by column.</summary>
    public IReadOnlyDictionary<string, long> UniqueValueCounts { get; init; } = new Dictionary<string, long>();
    
    /// <summary>Value range information for numeric columns.</summary>
    public IReadOnlyDictionary<string, ValueRange> ValueRanges { get; init; } = new Dictionary<string, ValueRange>();
    
    /// <summary>Pattern information for text columns.</summary>
    public IReadOnlyDictionary<string, TextPatterns> TextPatterns { get; init; } = new Dictionary<string, TextPatterns>();
}

/// <summary>
/// Value range information for numeric data.
/// </summary>
public sealed record ValueRange
{
    /// <summary>Minimum value.</summary>
    public double Min { get; init; }
    
    /// <summary>Maximum value.</summary>
    public double Max { get; init; }
    
    /// <summary>Mean value.</summary>
    public double Mean { get; init; }
    
    /// <summary>Standard deviation.</summary>
    public double StdDev { get; init; }
    
    /// <summary>Median value.</summary>
    public double Median { get; init; }
}

/// <summary>
/// Pattern information for text data.
/// </summary>
public sealed record TextPatterns
{
    /// <summary>Average length.</summary>
    public double AverageLength { get; init; }
    
    /// <summary>Common patterns detected.</summary>
    public IReadOnlyList<string> CommonPatterns { get; init; } = Array.Empty<string>();
    
    /// <summary>Character set used.</summary>
    public string CharacterSet { get; init; } = string.Empty;
    
    /// <summary>Format regularity score.</summary>
    public double RegularityScore { get; init; }
}

/// <summary>
/// Information about generated sample files.
/// </summary>
public sealed record SampleFileInfo
{
    /// <summary>File path.</summary>
    public required string FilePath { get; init; }
    
    /// <summary>Table name this file represents.</summary>
    public required string TableName { get; init; }
    
    /// <summary>File format.</summary>
    public SampleOutputFormat Format { get; init; }
    
    /// <summary>Number of rows in file.</summary>
    public long RowCount { get; init; }
    
    /// <summary>File size in bytes.</summary>
    public long FileSizeBytes { get; init; }
    
    /// <summary>File checksum.</summary>
    public string Checksum { get; init; } = string.Empty;
    
    /// <summary>Compression ratio if applicable.</summary>
    public double? CompressionRatio { get; init; }
}

/// <summary>
/// Statistics from sample generation.
/// </summary>
public sealed record SampleGenerationStats
{
    /// <summary>Number of tables processed.</summary>
    public int TablesProcessed { get; init; }
    
    /// <summary>Total rows in original data.</summary>
    public long TotalOriginalRows { get; init; }
    
    /// <summary>Total rows in samples.</summary>
    public long TotalSampleRows { get; init; }
    
    /// <summary>Overall sampling percentage.</summary>
    public double OverallSamplePercentage { get; init; }
    
    /// <summary>Total file size of samples.</summary>
    public long TotalSampleSizeBytes { get; init; }
    
    /// <summary>Average sample quality score.</summary>
    public double AverageQualityScore { get; init; }
    
    /// <summary>Strategy usage distribution.</summary>
    public IReadOnlyDictionary<SamplingStrategy, int> StrategyUsage { get; init; } = 
        new Dictionary<SamplingStrategy, int>();
}

/// <summary>
/// Recommendation for optimal sampling strategy.
/// </summary>
public sealed record SamplingRecommendation
{
    /// <summary>Table name analyzed.</summary>
    public required string TableName { get; init; }
    
    /// <summary>Recommended primary strategy.</summary>
    public SamplingStrategy RecommendedStrategy { get; init; }
    
    /// <summary>Alternative strategies.</summary>
    public IReadOnlyList<SamplingStrategy> AlternativeStrategies { get; init; } = Array.Empty<SamplingStrategy>();
    
    /// <summary>Recommended sample size.</summary>
    public int RecommendedSampleSize { get; init; }
    
    /// <summary>Confidence in recommendation (0.0-1.0).</summary>
    public double Confidence { get; init; }
    
    /// <summary>Reason for recommendation.</summary>
    public required string Reason { get; init; }
    
    /// <summary>Table characteristics that influenced recommendation.</summary>
    public TableCharacteristics TableCharacteristics { get; init; } = new();
    
    /// <summary>Estimated sample quality with recommended strategy.</summary>
    public double EstimatedQuality { get; init; }
}

/// <summary>
/// Characteristics of a table for sampling analysis.
/// </summary>
public sealed record TableCharacteristics
{
    /// <summary>Total row count.</summary>
    public long RowCount { get; init; }
    
    /// <summary>Column count.</summary>
    public int ColumnCount { get; init; }
    
    /// <summary>Has time-based columns.</summary>
    public bool HasTimeColumns { get; init; }
    
    /// <summary>Has categorical columns suitable for stratification.</summary>
    public bool HasCategoricalColumns { get; init; }
    
    /// <summary>Data distribution characteristics.</summary>
    public DataDistribution Distribution { get; init; } = new();
    
    /// <summary>Detected patterns.</summary>
    public IReadOnlyList<string> DetectedPatterns { get; init; } = Array.Empty<string>();
    
    /// <summary>Estimated data skew.</summary>
    public double DataSkew { get; init; }
    
    /// <summary>Relationship complexity.</summary>
    public RelationshipComplexity RelationshipComplexity { get; init; }
}

/// <summary>
/// Data distribution characteristics.
/// </summary>
public sealed record DataDistribution
{
    /// <summary>Distribution type (normal, skewed, uniform, etc.).</summary>
    public string DistributionType { get; init; } = "unknown";
    
    /// <summary>Entropy measure.</summary>
    public double Entropy { get; init; }
    
    /// <summary>Concentration measure.</summary>
    public double Concentration { get; init; }
    
    /// <summary>Outlier percentage.</summary>
    public double OutlierPercentage { get; init; }
}

/// <summary>
/// Relationship complexity measures.
/// </summary>
public enum RelationshipComplexity
{
    Simple,
    Moderate,
    Complex,
    HighlyComplex
}

/// <summary>
/// Options for synthetic data generation.
/// </summary>
public sealed record SyntheticDataOptions
{
    /// <summary>Source table for pattern analysis.</summary>
    public required string SourceTable { get; init; }
    
    /// <summary>Number of synthetic rows to generate.</summary>
    public int RowCount { get; init; } = 1000;
    
    /// <summary>Preserve statistical characteristics.</summary>
    public bool PreserveStatistics { get; init; } = true;
    
    /// <summary>Preserve data relationships.</summary>
    public bool PreserveRelationships { get; init; } = true;
    
    /// <summary>Privacy protection level.</summary>
    public PrivacyLevel PrivacyLevel { get; init; } = PrivacyLevel.High;
    
    /// <summary>Output directory for synthetic data.</summary>
    public required string OutputDirectory { get; init; }
}

/// <summary>
/// Privacy protection levels for synthetic data.
/// </summary>
public enum PrivacyLevel
{
    Low,
    Medium,
    High,
    Maximum
}

/// <summary>
/// Result of synthetic data generation.
/// </summary>
public sealed record SyntheticDataResult
{
    /// <summary>Whether generation was successful.</summary>
    public bool IsSuccess { get; init; }
    
    /// <summary>Generated synthetic data file path.</summary>
    public string? FilePath { get; init; }
    
    /// <summary>Number of synthetic rows generated.</summary>
    public long GeneratedRows { get; init; }
    
    /// <summary>Quality metrics for synthetic data.</summary>
    public SyntheticDataQuality Quality { get; init; } = new();
    
    /// <summary>Privacy protection metrics.</summary>
    public PrivacyMetrics Privacy { get; init; } = new();
    
    /// <summary>Generation errors.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Quality metrics for synthetic data.
/// </summary>
public sealed record SyntheticDataQuality
{
    /// <summary>Statistical fidelity score.</summary>
    public double StatisticalFidelity { get; init; }
    
    /// <summary>Pattern preservation score.</summary>
    public double PatternPreservation { get; init; }
    
    /// <summary>Diversity score.</summary>
    public double Diversity { get; init; }
    
    /// <summary>Utility score for downstream tasks.</summary>
    public double Utility { get; init; }
}

/// <summary>
/// Privacy protection metrics.
/// </summary>
public sealed record PrivacyMetrics
{
    /// <summary>Privacy risk score (lower is better).</summary>
    public double PrivacyRisk { get; init; }
    
    /// <summary>Anonymity level achieved.</summary>
    public int AnonymityLevel { get; init; }
    
    /// <summary>Re-identification risk.</summary>
    public double ReidentificationRisk { get; init; }
}

/// <summary>
/// Options for ML-specific sampling.
/// </summary>
public sealed record MlSamplingOptions
{
    /// <summary>Source table for ML sampling.</summary>
    public required string SourceTable { get; init; }
    
    /// <summary>Target column for classification/regression.</summary>
    public string? TargetColumn { get; init; }
    
    /// <summary>Training set percentage.</summary>
    public double TrainingPercentage { get; init; } = 0.7;
    
    /// <summary>Validation set percentage.</summary>
    public double ValidationPercentage { get; init; } = 0.15;
    
    /// <summary>Test set percentage.</summary>
    public double TestPercentage { get; init; } = 0.15;
    
    /// <summary>Whether to stratify by target column.</summary>
    public bool StratifyByTarget { get; init; } = true;
    
    /// <summary>Balance classes in training set.</summary>
    public bool BalanceClasses { get; init; } = false;
    
    /// <summary>Random seed for reproducibility.</summary>
    public int? RandomSeed { get; init; }
    
    /// <summary>Output directory for ML sample sets.</summary>
    public required string OutputDirectory { get; init; }
}

/// <summary>
/// Result of ML-specific sampling.
/// </summary>
public sealed record MlSampleResult
{
    /// <summary>Whether sampling was successful.</summary>
    public bool IsSuccess { get; init; }
    
    /// <summary>Training set file path.</summary>
    public string? TrainingSetPath { get; init; }
    
    /// <summary>Validation set file path.</summary>
    public string? ValidationSetPath { get; init; }
    
    /// <summary>Test set file path.</summary>
    public string? TestSetPath { get; init; }
    
    /// <summary>Sample set statistics.</summary>
    public MlSampleStats Statistics { get; init; } = new();
    
    /// <summary>Class distribution information.</summary>
    public IReadOnlyDictionary<string, ClassDistribution> ClassDistributions { get; init; } = 
        new Dictionary<string, ClassDistribution>();
    
    /// <summary>Any errors encountered.</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Statistics for ML sample sets.
/// </summary>
public sealed record MlSampleStats
{
    /// <summary>Total rows in original dataset.</summary>
    public long TotalRows { get; init; }
    
    /// <summary>Rows in training set.</summary>
    public long TrainingRows { get; init; }
    
    /// <summary>Rows in validation set.</summary>
    public long ValidationRows { get; init; }
    
    /// <summary>Rows in test set.</summary>
    public long TestRows { get; init; }
    
    /// <summary>Number of features.</summary>
    public int FeatureCount { get; init; }
    
    /// <summary>Target class count (for classification).</summary>
    public int? ClassCount { get; init; }
}

/// <summary>
/// Class distribution information for ML datasets.
/// </summary>
public sealed record ClassDistribution
{
    /// <summary>Sample set name (training, validation, test).</summary>
    public required string SetName { get; init; }
    
    /// <summary>Class value counts.</summary>
    public IReadOnlyDictionary<string, long> ClassCounts { get; init; } = new Dictionary<string, long>();
    
    /// <summary>Class balance score (0=imbalanced, 1=perfectly balanced).</summary>
    public double BalanceScore { get; init; }
    
    /// <summary>Entropy of class distribution.</summary>
    public double ClassEntropy { get; init; }
}