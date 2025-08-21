using DB2XL.Core.Models;

namespace DB2XL.Core.Services;

/// <summary>
/// Generates comprehensive manifest files that document bundle exports with full provenance tracking.
/// Creates machine-readable and human-readable documentation of export processes and results.
/// </summary>
public interface IManifestGenerator
{
    /// <summary>
    /// Generates a complete bundle manifest from export results and metadata.
    /// </summary>
    /// <param name="bundleId">Unique identifier for the bundle</param>
    /// <param name="sourceDatabase">Source database information</param>
    /// <param name="exportResults">Results from all export operations</param>
    /// <param name="configuration">Export configuration used</param>
    /// <param name="options">Manifest generation options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Complete bundle manifest with all metadata</returns>
    Task<BundleManifest> GenerateBundleManifestAsync(
        string bundleId,
        DatabaseMetadata sourceDatabase,
        IReadOnlyList<TableExportResult> exportResults,
        BundleExportConfiguration configuration,
        ManifestGenerationOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates manifest files in multiple formats (JSON, YAML, Markdown).
    /// </summary>
    /// <param name="manifest">Bundle manifest to export</param>
    /// <param name="outputDirectory">Directory for manifest files</param>
    /// <param name="options">Generation options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>File generation results</returns>
    Task<ManifestFileResult> WriteManifestFilesAsync(
        BundleManifest manifest,
        string outputDirectory,
        ManifestGenerationOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a manifest against actual exported files.
    /// </summary>
    /// <param name="manifest">Manifest to validate</param>
    /// <param name="bundleRootPath">Root path of the bundle</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with detailed diagnostics</returns>
    Task<ManifestValidationResult> ValidateManifestAsync(
        BundleManifest manifest,
        string bundleRootPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges multiple manifests into a consolidated view.
    /// </summary>
    /// <param name="manifests">Manifests to merge</param>
    /// <param name="mergeOptions">Merge configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Consolidated manifest</returns>
    Task<BundleManifest> MergeManifestsAsync(
        IReadOnlyList<BundleManifest> manifests,
        ManifestMergeOptions mergeOptions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a comparison report between two manifests.
    /// </summary>
    /// <param name="baselineManifest">Baseline manifest for comparison</param>
    /// <param name="currentManifest">Current manifest to compare</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detailed comparison report</returns>
    Task<ManifestComparisonReport> CompareManifestsAsync(
        BundleManifest baselineManifest,
        BundleManifest currentManifest,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration options for manifest generation.
/// </summary>
public sealed class ManifestGenerationOptions
{
    /// <summary>
    /// Include detailed schema information in manifests.
    /// </summary>
    public bool IncludeDetailedSchema { get; init; } = true;

    /// <summary>
    /// Include file checksums for integrity verification.
    /// </summary>
    public bool IncludeFileChecksums { get; init; } = true;

    /// <summary>
    /// Include performance metrics and timing information.
    /// </summary>
    public bool IncludePerformanceMetrics { get; init; } = true;

    /// <summary>
    /// Include data quality assessment results.
    /// </summary>
    public bool IncludeDataQuality { get; init; } = true;

    /// <summary>
    /// Include transformation tracking information.
    /// </summary>
    public bool IncludeTransformations { get; init; } = true;

    /// <summary>
    /// Include sample data in manifest for preview.
    /// </summary>
    public bool IncludeSampleData { get; init; } = false;

    /// <summary>
    /// Number of sample records to include per table.
    /// </summary>
    public int SampleDataRowCount { get; init; } = 5;

    /// <summary>
    /// Generate human-readable documentation alongside machine-readable manifest.
    /// </summary>
    public bool GenerateDocumentation { get; init; } = true;

    /// <summary>
    /// Output formats to generate.
    /// </summary>
    public ManifestOutputFormats OutputFormats { get; init; } = ManifestOutputFormats.Json | ManifestOutputFormats.Markdown;

    /// <summary>
    /// Include environment information (machine, user, etc.).
    /// </summary>
    public bool IncludeEnvironmentInfo { get; init; } = true;

    /// <summary>
    /// Include git information if available.
    /// </summary>
    public bool IncludeGitInfo { get; init; } = true;

    /// <summary>
    /// Custom metadata to include in manifest.
    /// </summary>
    public IReadOnlyDictionary<string, object> CustomMetadata { get; init; } = new Dictionary<string, object>();

    /// <summary>
    /// Path patterns to exclude from file listings.
    /// </summary>
    public IReadOnlyList<string> ExcludePatterns { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Maximum file size to include detailed information for (in bytes).
    /// </summary>
    public long MaxFileSize { get; init; } = 100 * 1024 * 1024; // 100MB

    /// <summary>
    /// Include relative paths instead of absolute paths.
    /// </summary>
    public bool UseRelativePaths { get; init; } = true;

    /// <summary>
    /// Base directory for relative path calculation.
    /// </summary>
    public string? BasePath { get; init; }
}

/// <summary>
/// Options for merging multiple manifests.
/// </summary>
public sealed class ManifestMergeOptions
{
    /// <summary>
    /// How to handle conflicting table information.
    /// </summary>
    public ConflictResolutionStrategy ConflictResolution { get; init; } = ConflictResolutionStrategy.LatestWins;

    /// <summary>
    /// Whether to preserve individual manifest metadata.
    /// </summary>
    public bool PreserveSourceMetadata { get; init; } = true;

    /// <summary>
    /// Prefix for merged bundle ID.
    /// </summary>
    public string? MergedBundlePrefix { get; init; } = "merged";

    /// <summary>
    /// Include cross-references between merged manifests.
    /// </summary>
    public bool IncludeCrossReferences { get; init; } = true;
}

/// <summary>
/// Result of manifest file generation.
/// </summary>
public sealed record ManifestFileResult
{
    /// <summary>
    /// Generated manifest files with their paths and metadata.
    /// </summary>
    public IReadOnlyList<GeneratedFile> Files { get; init; } = Array.Empty<GeneratedFile>();

    /// <summary>
    /// Total size of all generated files.
    /// </summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>
    /// Generation start time.
    /// </summary>
    public DateTime GenerationStartTime { get; init; }

    /// <summary>
    /// Generation completion time.
    /// </summary>
    public DateTime GenerationEndTime { get; init; }

    /// <summary>
    /// Warnings encountered during generation.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Whether generation completed successfully.
    /// </summary>
    public bool IsSuccessful { get; init; } = true;

    /// <summary>
    /// Error message if generation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Information about a generated manifest file.
/// </summary>
public sealed record GeneratedFile
{
    /// <summary>
    /// File path.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// File format.
    /// </summary>
    public ManifestFormat Format { get; init; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// File checksum.
    /// </summary>
    public string Checksum { get; init; } = string.Empty;

    /// <summary>
    /// File purpose/description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Whether the file is machine-readable.
    /// </summary>
    public bool IsMachineReadable { get; init; }

    /// <summary>
    /// Intended audience for the file.
    /// </summary>
    public FileAudience Audience { get; init; }
}

/// <summary>
/// Result of manifest validation.
/// </summary>
public sealed record ManifestValidationResult
{
    /// <summary>
    /// Whether the manifest is valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Validation errors found.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Validation warnings.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Missing files referenced in manifest.
    /// </summary>
    public IReadOnlyList<string> MissingFiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Files present but not referenced in manifest.
    /// </summary>
    public IReadOnlyList<string> OrphanedFiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Checksum validation results.
    /// </summary>
    public IReadOnlyList<ChecksumValidationResult> ChecksumResults { get; init; } = Array.Empty<ChecksumValidationResult>();

    /// <summary>
    /// Validation metrics.
    /// </summary>
    public ManifestValidationMetrics Metrics { get; init; } = new();
}

/// <summary>
/// Comparison report between two manifests.
/// </summary>
public sealed record ManifestComparisonReport
{
    /// <summary>
    /// Comparison summary.
    /// </summary>
    public ComparisonSummary Summary { get; init; } = new();

    /// <summary>
    /// Tables added in the current manifest.
    /// </summary>
    public IReadOnlyList<string> AddedTables { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Tables removed from the baseline manifest.
    /// </summary>
    public IReadOnlyList<string> RemovedTables { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Tables modified between manifests.
    /// </summary>
    public IReadOnlyList<TableModification> ModifiedTables { get; init; } = Array.Empty<TableModification>();

    /// <summary>
    /// Schema differences found.
    /// </summary>
    public IReadOnlyList<SchemaDifference> SchemaDifferences { get; init; } = Array.Empty<SchemaDifference>();

    /// <summary>
    /// Data quality changes.
    /// </summary>
    public IReadOnlyList<QualityChange> QualityChanges { get; init; } = Array.Empty<QualityChange>();

    /// <summary>
    /// Performance comparison.
    /// </summary>
    public PerformanceComparison PerformanceComparison { get; init; } = new();

    /// <summary>
    /// Detailed comparison report in markdown format.
    /// </summary>
    public string DetailedReport { get; init; } = string.Empty;
}

/// <summary>
/// Result of a checksum validation operation.
/// </summary>
public sealed record ChecksumValidationResult
{
    /// <summary>
    /// File path that was validated.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// Expected checksum from manifest.
    /// </summary>
    public string ExpectedChecksum { get; init; } = string.Empty;

    /// <summary>
    /// Actual checksum computed from file.
    /// </summary>
    public string ActualChecksum { get; init; } = string.Empty;

    /// <summary>
    /// Whether checksums match.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Validation error message if any.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Metrics from manifest validation.
/// </summary>
public sealed record ManifestValidationMetrics
{
    /// <summary>
    /// Number of files validated.
    /// </summary>
    public int FilesValidated { get; init; }

    /// <summary>
    /// Number of checksums verified.
    /// </summary>
    public int ChecksumsVerified { get; init; }

    /// <summary>
    /// Time taken for validation.
    /// </summary>
    public TimeSpan ValidationTime { get; init; }

    /// <summary>
    /// Total size of data validated.
    /// </summary>
    public long TotalSizeValidated { get; init; }
}

/// <summary>
/// Summary of manifest comparison.
/// </summary>
public sealed record ComparisonSummary
{
    /// <summary>
    /// Baseline manifest information.
    /// </summary>
    public ManifestInfo Baseline { get; init; } = new();

    /// <summary>
    /// Current manifest information.
    /// </summary>
    public ManifestInfo Current { get; init; } = new();

    /// <summary>
    /// Overall similarity score (0-100).
    /// </summary>
    public double SimilarityScore { get; init; }

    /// <summary>
    /// Number of changes detected.
    /// </summary>
    public int ChangeCount { get; init; }

    /// <summary>
    /// Comparison timestamp.
    /// </summary>
    public DateTime ComparisonTime { get; init; }
}

/// <summary>
/// Basic manifest information for comparison.
/// </summary>
public sealed record ManifestInfo
{
    /// <summary>
    /// Bundle ID.
    /// </summary>
    public string BundleId { get; init; } = string.Empty;

    /// <summary>
    /// Export timestamp.
    /// </summary>
    public DateTime ExportTimestamp { get; init; }

    /// <summary>
    /// Number of tables.
    /// </summary>
    public int TableCount { get; init; }

    /// <summary>
    /// Total record count.
    /// </summary>
    public long TotalRecords { get; init; }

    /// <summary>
    /// Total data size.
    /// </summary>
    public long TotalSize { get; init; }
}

/// <summary>
/// Information about a modified table between manifests.
/// </summary>
public sealed record TableModification
{
    /// <summary>
    /// Table name.
    /// </summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>
    /// Type of modification.
    /// </summary>
    public ModificationType ModificationType { get; init; }

    /// <summary>
    /// Detailed changes.
    /// </summary>
    public IReadOnlyList<string> Changes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Impact assessment.
    /// </summary>
    public string ImpactAssessment { get; init; } = string.Empty;
}

/// <summary>
/// Schema difference between manifests.
/// </summary>
public sealed record SchemaDifference
{
    /// <summary>
    /// Table name.
    /// </summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>
    /// Column name (if applicable).
    /// </summary>
    public string? ColumnName { get; init; }

    /// <summary>
    /// Type of difference.
    /// </summary>
    public DifferenceType DifferenceType { get; init; }

    /// <summary>
    /// Baseline value.
    /// </summary>
    public string? BaselineValue { get; init; }

    /// <summary>
    /// Current value.
    /// </summary>
    public string? CurrentValue { get; init; }

    /// <summary>
    /// Description of the difference.
    /// </summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Data quality change between manifests.
/// </summary>
public sealed record QualityChange
{
    /// <summary>
    /// Table name.
    /// </summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>
    /// Quality metric that changed.
    /// </summary>
    public string Metric { get; init; } = string.Empty;

    /// <summary>
    /// Previous value.
    /// </summary>
    public object? PreviousValue { get; init; }

    /// <summary>
    /// Current value.
    /// </summary>
    public object? CurrentValue { get; init; }

    /// <summary>
    /// Change direction.
    /// </summary>
    public ChangeDirection Direction { get; init; }

    /// <summary>
    /// Significance of the change.
    /// </summary>
    public ChangeSeverity Severity { get; init; }
}

/// <summary>
/// Performance comparison between manifests.
/// </summary>
public sealed record PerformanceComparison
{
    /// <summary>
    /// Export duration comparison.
    /// </summary>
    public TimeSpan ExportDurationDelta { get; init; }

    /// <summary>
    /// Processing rate comparison (records/second).
    /// </summary>
    public double ProcessingRateDelta { get; init; }

    /// <summary>
    /// File size comparison.
    /// </summary>
    public long FileSizeDelta { get; init; }

    /// <summary>
    /// Performance summary.
    /// </summary>
    public string Summary { get; init; } = string.Empty;
}

/// <summary>
/// Database metadata for manifest generation.
/// </summary>
public sealed record DatabaseMetadata
{
    /// <summary>
    /// Database file path.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// Database file size.
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// Last modification time.
    /// </summary>
    public DateTime LastModified { get; init; }

    /// <summary>
    /// SQLite version.
    /// </summary>
    public string SqliteVersion { get; init; } = string.Empty;

    /// <summary>
    /// Database schema version.
    /// </summary>
    public int SchemaVersion { get; init; }

    /// <summary>
    /// Database page size.
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Total page count.
    /// </summary>
    public int PageCount { get; init; }

    /// <summary>
    /// Database checksum.
    /// </summary>
    public string Checksum { get; init; } = string.Empty;
}


/// <summary>
/// Result of exporting a table in a specific format.
/// </summary>
public sealed record FormatExportResult
{
    /// <summary>
    /// Export format.
    /// </summary>
    public string Format { get; init; } = string.Empty;

    /// <summary>
    /// Generated files.
    /// </summary>
    public IReadOnlyList<ExportedFile> Files { get; init; } = Array.Empty<ExportedFile>();

    /// <summary>
    /// Total size of exported data.
    /// </summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>
    /// Export-specific metadata.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Information about an exported file.
/// </summary>
public sealed record ExportedFile
{
    /// <summary>
    /// File path.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// File checksum.
    /// </summary>
    public string Checksum { get; init; } = string.Empty;

    /// <summary>
    /// Record count in this file.
    /// </summary>
    public long RecordCount { get; init; }

    /// <summary>
    /// File creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// File-specific metadata.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Enumerations for manifest generation.
/// </summary>
[Flags]
public enum ManifestOutputFormats
{
    None = 0,
    Json = 1,
    Yaml = 2,
    Xml = 4,
    Markdown = 8,
    Html = 16,
    All = Json | Yaml | Xml | Markdown | Html
}

public enum ManifestFormat
{
    Json,
    Yaml,
    Xml,
    Markdown,
    Html,
    Text
}

public enum FileAudience
{
    Machine,
    Human,
    Both
}

public enum ConflictResolutionStrategy
{
    LatestWins,
    EarliestWins,
    Merge,
    FailOnConflict
}

public enum ModificationType
{
    Added,
    Removed,
    Modified,
    Renamed
}

public enum DifferenceType
{
    ColumnAdded,
    ColumnRemoved,
    ColumnTypeChanged,
    ColumnNullabilityChanged,
    IndexAdded,
    IndexRemoved,
    ConstraintAdded,
    ConstraintRemoved
}

public enum ChangeDirection
{
    Improved,
    Degraded,
    Neutral
}

public enum ChangeSeverity
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Placeholder records for complete type definitions.
/// These would be implemented in their respective modules.
/// </summary>

public sealed record DataQualityMetrics
{
    public int QualityScore { get; init; }
    public long NullCount { get; init; }
    public long DuplicateCount { get; init; }
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
}

public sealed record ExportPerformanceMetrics
{
    public TimeSpan ExportDuration { get; init; }
    public double RecordsPerSecond { get; init; }
    public double MegabytesPerSecond { get; init; }
}

public sealed record TableSchemaMetadata
{
    public IReadOnlyList<ColumnMetadata> Columns { get; init; } = Array.Empty<ColumnMetadata>();
    public IReadOnlyList<string> PrimaryKeyColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<IndexInfo> Indexes { get; init; } = Array.Empty<IndexInfo>();
}

public sealed record PartitioningMetadata
{
    public string Strategy { get; init; } = string.Empty;
    public int PartitionCount { get; init; }
    public IReadOnlyDictionary<string, object> Parameters { get; init; } = new Dictionary<string, object>();
}

public sealed record TransformationMetadata
{
    public int TransformationCount { get; init; }
    public IReadOnlyList<string> TransformerTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, object> Configuration { get; init; } = new Dictionary<string, object>();
}