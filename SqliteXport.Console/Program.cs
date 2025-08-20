using CommandLine;
using SqliteXport.Console.Commands;
using SqliteXport.Console.Options;
using Spectre.Console;
using System.Reflection;

namespace SqliteXport.Console;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            // Display banner
            ShowBanner();

            // Parse command line arguments and execute appropriate command
            return await Parser.Default.ParseArguments<ExportOptions, AnalyzeOptions>(args)
                .MapResult(
                    async (ExportOptions opts) => await ExportCommand.Execute(opts),
                    (AnalyzeOptions opts) => Task.FromResult(AnalyzeCommand.Execute(opts)),
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
            
            [yellow]Basic Examples:[/]
              [dim]sqlitexport export data.db output.xlsx --transform[/]
              [dim]sqlitexport analyze logs.db --pk-discovery --suggest-indexes[/]
              [dim]sqlitexport export trades.db trades.jsonl --where "amount > 1000"[/]
            
            [yellow]Advanced Filtering:[/]
              [dim]sqlitexport export db.sqlite data.xlsx --filter query.json[/]
              [dim]sqlitexport export db.sqlite data.xlsx --order-by "created_at" --order-desc[/]
            
            [yellow]Delta Exports:[/]
              [dim]sqlitexport export db.sqlite delta.xlsx --delta --delta-strategy watermark[/]
              [dim]sqlitexport export db.sqlite changes.xlsx --install-changelog[/]
              [dim]sqlitexport export db.sqlite inc.xlsx --delta --checkpoint-file state.json[/]
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
        if (errorList.Any(e => e is HelpRequestedError or VersionRequestedError))
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
}
