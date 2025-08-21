using DB2XL;
using DB2XL.Transform.Configuration;
using DB2XL.Transform.Interfaces;
using DB2XL.Query;
using DB2XL.DeltaExport;
using DB2XL.Console.Options;
using DB2XL.Console.Helpers;
using DB2XL.Core.Models;
using DB2XL.Core.Enums;
using DB2XL.Data.Schema;
using Spectre.Console;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DB2XL.Console.Commands;

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

            // Handle delta trigger installation if requested
            if (options.InstallChangelog)
            {
                using var connection = new SqliteConnection($"Data Source={options.Database};");
                connection.Open();
                if (await HandleDeltaExport(options, connection))
                {
                    AnsiConsole.MarkupLine("[green]✓[/] Changelog triggers installed successfully.");
                    return 0;
                }
            }

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

            // Handle delta export if requested
            if (options.Delta)
            {
                return await ExecuteDeltaExport(options);
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

        // Validate filter file
        if (!string.IsNullOrEmpty(options.FilterFile) && !File.Exists(options.FilterFile))
        {
            AnsiConsole.MarkupLine($"[red]✗ Error:[/] Filter file not found: {options.FilterFile}");
            return false;
        }

        // Validate delta options
        if (options.Delta)
        {
            if (!string.IsNullOrEmpty(options.DeltaStrategy))
            {
                var validStrategies = new[] { "watermark", "changelog", "full" };
                if (!validStrategies.Contains(options.DeltaStrategy.ToLowerInvariant()))
                {
                    AnsiConsole.MarkupLine($"[red]✗ Error:[/] Invalid delta strategy. Valid options: {string.Join(", ", validStrategies)}");
                    return false;
                }
            }
        }

        // Validate mutual exclusion: WHERE and filter file
        if (!string.IsNullOrEmpty(options.Where) && !string.IsNullOrEmpty(options.FilterFile))
        {
            AnsiConsole.MarkupLine("[red]✗ Error:[/] Cannot specify both --where and --filter options. Use one or the other.");
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
        // Load SelectionGrammar if provided
        var selectionGrammar = LoadSelectionGrammar(options);
        
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
            DualExportStrategy = dualStrategy,
            SelectionGrammar = selectionGrammar
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
        table.AddRow("Filter File", options.FilterFile ?? "None");
        table.AddRow("Order By", options.OrderBy ?? "Deterministic");
        table.AddRow("Max Rows", options.MaxRows?.ToString() ?? "Unlimited");
        table.AddRow("Delta Mode", options.Delta ? "Yes" : "No");
        if (options.Delta)
        {
            table.AddRow("Delta Strategy", options.DeltaStrategy ?? "watermark");
            table.AddRow("Checkpoint File", options.CheckpointFile ?? "Auto-generated");
            table.AddRow("Watermark Columns", options.WatermarkColumns ?? "Auto-detected");
        }

        AnsiConsole.Write(table);
    }

    private static int ShowCount(ExportOptions options)
    {
        try
        {
            AnsiConsole.MarkupLine("[blue]ℹ[/] Counting rows...");
            
            using var connection = new SqliteConnection($"Data Source={options.Database};Mode=ReadOnly;");
            connection.Open();
            
            var objects = SqliteSchemaReader.GetDatabaseObjects(connection, null, options.IncludeViews);
            var filteredObjects = objects.Where(o => string.IsNullOrEmpty(options.Tables) || 
                options.Tables.Split(',').Any(t => o.Name.Contains(t.Trim())));
            
            long totalRows = 0;
            var table = new Table();
            table.AddColumn("Table");
            table.AddColumn("Rows", c => c.RightAligned());
            
            foreach (var obj in filteredObjects)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM \"{obj.Name.Replace("\"", "\"\"")}\"";
                var count = Convert.ToInt64(cmd.ExecuteScalar());
                totalRows += count;
                table.AddRow(obj.Name, count.ToString("N0"));
            }
            
            table.AddEmptyRow();
            table.AddRow("[bold]Total[/]", $"[bold]{totalRows:N0}[/]");
            
            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ Error counting rows:[/] {ex.Message}");
            return 1;
        }
    }

    private static SelectionGrammar? LoadSelectionGrammar(ExportOptions options)
    {
        if (string.IsNullOrEmpty(options.FilterFile))
            return null;

        try
        {
            var json = File.ReadAllText(options.FilterFile);
            return JsonSerializer.Deserialize<SelectionGrammar>(json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load filter file '{options.FilterFile}': {ex.Message}", ex);
        }
    }

    private static async Task<bool> HandleDeltaExport(ExportOptions options, SqliteConnection connection)
    {
        if (!options.Delta)
            return false;

        var strategy = options.DeltaStrategy?.ToLowerInvariant() ?? "watermark";
        
        if (options.InstallChangelog && strategy == "changelog")
        {
            AnsiConsole.MarkupLine("[blue]ℹ[/] Installing changelog triggers...");
            
            var changeLogService = new ChangeLogDeltaService(null, new DB2XL.Query.PrimaryKeyDiscoveryService());
            var tables = SqliteSchemaReader.GetDatabaseObjects(connection, null, false);
            
            foreach (var table in tables)
            {
                var config = new ChangeLogConfig { CaptureFullRowData = true };
                await changeLogService.InstallChangeTrackingAsync(connection, table.Name, config);
                AnsiConsole.MarkupLine($"[green]✓[/] Installed triggers for table: {table.Name}");
            }
            
            return true; // Exit after installing triggers
        }

        return false;
    }

    private static async Task<int> ExecuteDeltaExport(ExportOptions options)
    {
        try
        {
            AnsiConsole.MarkupLine("[blue]ℹ[/] Starting delta export...");
            
            var format = DetermineFormat(options);
            var strategy = options.DeltaStrategy?.ToLowerInvariant() ?? "watermark";
            
            // Build delta export configuration
            var deltaConfig = BuildDeltaExportConfig(options, strategy);
            
            // Create modified export options for delta export
            var transformConfig = LoadTransformationConfig(options);
            var exportOptions = BuildExportOptions(options, transformConfig);
            
            // Use existing delta export API via DeltaExportExtensions
            
            await AnsiConsole.Progress().StartAsync(async ctx =>
            {
                var progressTask = ctx.AddTask("[green]Processing delta export[/]");
                
                if (format.Equals("jsonl", StringComparison.OrdinalIgnoreCase))
                {
                    AnsiConsole.MarkupLine("[yellow]⚠[/] Delta export for JSONL format not yet implemented - falling back to regular export");
                    await ExportJsonl(options, exportOptions, progressTask);
                }
                else
                {
                    // Use SqliteToExcel delta export functionality
                    progressTask.Description = "[green]Exporting with delta processing[/]";
                    
                    await Task.Run(async () =>
                    {
                        // Create delta-enabled options
                        var deltaOptions = new SqliteToExcelOptions
                        {
                            WriteAllAsText = exportOptions.WriteAllAsText,
                            PreserveNumericTypes = exportOptions.PreserveNumericTypes,
                            IncludeMetadataSheet = exportOptions.IncludeMetadataSheet,
                            ReadBatchSize = exportOptions.ReadBatchSize,
                            CommandTimeoutSeconds = exportOptions.CommandTimeoutSeconds,
                            IncludeViews = exportOptions.IncludeViews,
                            OrderRowsDeterministically = exportOptions.OrderRowsDeterministically,
                            SplitOversizeSheets = exportOptions.SplitOversizeSheets,
                            BlobMode = exportOptions.BlobMode,
                            TableNameLikeFilter = exportOptions.TableNameLikeFilter,
                            TransformationConfig = exportOptions.TransformationConfig,
                            TransformerRegistry = exportOptions.TransformerRegistry,
                            DualExportStrategy = exportOptions.DualExportStrategy,
                            DeltaExportConfig = deltaConfig
                        };
                        
                        await SqliteToExcelDeltaExtensions.ExportDeltaAsync(options.Database, options.Output, deltaOptions);
                    });
                }
                
                progressTask.Value = 100;
            });
            
            AnsiConsole.MarkupLine($"[green]✓[/] Delta export completed: {options.Output}");
            
            // Show checkpoint information if available
            var checkpointPath = options.CheckpointFile ?? Path.ChangeExtension(options.Output, ".checkpoint.json");
            if (File.Exists(checkpointPath))
            {
                AnsiConsole.MarkupLine($"[blue]ℹ[/] Checkpoint saved: {checkpointPath}");
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗ Delta export failed:[/] {ex.Message}");
            return 1;
        }
    }

    private static DeltaExportConfig BuildDeltaExportConfig(ExportOptions options, string strategy)
    {
        return strategy switch
        {
            "watermark" => new DeltaExportConfig
            {
                Strategy = DeltaStrategy.Watermark,
                WatermarkColumns = ParseWatermarkColumns(options.WatermarkColumns)
            },
            "changelog" => new DeltaExportConfig
            {
                Strategy = DeltaStrategy.ChangeLog,
                ChangeLogConfig = new ChangeLogConfig
                {
                    ChangeLogTableName = "__changes",
                    CaptureFullRowData = true,
                    AutoInstallTriggers = false
                }
            },
            "full" => new DeltaExportConfig
            {
                Strategy = DeltaStrategy.Full
            },
            _ => throw new ArgumentException($"Unsupported delta strategy: {strategy}")
        };
    }

    private static IReadOnlyList<string> ParseWatermarkColumns(string? watermarkColumns)
    {
        if (string.IsNullOrEmpty(watermarkColumns))
            return Array.Empty<string>();
            
        return watermarkColumns.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim())
            .ToArray();
    }

}