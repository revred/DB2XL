using CommandLine;

namespace DB2XL.Console.Options;

[Verb("mcp", HelpText = "Start MCP (Model Context Protocol) server for AI integration.")]
public class McpOptions : GlobalOptions
{
    [Option("port", Required = false, HelpText = "Port number for MCP server.", Default = 8080)]
    public int Port { get; set; } = 8080;

    [Option("host", Required = false, HelpText = "Host address to bind to.", Default = "localhost")]
    public string Host { get; set; } = "localhost";

    [Option("stdio", Required = false, HelpText = "Use stdio transport instead of HTTP.")]
    public bool UseStdio { get; set; }

    [Option("capabilities-only", Required = false, HelpText = "Output capabilities JSON and exit.")]
    public bool CapabilitiesOnly { get; set; }
}