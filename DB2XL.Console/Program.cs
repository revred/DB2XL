using CommandLine;
using DB2XL.Console.Commands;
using DB2XL.Console.Options;
using Spectre.Console;
using System.Reflection;

namespace DB2XL.Console;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            // Display banner
            ShowBanner();

            // Parse command line arguments and execute appropriate command
            // Create stub classes to maintain command parser structure while Bundle is temporarily disabled
            return await Parser.Default.ParseArguments<ExportOptions, AnalyzeOptions, StubBundleOptions, McpOptions>(args)
                .MapResult(
                    async (ExportOptions opts) => await ExportCommand.Execute(opts),
                    (AnalyzeOptions opts) => Task.FromResult(AnalyzeCommand.Execute(opts)),
                    (StubBundleOptions opts) => Task.FromResult(HandleDisabledCommand("bundle")),
                    async (McpOptions opts) => await McpCommand.Execute(opts),
                    errs => Task.FromResult(HandleParseErrors(errs))
                );
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Fatal error:[/] {ex.Message}");
            return 1;
        }
    }

    private static void ShowBanner()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        
        var panel = new Panel($"""
            [bold blue]SqliteXport Console[/] v{version}
            
            [dim]Deterministic SQLite → Excel/JSONL exporter with advanced filtering & delta exports[/]
            [dim]AI-ready database analysis with PK discovery and performance optimization[/]
            
            [yellow]Commands:[/]
              [green]export[/]   Export SQLite database to Excel or JSONL with advanced filtering
              [green]analyze[/]  Analyze database structure, PKs, and performance metrics
              [dim]bundle[/]   Export to structured bundle with JSONL partitions and AI manifests (temporarily disabled)
              [green]mcp[/]      Start MCP server for AI assistant integration
            
            [yellow]Basic Examples:[/]
              [dim]sqlitexport export data.db output.xlsx --transform[/]
              [dim]sqlitexport analyze logs.db --pk-discovery --suggest-indexes[/]
              [dim]sqlitexport bundle app.db ./bundle_output --samples[/]
              [dim]sqlitexport export trades.db trades.jsonl --where "amount > 1000"[/]
            
            [yellow]Advanced Filtering:[/]
              [dim]sqlitexport export db.sqlite data.xlsx --filter query.json[/]
              [dim]sqlitexport export db.sqlite data.xlsx --order-by "created_at" --order-desc[/]
            
            [yellow]Delta Bundle Exports:[/]
              [dim]sqlitexport bundle app.db ./delta --delta --watermark-column updated_at[/]
              [dim]sqlitexport bundle logs.db ./inc --delta --delta-strategy changelog[/]
              [dim]sqlitexport bundle db.db ./bundle --install-changelog[/]
              [dim]sqlitexport bundle orders.db ./export --delta --pii-config redact.yaml[/]
            
            [yellow]AI Integration (MCP):[/]
              [dim]sqlitexport mcp --stdio[/]
              [dim]sqlitexport mcp --capabilities-only[/]
            """)
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Blue);

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private static int HandleParseErrors(IEnumerable<Error> errors)
    {
        var errorList = errors.ToList();
        
        // Check if this is a help or version request
        if (errorList.Any(e => e is HelpRequestedError or HelpVerbRequestedError or VersionRequestedError))
        {
            return 0;
        }

        // Show specific error information
        AnsiConsole.MarkupLine("[red]Command line parsing failed:[/]");
        
        foreach (var error in errorList)
        {
            var message = error switch
            {
                MissingRequiredOptionError missing => $"Missing required option: {missing.NameInfo.LongName}",
                MissingValueOptionError missingValue => $"Missing value for option: {missingValue.NameInfo.LongName}",
                UnknownOptionError unknown => $"Unknown option: {unknown.Token}",
                BadFormatTokenError badFormat => $"Invalid format for token: {badFormat.Token}",
                _ => error.ToString()
            };
            
            AnsiConsole.MarkupLine($"  • {message}");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Use --help for usage information[/]");
        
        return 1;
    }

    private static int HandleDisabledCommand(string commandName)
    {
        AnsiConsole.MarkupLine($"[yellow]The '{commandName}' command is temporarily disabled.[/]");
        AnsiConsole.MarkupLine($"[dim]This feature is being updated and will be available in a future release.[/]");
        AnsiConsole.MarkupLine($"[dim]Available commands: export, analyze[/]");
        return 1;
    }
}

// Stub classes to maintain command parser structure
[Verb("bundle", HelpText = "Export to structured bundle with JSONL partitions (temporarily disabled).")]
internal class StubBundleOptions
{
}
