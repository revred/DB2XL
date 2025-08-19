using CommandLine;

namespace SqliteXport.Console.Options;

[Verb("export", HelpText = "Export SQLite database to Excel or JSONL formats.")]
public class ExportOptions : GlobalOptions
{
    [Value(0, Required = true, HelpText = "Path to SQLite database file.", MetaName = "database")]
    public string Database { get; set; } = string.Empty;

    [Value(1, Required = true, HelpText = "Output file (.xlsx) or directory (.jsonl).", MetaName = "output")]
    public string Output { get; set; } = string.Empty;

    // Format and output options
    [Option("format", Required = false, HelpText = "Output format: excel, jsonl (default: auto-detect from extension).")]
    public string? Format { get; set; }

    [Option("transform", Required = false, HelpText = "Apply intelligent transformations.")]
    public bool Transform { get; set; }

    [Option("config", Required = false, HelpText = "Transformation configuration file path.")]
    public string? Config { get; set; }

    [Option("dual-sheets", Required = false, HelpText = "Export both raw and transformed data to separate sheets.")]
    public bool DualSheets { get; set; }

    [Option("dual-workbooks", Required = false, HelpText = "Export raw and transformed data to separate workbooks.")]
    public bool DualWorkbooks { get; set; }

    [Option("metadata", Required = false, HelpText = "Include comprehensive metadata sheet.")]
    public bool Metadata { get; set; } = true;

    [Option("manifest", Required = false, HelpText = "Generate schema and provenance manifest.")]
    public bool Manifest { get; set; }

    // Data filtering (aligned with Filters.md vision)
    [Option("tables", Required = false, HelpText = "Comma-separated list of tables to export.")]
    public string? Tables { get; set; }

    [Option("exclude-tables", Required = false, HelpText = "Comma-separated list of tables to exclude.")]
    public string? ExcludeTables { get; set; }

    [Option("where", Required = false, HelpText = "SQL WHERE clause for row filtering.")]
    public string? Where { get; set; }

    [Option("max-rows", Required = false, HelpText = "Maximum rows per table.")]
    public int? MaxRows { get; set; }

    [Option("include-views", Required = false, HelpText = "Include database views in export.")]
    public bool IncludeViews { get; set; }

    // Column filtering
    [Option("columns", Required = false, HelpText = "Comma-separated list of specific columns to include.")]
    public string? Columns { get; set; }

    [Option("exclude-columns", Required = false, HelpText = "Comma-separated list of columns to exclude.")]
    public string? ExcludeColumns { get; set; }

    // Format options
    [Option("write-all-as-text", Required = false, HelpText = "Force all values as text (default: true).")]
    public bool? WriteAllAsText { get; set; }

    [Option("preserve-numeric-types", Required = false, HelpText = "Preserve numeric types in Excel.")]
    public bool PreserveNumericTypes { get; set; }

    [Option("blob-mode", Required = false, HelpText = "How to handle BLOB data: skip, hex, base64.")]
    public string? BlobMode { get; set; }

    [Option("split-oversized", Required = false, HelpText = "Split large tables across sheets.")]
    public bool? SplitOversized { get; set; }

    // Performance
    [Option("batch-size", Required = false, HelpText = "Rows per processing batch.")]
    public int? BatchSize { get; set; }

    [Option("parallel", Required = false, HelpText = "Enable parallel processing.")]
    public bool Parallel { get; set; }

    [Option("timeout", Required = false, HelpText = "Command timeout in seconds.")]
    public int? Timeout { get; set; }

    // Analysis options
    [Option("count", Required = false, HelpText = "Return fast row count only.")]
    public bool Count { get; set; }

    [Option("dry-run", Required = false, HelpText = "Show planned operations without executing.")]
    public bool DryRun { get; set; }

    [Option("strict", Required = false, HelpText = "Fail on transformer errors (default: log and continue).")]
    public bool Strict { get; set; }
}