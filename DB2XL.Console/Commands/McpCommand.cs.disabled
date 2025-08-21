using DB2XL.Console.Options;
using DB2XL.Console.Services;
using Spectre.Console;
using System.Text;

namespace DB2XL.Console.Commands;

/// <summary>
/// Command implementation for MCP (Model Context Protocol) server operations.
/// Provides AI assistants with structured access to DB2XL functionality.
/// </summary>
public static class McpCommand
{
    /// <summary>
    /// Execute MCP server command with the provided options.
    /// </summary>
    /// <param name="options">Parsed command line options</param>
    /// <returns>Exit code (0 = success, non-zero = error)</returns>
    public static async Task<int> Execute(McpOptions options)
    {
        try
        {
            var mcpHost = new McpServerHost();

            // Handle capabilities-only mode
            if (options.CapabilitiesOnly)
            {
                var capabilities = mcpHost.GetCapabilities();
                System.Console.WriteLine(capabilities);
                return 0;
            }

            // Handle stdio transport mode
            if (options.UseStdio)
            {
                return await RunStdioServer(mcpHost, options);
            }

            // Handle HTTP server mode (future implementation)
            AnsiConsole.MarkupLine("[red]HTTP server mode not yet implemented. Use --stdio for now.[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]MCP server failed:[/] {ex.Message}");
            if (options.Verbose)
            {
                AnsiConsole.WriteException(ex);
            }
            return 1;
        }
    }

    private static async Task<int> RunStdioServer(McpServerHost mcpHost, McpOptions options)
    {
        AnsiConsole.MarkupLine("[blue]Starting DB2XL MCP Server (stdio mode)...[/]");
        AnsiConsole.MarkupLine("[dim]Ready to receive JSON-RPC requests on stdin[/]");
        
        if (options.Verbose)
        {
            AnsiConsole.MarkupLine("[dim]Use Ctrl+C to stop the server[/]");
            AnsiConsole.MarkupLine("[dim]Send JSON-RPC requests to stdin, responses will be written to stdout[/]");
        }

        try
        {
            var cancellationTokenSource = new CancellationTokenSource();
            
            // Handle Ctrl+C gracefully
            System.Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cancellationTokenSource.Cancel();
            };

            using var reader = new StreamReader(System.Console.OpenStandardInput(), Encoding.UTF8);
            using var writer = new StreamWriter(System.Console.OpenStandardOutput(), Encoding.UTF8);

            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    // Read JSON-RPC request from stdin
                    var requestLine = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(requestLine))
                    {
                        // EOF reached
                        break;
                    }

                    if (options.Verbose)
                    {
                        AnsiConsole.MarkupLine($"[dim]Received request: {requestLine}[/]");
                    }

                    // Process the request
                    var response = await mcpHost.ProcessRequestAsync(requestLine);

                    // Write response to stdout
                    await writer.WriteLineAsync(response);
                    await writer.FlushAsync();

                    if (options.Verbose)
                    {
                        AnsiConsole.MarkupLine($"[dim]Sent response: {response}[/]");
                    }
                }
                catch (Exception ex)
                {
                    if (options.Verbose)
                    {
                        AnsiConsole.MarkupLine($"[red]Error processing request:[/] {ex.Message}");
                    }

                    // Send error response
                    var errorResponse = $$"""
                        {
                            "jsonrpc": "2.0",
                            "id": null,
                            "error": {
                                "code": -32603,
                                "message": "{{ex.Message}}"
                            }
                        }
                        """;

                    await writer.WriteLineAsync(errorResponse);
                    await writer.FlushAsync();
                }
            }

            AnsiConsole.MarkupLine("[yellow]MCP server stopped gracefully[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]MCP server error:[/] {ex.Message}");
            return 1;
        }
    }
}