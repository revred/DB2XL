using CommandLine;

namespace SqliteXport.Console.Options;

[Verb("analyze", HelpText = "Analyze SQLite database structure and content.")]
public class AnalyzeOptions : GlobalOptions
{
    [Value(0, Required = true, HelpText = "Path to SQLite database file.", MetaName = "database")]
    public string Database { get; set; } = string.Empty;

    [Option("output", Required = false, HelpText = "Save analysis to file (default: console output).")]
    public string? Output { get; set; }

    [Option("format", Required = false, HelpText = "Analysis output format: text, json, yaml.")]
    public string Format { get; set; } = "text";

    [Option("include-data", Required = false, HelpText = "Include data samples in analysis.")]
    public bool IncludeData { get; set; }

    [Option("sample-size", Required = false, HelpText = "Number of sample rows per table.")]
    public int SampleSize { get; set; } = 5;

    [Option("check-integrity", Required = false, HelpText = "Run SQLite integrity checks.")]
    public bool CheckIntegrity { get; set; }

    [Option("performance", Required = false, HelpText = "Include performance metrics and optimization suggestions.")]
    public bool Performance { get; set; }

    [Option("tables", Required = false, HelpText = "Comma-separated list of specific tables to analyze.")]
    public string? Tables { get; set; }

    [Option("pk-discovery", Required = false, HelpText = "Analyze primary key and indexing strategies.")]
    public bool PkDiscovery { get; set; } = true;

    [Option("pk-strategy", Required = false, HelpText = "Show which PK discovery strategy would be used for each table.")]
    public bool ShowPkStrategy { get; set; }

    [Option("pk-quality", Required = false, HelpText = "Analyze primary key quality and uniqueness characteristics.")]
    public bool PkQuality { get; set; }

    [Option("suggest-indexes", Required = false, HelpText = "Suggest missing indexes for better query performance.")]
    public bool SuggestIndexes { get; set; }

    [Option("deterministic-order", Required = false, HelpText = "Show deterministic ordering capabilities for each table.")]
    public bool DeterministicOrder { get; set; }
}