using System.Text.Json.Serialization;

namespace DB2XL.Configuration;

/// <summary>
/// Root configuration for database transformation pipelines
/// </summary>
public class TransformationConfig
{
    /// <summary>
    /// Version of the configuration format
    /// </summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Global settings that apply to all transformations
    /// </summary>
    public GlobalSettings Global { get; set; } = new();

    /// <summary>
    /// Table-specific transformation rules
    /// </summary>
    public Dictionary<string, TableConfig> Tables { get; set; } = new();

    /// <summary>
    /// Global transformation rules that apply to all tables
    /// </summary>
    public List<TransformerConfig> GlobalTransformers { get; set; } = new();
}

/// <summary>
/// Global settings for transformation behavior
/// </summary>
public class GlobalSettings
{
    /// <summary>
    /// Whether to enable transformations by default
    /// </summary>
    public bool EnableTransformations { get; set; } = true;

    /// <summary>
    /// Default error handling behavior
    /// </summary>
    public ErrorHandling ErrorHandling { get; set; } = ErrorHandling.LogAndContinue;

    /// <summary>
    /// Maximum number of transformer errors before stopping
    /// </summary>
    public int MaxErrors { get; set; } = 100;

    /// <summary>
    /// Performance settings
    /// </summary>
    public PerformanceSettings Performance { get; set; } = new();
}

/// <summary>
/// Performance-related configuration options
/// </summary>
public class PerformanceSettings
{
    /// <summary>
    /// Number of rows to process in each batch
    /// </summary>
    public int BatchSize { get; set; } = 10000;

    /// <summary>
    /// Whether to enable parallel processing of transformers
    /// </summary>
    public bool EnableParallelProcessing { get; set; } = true;

    /// <summary>
    /// Maximum degree of parallelism (0 = auto)
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } = 0;
}

/// <summary>
/// Configuration for a specific table
/// </summary>
public class TableConfig
{
    /// <summary>
    /// Whether to enable transformations for this table
    /// </summary>
    public bool EnableTransformations { get; set; } = true;

    /// <summary>
    /// Column-specific transformers
    /// </summary>
    public Dictionary<string, List<TransformerConfig>> Columns { get; set; } = new();

    /// <summary>
    /// Row-level transformers for this table
    /// </summary>
    public List<RowTransformerConfig> RowTransformers { get; set; } = new();

    /// <summary>
    /// Table-level filters or conditions
    /// </summary>
    public TableFilters Filters { get; set; } = new();
}

/// <summary>
/// Configuration for table-level filtering
/// </summary>
public class TableFilters
{
    /// <summary>
    /// SQL WHERE clause to filter rows (optional)
    /// </summary>
    public string? WhereClause { get; set; }

    /// <summary>
    /// Maximum number of rows to process (0 = unlimited)
    /// </summary>
    public int MaxRows { get; set; } = 0;

    /// <summary>
    /// Columns to exclude from transformation
    /// </summary>
    public List<string> ExcludeColumns { get; set; } = new();

    /// <summary>
    /// Columns to include (empty = include all)
    /// </summary>
    public List<string> IncludeColumns { get; set; } = new();
}

/// <summary>
/// Configuration for a single transformer
/// </summary>
public class TransformerConfig
{
    /// <summary>
    /// Name of the transformer (must be registered)
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Configuration parameters for the transformer
    /// </summary>
    public Dictionary<string, string> Config { get; set; } = new();

    /// <summary>
    /// Conditions when this transformer should apply
    /// </summary>
    public TransformerConditions? Conditions { get; set; }

    /// <summary>
    /// Order/priority for this transformer (lower = higher priority)
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Whether this transformer is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Configuration for row-level transformers
/// </summary>
public class RowTransformerConfig
{
    /// <summary>
    /// Name of the row transformer
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Configuration parameters
    /// </summary>
    public Dictionary<string, string> Config { get; set; } = new();

    /// <summary>
    /// Conditions when this transformer should apply
    /// </summary>
    public RowTransformerConditions? Conditions { get; set; }

    /// <summary>
    /// Order/priority for this transformer
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Whether this transformer is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Conditions for when a cell transformer should apply
/// </summary>
public class TransformerConditions
{
    /// <summary>
    /// Column name patterns (supports wildcards)
    /// </summary>
    public List<string> ColumnPatterns { get; set; } = new();

    /// <summary>
    /// Column names to exclude
    /// </summary>
    public List<string> ExcludeColumns { get; set; } = new();

    /// <summary>
    /// Required column data types
    /// </summary>
    public List<string> DataTypes { get; set; } = new();

    /// <summary>
    /// Regular expression pattern for cell values
    /// </summary>
    public string? ValuePattern { get; set; }

    /// <summary>
    /// Custom condition script (future extension)
    /// </summary>
    public string? CustomCondition { get; set; }
}

/// <summary>
/// Conditions for when a row transformer should apply
/// </summary>
public class RowTransformerConditions
{
    /// <summary>
    /// Row number ranges (1-based, inclusive)
    /// </summary>
    public List<RowRange> RowRanges { get; set; } = new();

    /// <summary>
    /// Value-based conditions
    /// </summary>
    public Dictionary<string, string> ColumnValueConditions { get; set; } = new();

    /// <summary>
    /// Custom condition script (future extension)
    /// </summary>
    public string? CustomCondition { get; set; }
}

/// <summary>
/// Represents a range of row numbers
/// </summary>
public class RowRange
{
    /// <summary>
    /// Starting row number (1-based, inclusive)
    /// </summary>
    public int Start { get; set; } = 1;

    /// <summary>
    /// Ending row number (1-based, inclusive, 0 = end of table)
    /// </summary>
    public int End { get; set; } = 0;
}

/// <summary>
/// Error handling strategies
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ErrorHandling
{
    /// <summary>
    /// Stop processing on first error
    /// </summary>
    StopOnError,

    /// <summary>
    /// Log error and continue processing
    /// </summary>
    LogAndContinue,

    /// <summary>
    /// Skip failed transformations silently
    /// </summary>
    SkipErrors,

    /// <summary>
    /// Use original value when transformation fails
    /// </summary>
    UseOriginalOnError
}