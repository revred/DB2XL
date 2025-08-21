using DB2XL.Core.Models;

namespace DB2XL.Core.Services;

/// <summary>
/// Generates Excel index workbooks that provide navigation and summary views for bundle exports.
/// Creates comprehensive overviews, table listings, and partition maps for large datasets.
/// </summary>
public interface IExcelIndexGenerator
{
    /// <summary>
    /// Generates a comprehensive Excel index workbook for a bundle export.
    /// </summary>
    /// <param name="bundleManifest">Complete bundle manifest with all tables and partitions</param>
    /// <param name="outputFilePath">Path for the Excel index workbook</param>
    /// <param name="options">Index generation configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Index generation result with metadata</returns>
    Task<ExcelIndexResult> GenerateIndexWorkbookAsync(
        BundleManifest bundleManifest,
        string outputFilePath,
        ExcelIndexOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a focused table index for a specific set of tables.
    /// </summary>
    /// <param name="tableManifests">Table manifests to include in index</param>
    /// <param name="outputFilePath">Path for the Excel index workbook</param>
    /// <param name="options">Index generation configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Index generation result</returns>
    Task<ExcelIndexResult> GenerateTableIndexAsync(
        IReadOnlyList<TableManifest> tableManifests,
        string outputFilePath,
        ExcelIndexOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing index workbook with new table data.
    /// </summary>
    /// <param name="existingIndexPath">Path to existing Excel index</param>
    /// <param name="updatedManifest">Updated bundle manifest</param>
    /// <param name="options">Update configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Update result</returns>
    Task<ExcelIndexResult> UpdateIndexWorkbookAsync(
        string existingIndexPath,
        BundleManifest updatedManifest,
        ExcelIndexOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an index workbook against its source bundle.
    /// </summary>
    /// <param name="indexFilePath">Path to Excel index workbook</param>
    /// <param name="bundleManifest">Source bundle manifest</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with detailed diagnostics</returns>
    Task<IndexValidationResult> ValidateIndexAsync(
        string indexFilePath,
        BundleManifest bundleManifest,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration options for Excel index generation.
/// </summary>
public sealed class ExcelIndexOptions
{
    /// <summary>
    /// Include summary dashboard sheet with key metrics.
    /// </summary>
    public bool IncludeDashboard { get; init; } = true;

    /// <summary>
    /// Include detailed table listing with column information.
    /// </summary>
    public bool IncludeTableCatalog { get; init; } = true;

    /// <summary>
    /// Include partition mapping and file navigation.
    /// </summary>
    public bool IncludePartitionMap { get; init; } = true;

    /// <summary>
    /// Include data quality assessment sheet.
    /// </summary>
    public bool IncludeDataQuality { get; init; } = true;

    /// <summary>
    /// Include schema comparison views (if multiple versions).
    /// </summary>
    public bool IncludeSchemaComparison { get; init; } = false;

    /// <summary>
    /// Maximum number of rows per sheet before pagination.
    /// </summary>
    public int MaxRowsPerSheet { get; init; } = 100_000;

    /// <summary>
    /// Include hyperlinks to actual data files.
    /// </summary>
    public bool IncludeFileHyperlinks { get; init; } = true;

    /// <summary>
    /// Include data sampling preview (first few records).
    /// </summary>
    public bool IncludeDataPreview { get; init; } = true;

    /// <summary>
    /// Number of sample rows to include in preview.
    /// </summary>
    public int PreviewRowCount { get; init; } = 10;

    /// <summary>
    /// Include export performance metrics.
    /// </summary>
    public bool IncludePerformanceMetrics { get; init; } = true;

    /// <summary>
    /// Include transformation tracking (if transforms were applied).
    /// </summary>
    public bool IncludeTransformationLog { get; init; } = true;

    /// <summary>
    /// Color scheme for the Excel workbook.
    /// </summary>
    public ExcelColorScheme ColorScheme { get; init; } = ExcelColorScheme.Professional;

    /// <summary>
    /// Include freeze panes and auto-filters for navigation.
    /// </summary>
    public bool EnableAdvancedFormatting { get; init; } = true;

    /// <summary>
    /// Include conditional formatting for data quality indicators.
    /// </summary>
    public bool EnableConditionalFormatting { get; init; } = true;

    /// <summary>
    /// Generate charts and visualizations where appropriate.
    /// </summary>
    public bool IncludeCharts { get; init; } = false;

    /// <summary>
    /// Bundle root directory for relative path resolution.
    /// </summary>
    public string? BundleRootPath { get; init; }

    /// <summary>
    /// Custom title for the index workbook.
    /// </summary>
    public string? WorkbookTitle { get; init; }

    /// <summary>
    /// Author information for workbook metadata.
    /// </summary>
    public string? Author { get; init; } = "DB2XL Bundle Export System";

    /// <summary>
    /// Additional metadata to include in workbook properties.
    /// </summary>
    public IReadOnlyDictionary<string, string> CustomMetadata { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Result of Excel index generation operation.
/// </summary>
public sealed record ExcelIndexResult
{
    /// <summary>
    /// Path to the generated Excel index workbook.
    /// </summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>
    /// Number of sheets created in the workbook.
    /// </summary>
    public int SheetCount { get; init; }

    /// <summary>
    /// Total number of tables indexed.
    /// </summary>
    public int TableCount { get; init; }

    /// <summary>
    /// Total number of partitions indexed.
    /// </summary>
    public int PartitionCount { get; init; }

    /// <summary>
    /// Size of the generated Excel file in bytes.
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// Generation start timestamp.
    /// </summary>
    public DateTime GenerationStartTime { get; init; }

    /// <summary>
    /// Generation completion timestamp.
    /// </summary>
    public DateTime GenerationEndTime { get; init; }

    /// <summary>
    /// Performance metrics for the generation process.
    /// </summary>
    public IndexGenerationMetrics Metrics { get; init; } = new();

    /// <summary>
    /// Sheets created in the workbook with their purposes.
    /// </summary>
    public IReadOnlyList<IndexSheetInfo> Sheets { get; init; } = Array.Empty<IndexSheetInfo>();

    /// <summary>
    /// Any warnings or issues encountered during generation.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Whether the generation completed successfully.
    /// </summary>
    public bool IsSuccessful { get; init; } = true;

    /// <summary>
    /// Error message if generation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Bundle information that was indexed.
    /// </summary>
    public IndexedBundleInfo BundleInfo { get; init; } = new();
}

/// <summary>
/// Information about a sheet in the generated index workbook.
/// </summary>
public sealed record IndexSheetInfo
{
    /// <summary>
    /// Sheet name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Sheet purpose/type.
    /// </summary>
    public IndexSheetType Type { get; init; }

    /// <summary>
    /// Number of rows with data.
    /// </summary>
    public int DataRowCount { get; init; }

    /// <summary>
    /// Number of columns used.
    /// </summary>
    public int ColumnCount { get; init; }

    /// <summary>
    /// Whether the sheet includes hyperlinks.
    /// </summary>
    public bool HasHyperlinks { get; init; }

    /// <summary>
    /// Whether the sheet has conditional formatting applied.
    /// </summary>
    public bool HasConditionalFormatting { get; init; }

    /// <summary>
    /// Brief description of sheet contents.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Associated table names (if applicable).
    /// </summary>
    public IReadOnlyList<string> RelatedTables { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Performance metrics for index generation.
/// </summary>
public sealed record IndexGenerationMetrics
{
    /// <summary>
    /// Time spent analyzing bundle manifest.
    /// </summary>
    public TimeSpan ManifestAnalysisTime { get; init; }

    /// <summary>
    /// Time spent creating Excel sheets.
    /// </summary>
    public TimeSpan SheetCreationTime { get; init; }

    /// <summary>
    /// Time spent applying formatting.
    /// </summary>
    public TimeSpan FormattingTime { get; init; }

    /// <summary>
    /// Time spent writing to disk.
    /// </summary>
    public TimeSpan FileWriteTime { get; init; }

    /// <summary>
    /// Peak memory usage during generation.
    /// </summary>
    public long PeakMemoryUsage { get; init; }

    /// <summary>
    /// Number of hyperlinks created.
    /// </summary>
    public int HyperlinksCreated { get; init; }

    /// <summary>
    /// Number of conditional formatting rules applied.
    /// </summary>
    public int ConditionalFormattingRules { get; init; }
}

/// <summary>
/// Summary information about the indexed bundle.
/// </summary>
public sealed record IndexedBundleInfo
{
    /// <summary>
    /// Bundle export timestamp.
    /// </summary>
    public DateTime ExportTimestamp { get; init; }

    /// <summary>
    /// Source database path or identifier.
    /// </summary>
    public string SourceDatabase { get; init; } = string.Empty;

    /// <summary>
    /// Total record count across all tables.
    /// </summary>
    public long TotalRecordCount { get; init; }

    /// <summary>
    /// Total file size of all exported data.
    /// </summary>
    public long TotalDataSizeBytes { get; init; }

    /// <summary>
    /// Export formats used in the bundle.
    /// </summary>
    public IReadOnlyList<string> ExportFormats { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Partitioning strategies used.
    /// </summary>
    public IReadOnlyList<string> PartitioningStrategies { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Whether transformations were applied.
    /// </summary>
    public bool HasTransformations { get; init; }

    /// <summary>
    /// Data quality score (0-100).
    /// </summary>
    public int DataQualityScore { get; init; }
}

/// <summary>
/// Result of index validation operation.
/// </summary>
public sealed record IndexValidationResult
{
    /// <summary>
    /// Whether the index is valid and consistent with the bundle.
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
    /// Missing tables or files.
    /// </summary>
    public IReadOnlyList<string> MissingItems { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Inconsistencies found between index and actual data.
    /// </summary>
    public IReadOnlyList<string> Inconsistencies { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Validation metrics and timing.
    /// </summary>
    public IndexValidationMetrics Metrics { get; init; } = new();
}

/// <summary>
/// Metrics from index validation process.
/// </summary>
public sealed record IndexValidationMetrics
{
    /// <summary>
    /// Number of sheets validated.
    /// </summary>
    public int SheetsValidated { get; init; }

    /// <summary>
    /// Number of hyperlinks checked.
    /// </summary>
    public int HyperlinksChecked { get; init; }

    /// <summary>
    /// Number of data files verified.
    /// </summary>
    public int FilesVerified { get; init; }

    /// <summary>
    /// Time taken for validation.
    /// </summary>
    public TimeSpan ValidationTime { get; init; }
}

/// <summary>
/// Types of sheets in an index workbook.
/// </summary>
public enum IndexSheetType
{
    Dashboard,
    TableCatalog,
    PartitionMap,
    DataQuality,
    SchemaComparison,
    DataPreview,
    PerformanceMetrics,
    TransformationLog,
    FileInventory,
    ErrorLog
}

/// <summary>
/// Color schemes for Excel workbook formatting.
/// </summary>
public enum ExcelColorScheme
{
    Professional,
    Modern,
    Classic,
    HighContrast,
    Minimal
}

/// <summary>
/// Bundle manifest containing complete export information.
/// This would typically be generated by the Bundle Orchestration Engine.
/// </summary>
public sealed record BundleManifest
{
    /// <summary>
    /// Bundle unique identifier.
    /// </summary>
    public string BundleId { get; init; } = string.Empty;

    /// <summary>
    /// Bundle export timestamp.
    /// </summary>
    public DateTime ExportTimestamp { get; init; }

    /// <summary>
    /// Source database information.
    /// </summary>
    public SourceDatabaseInfo SourceDatabase { get; init; } = new();

    /// <summary>
    /// Table manifests for all exported tables.
    /// </summary>
    public IReadOnlyList<TableManifest> Tables { get; init; } = Array.Empty<TableManifest>();

    /// <summary>
    /// Export configuration used.
    /// </summary>
    public BundleExportConfiguration Configuration { get; init; } = new();

    /// <summary>
    /// Bundle-level metadata and statistics.
    /// </summary>
    public BundleStatistics Statistics { get; init; } = new();

    /// <summary>
    /// Data quality assessment results.
    /// </summary>
    public DataQualityAssessment DataQuality { get; init; } = new();

    /// <summary>
    /// Transformation information (if applied).
    /// </summary>
    public TransformationSummary? Transformations { get; init; }
}

/// <summary>
/// Information about a single table in the bundle.
/// </summary>
public sealed record TableManifest
{
    /// <summary>
    /// Table name.
    /// </summary>
    public string TableName { get; init; } = string.Empty;

    /// <summary>
    /// Table schema information.
    /// </summary>
    public TableSchemaInfo Schema { get; init; } = new();

    /// <summary>
    /// Export formats for this table.
    /// </summary>
    public IReadOnlyList<TableExportInfo> Exports { get; init; } = Array.Empty<TableExportInfo>();

    /// <summary>
    /// Partitioning information.
    /// </summary>
    public TablePartitioningSummary Partitioning { get; init; } = new();

    /// <summary>
    /// Table-level statistics.
    /// </summary>
    public TableStatistics Statistics { get; init; } = new();

    /// <summary>
    /// Data quality metrics for this table.
    /// </summary>
    public TableDataQuality DataQuality { get; init; } = new();
}

/// <summary>
/// Information about a table export in a specific format.
/// </summary>
public sealed record TableExportInfo
{
    /// <summary>
    /// Export format (xlsx, jsonl, parquet, etc.).
    /// </summary>
    public string Format { get; init; } = string.Empty;

    /// <summary>
    /// File paths for this export.
    /// </summary>
    public IReadOnlyList<string> FilePaths { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Total file size for this export.
    /// </summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>
    /// Export-specific metadata.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Placeholder records for the manifest structure.
/// These would be fully implemented in the Bundle Orchestration Engine.
/// </summary>
public sealed record SourceDatabaseInfo
{
    public string FilePath { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public DateTime LastModified { get; init; }
    public string SqliteVersion { get; init; } = string.Empty;
}

public sealed record BundleExportConfiguration
{
    public IReadOnlyList<string> IncludedTables { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExportFormats { get; init; } = Array.Empty<string>();
    public bool IncludeViews { get; init; }
    public string PartitioningStrategy { get; init; } = string.Empty;
}

public sealed record BundleStatistics
{
    public int TableCount { get; init; }
    public long TotalRecordCount { get; init; }
    public long TotalFileSizeBytes { get; init; }
    public TimeSpan ExportDuration { get; init; }
}

public sealed record DataQualityAssessment
{
    public int OverallScore { get; init; }
    public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();
}

public sealed record TransformationSummary
{
    public int TransformationsApplied { get; init; }
    public IReadOnlyList<string> TransformerTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, int> TransformationsByTable { get; init; } = new Dictionary<string, int>();
}

public sealed record TableSchemaInfo
{
    public IReadOnlyList<ColumnInfo> Columns { get; init; } = Array.Empty<ColumnInfo>();
    public IReadOnlyList<string> PrimaryKeyColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<IndexInfo> Indexes { get; init; } = Array.Empty<IndexInfo>();
}


public sealed record TablePartitioningSummary
{
    public string Strategy { get; init; } = string.Empty;
    public int PartitionCount { get; init; }
    public IReadOnlyList<PartitionInfo> Partitions { get; init; } = Array.Empty<PartitionInfo>();
}

public sealed record TableStatistics
{
    public long RecordCount { get; init; }
    public long SizeBytes { get; init; }
    public DateTime LastUpdated { get; init; }
    public TimeSpan ExportTime { get; init; }
}

public sealed record TableDataQuality
{
    public int QualityScore { get; init; }
    public long NullValueCount { get; init; }
    public long DuplicateRecordCount { get; init; }
    public IReadOnlyList<string> DataIssues { get; init; } = Array.Empty<string>();
}