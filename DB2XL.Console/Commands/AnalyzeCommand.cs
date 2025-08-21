using DB2XL;
using DB2XL.Query;
using DB2XL.Console.Options;
using DB2XL.Console.Helpers;
using DB2XL.Core.Models;
using DB2XL.Data.Schema;
using Spectre.Console;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Text;

namespace DB2XL.Console.Commands;

public static class AnalyzeCommand
{
    public static int Execute(AnalyzeOptions options)
    {
        try
        {
            ConsoleHelper.SetupOutput(options.Quiet, options.Verbose, options.NoColor);

            if (!ValidateInputs(options))
                return 1;

            ConsoleHelper.WriteInfo("Analyzing database structure and content...");

            var analysis = PerformAnalysis(options);

            if (string.IsNullOrEmpty(options.Output))
            {
                DisplayAnalysis(analysis, options.Format);
            }
            else
            {
                SaveAnalysis(analysis, options.Output, options.Format);
                ConsoleHelper.WriteSuccess($"Analysis saved to: {options.Output}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            ConsoleHelper.WriteError($"Analysis failed: {ex.Message}");
            if (options.Verbose)
            {
                AnsiConsole.WriteException(ex);
            }
            return 1;
        }
    }

    private static bool ValidateInputs(AnalyzeOptions options)
    {
        if (!File.Exists(options.Database))
        {
            ConsoleHelper.WriteError($"Database file not found: {options.Database}");
            return false;
        }

        var validFormats = new[] { "text", "json", "yaml" };
        if (!validFormats.Contains(options.Format.ToLowerInvariant()))
        {
            ConsoleHelper.WriteError($"Invalid format: {options.Format}. Valid formats: {string.Join(", ", validFormats)}");
            return false;
        }

        return true;
    }

    private static DatabaseAnalysis PerformAnalysis(AnalyzeOptions options)
    {
        using var connection = new SqliteConnection($"Data Source={options.Database};Mode=ReadOnly;Cache=Shared;Pooling=True;");
        connection.Open();

        var analysis = new DatabaseAnalysis
        {
            DatabasePath = options.Database,
            AnalysisTimestamp = DateTime.UtcNow,
            FileSize = new FileInfo(options.Database).Length
        };

        // Get database metadata
        analysis.DatabaseInfo = GetDatabaseInfo(connection);

        // Get table information
        var tableFilter = ParseTableFilter(options.Tables);
        analysis.Tables = GetTableAnalysis(connection, tableFilter, options);

        // Integrity check if requested
        if (options.CheckIntegrity)
        {
            analysis.IntegrityCheck = PerformIntegrityCheck(connection);
        }

        // Performance analysis if requested
        if (options.Performance)
        {
            analysis.PerformanceMetrics = GetPerformanceMetrics(connection, analysis.Tables);
        }

        return analysis;
    }

    private static DatabaseInfo GetDatabaseInfo(SqliteConnection connection)
    {
        var info = new DatabaseInfo();

        try
        {
            using var cmd = connection.CreateCommand();
            
            cmd.CommandText = "PRAGMA journal_mode;";
            info.JournalMode = cmd.ExecuteScalar()?.ToString() ?? "unknown";

            cmd.CommandText = "PRAGMA user_version;";
            info.UserVersion = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);

            cmd.CommandText = "PRAGMA schema_version;";
            info.SchemaVersion = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);

            cmd.CommandText = "PRAGMA page_size;";
            info.PageSize = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);

            cmd.CommandText = "PRAGMA page_count;";
            info.PageCount = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);

            // Check for enabled extensions
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name = 'json_extract';";
            info.JsonExtensionAvailable = Convert.ToInt32(cmd.ExecuteScalar()) > 0;

            // Get SQLite version
            cmd.CommandText = "SELECT sqlite_version();";
            info.SqliteVersion = cmd.ExecuteScalar()?.ToString() ?? "unknown";
        }
        catch (Exception ex)
        {
            // Log warning but continue
            ConsoleHelper.WriteWarning($"Could not retrieve some database info: {ex.Message}");
        }

        return info;
    }

    private static List<TableAnalysis> GetTableAnalysis(SqliteConnection connection, HashSet<string>? tableFilter, AnalyzeOptions options)
    {
        var tables = new List<TableAnalysis>();
        
        // Get table information using DatabaseDiscovery
        var discoveredTables = SqliteSchemaReader.GetDatabaseObjects(connection, null, true);

        foreach (var table in discoveredTables)
        {
            if (tableFilter != null && !tableFilter.Contains(table.Name))
                continue;

            var analysis = new TableAnalysis
            {
                Name = table.Name,
                Type = table.Type,
                Columns = GetColumnAnalysis(connection, table.Name, options),
            };

            // Get row count
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM \"{table.Name.Replace("\"", "\"\"")}\"";
                analysis.RowCount = Convert.ToInt64(cmd.ExecuteScalar());
            }
            catch
            {
                analysis.RowCount = -1; // Indicates error
            }

            // Primary key discovery using advanced DB2XL.Query service
            if (options.PkDiscovery || options.ShowPkStrategy || options.PkQuality || options.DeterministicOrder)
            {
                var pkService = new DB2XL.Query.PrimaryKeyDiscoveryService();
                var pkInfo = pkService.DiscoverPrimaryKey(connection, table.Name);
                
                analysis.PrimaryKeyInfo = new PrimaryKeyAnalysis
                {
                    Strategy = pkInfo.Strategy.ToString(),
                    Columns = pkInfo.Columns.ToList(),
                    IsGuaranteed = pkInfo.IsDeterministic,
                    QualityScore = CalculatePkQuality(analysis.Columns, pkInfo),
                    DeterministicOrdering = pkInfo.IsDeterministic ? "Guaranteed" : pkInfo.Columns.Any() ? "Best-effort" : "None"
                };
                
                // Legacy field for backward compatibility
                analysis.PrimaryKeyStrategy = FormatPkStrategy(pkInfo);
            }

            // Get data samples if requested
            if (options.IncludeData && analysis.RowCount > 0)
            {
                analysis.DataSample = GetDataSample(connection, table.Name, analysis.Columns, options.SampleSize);
            }

            tables.Add(analysis);
        }

        return tables;
    }

    private static List<TableInfo> GetTablesFromDatabase(SqliteConnection connection, bool includeViews)
    {
        var tables = new List<TableInfo>();
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"
            SELECT name, type 
            FROM sqlite_master 
            WHERE type IN ('table'{(includeViews ? ", 'view'" : "")})
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(new TableInfo(reader.GetString(0), reader.GetString(1)));
        }

        return tables;
    }

    private static List<ColumnAnalysis> GetColumnAnalysis(SqliteConnection connection, string tableName, AnalyzeOptions options)
    {
        var columns = new List<ColumnAnalysis>();
        var dbColumns = GetColumnsFromDatabase(connection, tableName);

        foreach (var column in dbColumns)
        {
            var analysis = new ColumnAnalysis
            {
                Name = column.Name,
                DataType = column.Type,
                NotNull = column.NotNull,
                DefaultValue = column.DefaultValue?.ToString(),
                IsPrimaryKey = column.IsPrimaryKey,
                PrimaryKeyOrder = column.IsPrimaryKey ? 1 : 0
            };

            // Analyze column characteristics
            if (options.IncludeData)
            {
                AnalyzeColumnData(connection, tableName, column.Name, analysis);
            }

            columns.Add(analysis);
        }

        return columns;
    }

    private static void AnalyzeColumnData(SqliteConnection connection, string tableName, string columnName, ColumnAnalysis analysis)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            
            // Get distinct value count
            cmd.CommandText = $"SELECT COUNT(DISTINCT \"{columnName.Replace("\"", "\"\"")}\") FROM \"{tableName.Replace("\"", "\"\"")}\"";
            analysis.DistinctValues = Convert.ToInt64(cmd.ExecuteScalar());

            // Get null count
            cmd.CommandText = $"SELECT COUNT(*) FROM \"{tableName.Replace("\"", "\"\"")}\" WHERE \"{columnName.Replace("\"", "\"\"")}\" IS NULL";
            analysis.NullCount = Convert.ToInt64(cmd.ExecuteScalar());

            // Check for potential transformations based on Filters.md patterns
            analysis.SuggestedTransformers = SuggestTransformers(columnName, analysis.DataType);
        }
        catch
        {
            // Ignore errors in data analysis
        }
    }

    private static List<string> SuggestTransformers(string columnName, string dataType)
    {
        var suggestions = new List<string>();
        var lowerName = columnName.ToLowerInvariant();

        // Time-based suggestions (aligned with Filters.md transformers)
        if (lowerName.Contains("time") || lowerName.Contains("date") || lowerName.Contains("ts"))
        {
            if (dataType.Equals("INTEGER", StringComparison.OrdinalIgnoreCase))
            {
                if (lowerName.Contains("tick"))
                    suggestions.Add("ticks (convert .NET ticks to ISO-8601)");
                else
                    suggestions.Add("epoch (convert Unix timestamp to ISO-8601)");
            }
            else if (dataType.Equals("REAL", StringComparison.OrdinalIgnoreCase))
            {
                suggestions.Add("julian-day (convert SQLite Julian Day to ISO-8601)");
            }
        }

        // JSON suggestions
        if (lowerName.Contains("json") || lowerName.Contains("payload") || lowerName.Contains("data"))
        {
            suggestions.Add("json-compact (minify JSON)");
            suggestions.Add("json-pretty (format JSON for readability)");
            suggestions.Add("json-flatten (extract nested properties)");
        }

        // PII suggestions
        if (lowerName.Contains("email") || lowerName.Contains("phone") || lowerName.Contains("card") || lowerName.Contains("ssn"))
        {
            suggestions.Add("mask (redact sensitive information)");
        }

        return suggestions;
    }

    private static string FormatPkStrategy(PrimaryKeyInfo pkInfo)
    {
        return pkInfo.Strategy switch
        {
            PrimaryKeyStrategy.ExplicitPrimaryKey => $"Explicit PK: [{string.Join(", ", pkInfo.Columns)}]",
            PrimaryKeyStrategy.UniqueIndex => $"Unique Index: [{string.Join(", ", pkInfo.Columns)}]",
            PrimaryKeyStrategy.ImplicitRowId => "Implicit rowid",
            PrimaryKeyStrategy.SyntheticHash => "Synthetic hash (no suitable PK found)",
            _ => "Unknown"
        };
    }

    private static double CalculatePkQuality(List<ColumnAnalysis> columns, PrimaryKeyInfo pkInfo)
    {
        if (pkInfo.Strategy == PrimaryKeyStrategy.SyntheticHash)
            return 0.0; // Synthetic hash is lowest quality

        if (pkInfo.Strategy == PrimaryKeyStrategy.ImplicitRowId)
            return 0.7; // Rowid is reliable but not ideal for replication

        if (pkInfo.Strategy == PrimaryKeyStrategy.ExplicitPrimaryKey)
            return 1.0; // Explicit PK is highest quality

        if (pkInfo.Strategy == PrimaryKeyStrategy.UniqueIndex)
            return 0.9; // Unique index is very good

        return 0.5; // Default for unknown strategies
    }

    private static List<string> SuggestMissingIndexes(SqliteConnection connection, List<TableAnalysis> tables)
    {
        var suggestions = new List<string>();
        
        foreach (var table in tables)
        {
            // Suggest indexes for large tables without explicit PKs
            if (table.RowCount > 10000 && table.PrimaryKeyInfo?.Strategy == "ImplicitRowId")
            {
                suggestions.Add($"Consider adding explicit primary key to table '{table.Name}' ({table.RowCount:N0} rows)");
            }

            // Suggest indexes for foreign key-like columns
            var fkCandidates = table.Columns
                .Where(c => c.Name.EndsWith("_id", StringComparison.OrdinalIgnoreCase) || 
                           c.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase))
                .Where(c => !c.IsPrimaryKey && c.DistinctValues > 1 && c.DistinctValues < table.RowCount * 0.8)
                .ToList();
                
            foreach (var candidate in fkCandidates)
            {
                suggestions.Add($"Consider adding index on '{table.Name}.{candidate.Name}' (likely foreign key, {candidate.DistinctValues:N0} distinct values)");
            }
        }
        
        return suggestions;
    }

    private static List<Dictionary<string, object?>> GetDataSample(SqliteConnection connection, string tableName, List<ColumnAnalysis> columns, int sampleSize)
    {
        var sample = new List<Dictionary<string, object?>>();
        
        try
        {
            var columnNames = string.Join(", ", columns.Select(c => $"\"{c.Name.Replace("\"", "\"\"")}\""));
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SELECT {columnNames} FROM \"{tableName.Replace("\"", "\"\"")}\" LIMIT {sampleSize}";
            
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                sample.Add(row);
            }
        }
        catch { }

        return sample;
    }

    private static IntegrityCheckResult PerformIntegrityCheck(SqliteConnection connection)
    {
        var result = new IntegrityCheckResult();
        
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            
            using var reader = cmd.ExecuteReader();
            var issues = new List<string>();
            
            while (reader.Read())
            {
                var issue = reader.GetString(0);
                if (!issue.Equals("ok", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(issue);
                }
            }

            result.IsHealthy = issues.Count == 0;
            result.Issues = issues;
        }
        catch (Exception ex)
        {
            result.IsHealthy = false;
            result.Issues = new List<string> { $"Integrity check failed: {ex.Message}" };
        }

        return result;
    }

    private static PerformanceMetrics GetPerformanceMetrics(SqliteConnection connection, List<TableAnalysis> tables)
    {
        var metrics = new PerformanceMetrics();
        
        metrics.TotalTables = tables.Count;
        metrics.TotalRows = tables.Sum(t => Math.Max(0, t.RowCount));
        metrics.LargestTable = tables.OrderByDescending(t => t.RowCount).FirstOrDefault()?.Name ?? "N/A";
        
        // Use enhanced index suggestion analysis
        metrics.IndexSuggestions = SuggestMissingIndexes(connection, tables);
        
        // Add PK quality analysis
        var lowQualityPks = tables
            .Where(t => t.PrimaryKeyInfo?.QualityScore < 0.8)
            .ToList();
            
        if (lowQualityPks.Any())
        {
            metrics.PrimaryKeyIssues = lowQualityPks
                .Select(t => $"Table '{t.Name}' has low-quality PK strategy: {t.PrimaryKeyInfo?.Strategy} (score: {t.PrimaryKeyInfo?.QualityScore:P0})")
                .ToList();
        }
        else
        {
            metrics.PrimaryKeyIssues = new List<string>();
        }

        return metrics;
    }

    private static HashSet<string>? ParseTableFilter(string? tables)
    {
        if (string.IsNullOrEmpty(tables))
            return null;

        return tables.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static void DisplayAnalysis(DatabaseAnalysis analysis, string format)
    {
        switch (format.ToLowerInvariant())
        {
            case "json":
                var json = JsonSerializer.Serialize(analysis, new JsonSerializerOptions { WriteIndented = true });
                AnsiConsole.WriteLine(json);
                break;
                
            case "yaml":
                ConsoleHelper.WriteWarning("YAML output not implemented yet - showing JSON format");
                goto case "json";
                
            default: // text
                DisplayTextAnalysis(analysis);
                break;
        }
    }

    private static void DisplayTextAnalysis(DatabaseAnalysis analysis)
    {
        // Database header
        var rule = new Rule($"[green]Database Analysis: {Path.GetFileName(analysis.DatabasePath)}[/]");
        AnsiConsole.Write(rule);

        // Database info table
        var dbTable = new Table();
        dbTable.AddColumn("Property");
        dbTable.AddColumn("Value");
        
        dbTable.AddRow("File Size", $"{analysis.FileSize:N0} bytes");
        dbTable.AddRow("SQLite Version", analysis.DatabaseInfo.SqliteVersion);
        dbTable.AddRow("Journal Mode", analysis.DatabaseInfo.JournalMode);
        dbTable.AddRow("Page Size", $"{analysis.DatabaseInfo.PageSize:N0} bytes");
        dbTable.AddRow("Page Count", $"{analysis.DatabaseInfo.PageCount:N0}");
        dbTable.AddRow("JSON Extension", analysis.DatabaseInfo.JsonExtensionAvailable ? "Available" : "Not Available");

        AnsiConsole.Write(dbTable);
        AnsiConsole.WriteLine();

        // Tables summary
        AnsiConsole.Write(new Rule("[blue]Tables Summary[/]"));
        
        var tablesTable = new Table();
        tablesTable.AddColumn("Table Name");
        tablesTable.AddColumn("Type");
        tablesTable.AddColumn("Columns");
        tablesTable.AddColumn("Rows");
        tablesTable.AddColumn("PK Strategy");
        tablesTable.AddColumn("PK Quality");
        tablesTable.AddColumn("Ordering");

        foreach (var table in analysis.Tables)
        {
            var qualityDisplay = table.PrimaryKeyInfo?.QualityScore switch
            {
                >= 0.9 => "[green]Excellent[/]",
                >= 0.8 => "[yellow]Good[/]",
                >= 0.6 => "[orange]Fair[/]",
                _ => "[red]Poor[/]"
            };
            
            var orderingDisplay = table.PrimaryKeyInfo?.DeterministicOrdering switch
            {
                "Guaranteed" => "[green]Guaranteed[/]",
                "Best-effort" => "[yellow]Best-effort[/]",
                _ => "[red]None[/]"
            };

            tablesTable.AddRow(
                table.Name,
                table.Type,
                table.Columns.Count.ToString(),
                table.RowCount >= 0 ? table.RowCount.ToString("N0") : "Error",
                table.PrimaryKeyStrategy ?? "Unknown",
                qualityDisplay,
                orderingDisplay
            );
        }

        AnsiConsole.Write(tablesTable);

        // Show integrity check results if performed
        if (analysis.IntegrityCheck != null)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[yellow]Integrity Check[/]"));
            
            if (analysis.IntegrityCheck.IsHealthy)
            {
                ConsoleHelper.WriteSuccess("Database integrity check passed");
            }
            else
            {
                ConsoleHelper.WriteError($"Database integrity issues found: {analysis.IntegrityCheck.Issues.Count}");
                foreach (var issue in analysis.IntegrityCheck.Issues)
                {
                    AnsiConsole.MarkupLine($"  • {issue}");
                }
            }
        }

        // Show performance metrics if requested
        if (analysis.PerformanceMetrics != null)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[cyan]Performance Analysis[/]"));
            
            AnsiConsole.MarkupLine($"Total Tables: {analysis.PerformanceMetrics.TotalTables}");
            AnsiConsole.MarkupLine($"Total Rows: {analysis.PerformanceMetrics.TotalRows:N0}");
            AnsiConsole.MarkupLine($"Largest Table: {analysis.PerformanceMetrics.LargestTable}");

            if (analysis.PerformanceMetrics.IndexSuggestions.Any())
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[yellow]Index Suggestions:[/]");
                foreach (var suggestion in analysis.PerformanceMetrics.IndexSuggestions)
                {
                    AnsiConsole.MarkupLine($"  • {suggestion}");
                }
            }
            
            if (analysis.PerformanceMetrics.PrimaryKeyIssues?.Any() == true)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[red]Primary Key Issues:[/]");
                foreach (var issue in analysis.PerformanceMetrics.PrimaryKeyIssues)
                {
                    AnsiConsole.MarkupLine($"  • {issue}");
                }
            }
        }
    }

    private static void SaveAnalysis(DatabaseAnalysis analysis, string outputPath, string format)
    {
        string content = format.ToLowerInvariant() switch
        {
            "json" => JsonSerializer.Serialize(analysis, new JsonSerializerOptions { WriteIndented = true }),
            "yaml" => throw new NotImplementedException("YAML output not yet implemented"),
            _ => ConvertToText(analysis)
        };

        File.WriteAllText(outputPath, content, Encoding.UTF8);
    }

    private static string ConvertToText(DatabaseAnalysis analysis)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"Database Analysis: {Path.GetFileName(analysis.DatabasePath)}");
        sb.AppendLine($"Generated: {analysis.AnalysisTimestamp:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        
        sb.AppendLine("Database Information:");
        sb.AppendLine($"  File Size: {analysis.FileSize:N0} bytes");
        sb.AppendLine($"  SQLite Version: {analysis.DatabaseInfo.SqliteVersion}");
        sb.AppendLine($"  Journal Mode: {analysis.DatabaseInfo.JournalMode}");
        sb.AppendLine();
        
        sb.AppendLine("Tables:");
        foreach (var table in analysis.Tables)
        {
            sb.AppendLine($"  {table.Name} ({table.Type}):");
            sb.AppendLine($"    Rows: {(table.RowCount >= 0 ? table.RowCount.ToString("N0") : "Error")}");
            sb.AppendLine($"    Columns: {table.Columns.Count}");
            sb.AppendLine($"    PK Strategy: {table.PrimaryKeyStrategy}");
        }
        
        return sb.ToString();
    }

    private static List<ColumnInfo> GetColumnsFromDatabase(SqliteConnection connection, string tableName)
    {
        var columns = new List<ColumnInfo>();
        
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")";
        
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(new ColumnInfo(
                reader.GetString(1), // name
                reader.GetString(2), // type
                reader.GetBoolean(3), // notnull
                reader.IsDBNull(4) ? null : reader.GetString(4), // dflt_value
                reader.GetInt32(5) > 0 // pk (convert int to bool)
            ));
        }

        return columns;
    }
}

// ColumnInfo moved to DB2XL.Core.Models to avoid duplication

// Analysis data structures
public class DatabaseAnalysis
{
    public string DatabasePath { get; set; } = string.Empty;
    public DateTime AnalysisTimestamp { get; set; }
    public long FileSize { get; set; }
    public DatabaseInfo DatabaseInfo { get; set; } = new();
    public List<TableAnalysis> Tables { get; set; } = new();
    public IntegrityCheckResult? IntegrityCheck { get; set; }
    public PerformanceMetrics? PerformanceMetrics { get; set; }
}

public class DatabaseInfo
{
    public string SqliteVersion { get; set; } = string.Empty;
    public string JournalMode { get; set; } = string.Empty;
    public long UserVersion { get; set; }
    public long SchemaVersion { get; set; }
    public int PageSize { get; set; }
    public long PageCount { get; set; }
    public bool JsonExtensionAvailable { get; set; }
}

public class TableAnalysis
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public List<ColumnAnalysis> Columns { get; set; } = new();
    public string? PrimaryKeyStrategy { get; set; }
    public PrimaryKeyAnalysis? PrimaryKeyInfo { get; set; }
    public List<Dictionary<string, object?>> DataSample { get; set; } = new();
}

public class ColumnAnalysis
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool NotNull { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsPrimaryKey { get; set; }
    public int PrimaryKeyOrder { get; set; }
    public long DistinctValues { get; set; }
    public long NullCount { get; set; }
    public List<string> SuggestedTransformers { get; set; } = new();
}

public class IntegrityCheckResult
{
    public bool IsHealthy { get; set; }
    public List<string> Issues { get; set; } = new();
}

public class PerformanceMetrics
{
    public int TotalTables { get; set; }
    public long TotalRows { get; set; }
    public string LargestTable { get; set; } = string.Empty;
    public List<string> IndexSuggestions { get; set; } = new();
    public List<string> PrimaryKeyIssues { get; set; } = new();
}

public class PrimaryKeyAnalysis
{
    public string Strategy { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public bool IsGuaranteed { get; set; }
    public double QualityScore { get; set; }
    public string DeterministicOrdering { get; set; } = string.Empty;
}