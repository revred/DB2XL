using Spectre.Console;

namespace SqliteXport.Console.Helpers;

public static class ConsoleHelper
{
    public static void SetupOutput(bool quiet, bool verbose, bool noColor)
    {
        if (noColor)
        {
            AnsiConsole.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
        }

        // Configure console behavior based on options
        if (quiet)
        {
            // In quiet mode, we'll only show errors
            // This would typically involve configuring a logger
        }
        else if (verbose)
        {
            // In verbose mode, show detailed information
            // This would typically involve setting log level to Debug
        }
    }

    public static void WriteInfo(string message, bool quiet = false)
    {
        if (!quiet)
        {
            AnsiConsole.MarkupLine($"[blue]ℹ[/] {message}");
        }
    }

    public static void WriteSuccess(string message, bool quiet = false)
    {
        if (!quiet)
        {
            AnsiConsole.MarkupLine($"[green]✓[/] {message}");
        }
    }

    public static void WriteWarning(string message, bool quiet = false)
    {
        if (!quiet)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠[/] {message}");
        }
    }

    public static void WriteError(string message)
    {
        AnsiConsole.MarkupLine($"[red]✗[/] {message}");
    }

    public static void WriteVerbose(string message, bool verbose = false)
    {
        if (verbose)
        {
            AnsiConsole.MarkupLine($"[dim]{message}[/]");
        }
    }
}