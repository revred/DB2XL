using CommandLine;

namespace SqliteXport.Console.Options;

public abstract class GlobalOptions
{
    [Option('v', "verbose", Required = false, HelpText = "Enable verbose output.")]
    public bool Verbose { get; set; }

    [Option('q', "quiet", Required = false, HelpText = "Suppress all output except errors.")]
    public bool Quiet { get; set; }

    [Option("no-color", Required = false, HelpText = "Disable colored output.")]
    public bool NoColor { get; set; }
}