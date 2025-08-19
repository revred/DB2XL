using DB2XL;
using DB2XL.Configuration;
using DB2XL.Transformers;
using SqliteXport.Console.Options;
using SqliteXport.Console.Helpers;
using Spectre.Console;
using System.Globalization;

namespace SqliteXport.Console.Commands;

public static class ExportCommand
{
    public static async Task<int> Execute(ExportOptions options)
    {
        try
        {
            ConsoleHelper.SetupOutput(options.Quiet, options.Verbose, options.NoColor);

            // Validate inputs
            if (!ValidateInputs(options))
                return 1;

            // Show dry-run information if requested
            if (options.DryRun)
            {
                ShowDryRun(options);
                return 0;
            }

            // Show count only if requested
            if (options.Count)
            {
                return ShowCount(options);
            }

            // Determine output format
            var format = DetermineFormat(options);
            AnsiConsole.MarkupLine($"[green]Exporting to {format} format...[/]");

            // Setup options
            var transformConfig = LoadTransformationConfig(options);
            var exportOptions = BuildExportOptions(options, transformConfig);

            // Execute export with progress reporting
            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[green]Exporting database[/]");
                    
                    if (format.Equals("jsonl", StringComparison.OrdinalIgnoreCase))
                    {
                        await ExportJsonl(options, exportOptions, task);
                    }
                    else
                    {
                        await ExportExcel(options, exportOptions, task);
                    }
                    
                    task.Value = 100;
                });

            AnsiConsole.MarkupLine($"[green]✓[/] Export completed: {options.Output}");

            // Generate manifest if requested
            if (options.Manifest && format.Equals("excel", StringComparison.OrdinalIgnoreCase))
            {
                var manifest = SqliteToExcel.GenerateManifest(options.Database, options.Output, exportOptions);
                var manifestPath = Path.ChangeExtension(options.Output, ".manifest.json");
                DB2XL.Schema.ManifestGenerator.SaveManifest(manifest, manifestPath);
                AnsiConsole.MarkupLine($"[blue]ℹ[/] Manifest saved: {manifestPath}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ Error:[/] {ex.Message}");
            if (options.Verbose)
            {
                AnsiConsole.WriteException(ex);
            }
            return 1;
        }
    }

    private static bool ValidateInputs(ExportOptions options)
    {
        if (!File.Exists(options.Database))
        {
            AnsiConsole.MarkupLine($"[red]✗ Error:[/] Database file not found: {options.Database}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.Output))
        {
            AnsiConsole.MarkupLine("[red]✗ Error:[/] Output path is required.");
            return false;
        }

        if (!string.IsNullOrEmpty(options.Config) && !File.Exists(options.Config))
        {
            AnsiConsole.MarkupLine($"[red]✗ Error:[/] Configuration file not found: {options.Config}");
            return false;
        }

        return true;
    }

    private static string DetermineFormat(ExportOptions options)
    {
        if (!string.IsNullOrEmpty(options.Format))
        {
            return options.Format.ToLowerInvariant() switch
            {
                "excel" or "xlsx" => "excel",
                "jsonl" or "json" => "jsonl",
                _ => throw new ArgumentException($"Unsupported format: {options.Format}")
            };
        }

        // Auto-detect from extension
        var extension = Path.GetExtension(options.Output).ToLowerInvariant();
        return extension switch
        {
            ".xlsx" => "excel",
            ".jsonl" => "jsonl",
            _ when Directory.Exists(Path.GetDirectoryName(options.Output)) || options.Output.EndsWith("/") || options.Output.EndsWith("\\") => "jsonl",
            _ => "excel"
        };
    }

    private static SqliteToExcelOptions BuildExportOptions(ExportOptions options, TransformationConfig? transformConfig)
    {
        // Determine dual export strategy
        var dualStrategy = DualExportStrategy.TransformedOnly;
        if (options.DualWorkbooks)
            dualStrategy = DualExportStrategy.DualWorkbooks;
        else if (options.DualSheets)
            dualStrategy = DualExportStrategy.DualSheets;
        else if (options.Transform)
            dualStrategy = DualExportStrategy.TransformedOnly;
        else
            dualStrategy = DualExportStrategy.RawOnly;

        // Determine BLOB mode
        var blobMode = BlobRenderMode.Hex;
        if (!string.IsNullOrEmpty(options.BlobMode))
        {
            blobMode = options.BlobMode.ToLowerInvariant() switch
            {
                "skip" => BlobRenderMode.Skip,
                "hex" => BlobRenderMode.Hex,
                "base64" => BlobRenderMode.Base64,
                _ => throw new ArgumentException($"Invalid BLOB mode: {options.BlobMode}")
            };
        }

        // Handle table filtering
        string? tableFilter = null;
        if (!string.IsNullOrEmpty(options.Tables))
        {
            // For now, use a simple LIKE filter - this would be enhanced with the selection grammar from Filters.md
            var tables = options.Tables.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (tables.Length == 1)
            {
                tableFilter = tables[0].Trim();
            }
        }

        var exportOptions = new SqliteToExcelOptions
        {
            WriteAllAsText = options.WriteAllAsText ?? true,
            PreserveNumericTypes = options.PreserveNumericTypes,
            IncludeMetadataSheet = options.Metadata,
            ReadBatchSize = options.BatchSize ?? 25000,
            CommandTimeoutSeconds = options.Timeout ?? 180,
            IncludeViews = options.IncludeViews,
            OrderRowsDeterministically = true,
            SplitOversizeSheets = options.SplitOversized ?? true,
            BlobMode = blobMode,
            TableNameLikeFilter = tableFilter,
            TransformationConfig = transformConfig,
            TransformerRegistry = transformConfig != null ? TransformerRegistryBuilder.CreateDefault() : null,
            DualExportStrategy = dualStrategy
        };

        return exportOptions;
    }

    private static TransformationConfig? LoadTransformationConfig(ExportOptions options)
    {
        if (!options.Transform && string.IsNullOrEmpty(options.Config))
            return null;

        if (!string.IsNullOrEmpty(options.Config))
        {
            return ConfigurationLoader.LoadFromFile(options.Config);
        }

        // Create a default transformation config when --transform is used without --config
        return new TransformationConfig
        {
            Version = "1.0",
            Global = new GlobalSettings
            {
                EnableTransformations = true,
                ErrorHandling = options.Strict ? ErrorHandling.StopOnError : ErrorHandling.LogAndContinue,
                Performance = new PerformanceSettings
                {
                    EnableParallelProcessing = options.Parallel,
                    BatchSize = options.BatchSize ?? 10000
                }
            },
            GlobalTransformers = new List<TransformerConfig>
            {
                new TransformerConfig
                {
                    Name = "coalesce",
                    Config = new Dictionary<string, string> { ["default"] = "N/A" },
                    Priority = 1000,
                    Enabled = true
                }
            }
        };
    }

    private static async Task ExportExcel(ExportOptions options, SqliteToExcelOptions exportOptions, Spectre.Console.ProgressTask task)
    {
        await Task.Run(() =>
        {
            task.Description = "Exporting to Excel format";
            SqliteToExcel.Export(options.Database, options.Output, exportOptions);
        });
    }

    private static async Task ExportJsonl(ExportOptions options, SqliteToExcelOptions exportOptions, Spectre.Console.ProgressTask task)
    {
        await Task.Run(() =>
        {
            task.Description = "Exporting to JSONL format";
            
            // Convert to JSONL options
            var jsonlOptions = new JsonLinesExportOptions
            {
                WriteAllAsStrings = exportOptions.WriteAllAsText,
                IncludeSchemaManifests = options.Manifest,
                CommandTimeoutSeconds = exportOptions.CommandTimeoutSeconds,
                TableNameLikeFilter = exportOptions.TableNameLikeFilter,
                IncludeViews = exportOptions.IncludeViews,
                BlobMode = exportOptions.BlobMode,
                OrderRowsDeterministically = exportOptions.OrderRowsDeterministically,
                TransformationConfig = exportOptions.TransformationConfig,
                TransformerRegistry = exportOptions.TransformerRegistry,
                DualExportStrategy = exportOptions.DualExportStrategy
            };

            DB2XL.JsonLinesExporter.Export(options.Database, options.Output, jsonlOptions);
        });
    }

    private static void ShowDryRun(ExportOptions options)
    {
        var table = new Table();
        table.AddColumn("Setting");
        table.AddColumn("Value");

        table.AddRow("Database", options.Database);
        table.AddRow("Output", options.Output);
        table.AddRow("Format", DetermineFormat(options));
        table.AddRow("Transform", options.Transform ? "Yes" : "No");
        table.AddRow("Config", options.Config ?? "None");
        table.AddRow("Tables", options.Tables ?? "All");
        table.AddRow("Where", options.Where ?? "None");
        table.AddRow("Max Rows", options.MaxRows?.ToString() ?? "Unlimited");

        AnsiConsole.Write(table);
    }

    private static int ShowCount(ExportOptions options)
    {
        try
        {
            AnsiConsole.MarkupLine("[blue]ℹ[/] Counting rows...");
            
            // This would be implemented with the database introspection from Filters.md
            // For now, show a placeholder
            AnsiConsole.MarkupLine("[yellow]⚠[/] Count functionality not yet implemented - requires database introspection API");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ Error counting rows:[/] {ex.Message}");
            return 1;
        }
    }
}