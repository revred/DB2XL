using System.Diagnostics;
using System.Globalization;
using ClosedXML.Excel;
using DB2XL.Core.Models;
using DB2XL.Core.Services;

namespace DB2XL.Export.Bundle.Services;

/// <summary>
/// High-performance Excel index generator that creates comprehensive navigation workbooks for bundle exports.
/// Optimized for large datasets with advanced formatting and interactive features.
/// </summary>
public sealed class ExcelIndexGenerator : IExcelIndexGenerator
{
    private static readonly Dictionary<ExcelColorScheme, ExcelColorPalette> ColorSchemes = new()
    {
        [ExcelColorScheme.Professional] = new ExcelColorPalette
        {
            Primary = XLColor.FromHtml("#2E86AB"),
            Secondary = XLColor.FromHtml("#A23B72"),
            Accent = XLColor.FromHtml("#F18F01"),
            Background = XLColor.FromHtml("#F8F9FA"),
            Text = XLColor.FromHtml("#212529"),
            Success = XLColor.FromHtml("#28A745"),
            Warning = XLColor.FromHtml("#FFC107"),
            Error = XLColor.FromHtml("#DC3545")
        },
        [ExcelColorScheme.Modern] = new ExcelColorPalette
        {
            Primary = XLColor.FromHtml("#6366F1"),
            Secondary = XLColor.FromHtml("#8B5CF6"),
            Accent = XLColor.FromHtml("#06B6D4"),
            Background = XLColor.FromHtml("#F9FAFB"),
            Text = XLColor.FromHtml("#111827"),
            Success = XLColor.FromHtml("#10B981"),
            Warning = XLColor.FromHtml("#F59E0B"),
            Error = XLColor.FromHtml("#EF4444")
        },
        [ExcelColorScheme.Classic] = new ExcelColorPalette
        {
            Primary = XLColor.FromHtml("#1F4E79"),
            Secondary = XLColor.FromHtml("#7030A0"),
            Accent = XLColor.FromHtml("#C65911"),
            Background = XLColor.White,
            Text = XLColor.Black,
            Success = XLColor.Green,
            Warning = XLColor.Gold,
            Error = XLColor.Red
        }
    };

    /// <summary>
    /// Generates a comprehensive Excel index workbook for a bundle export.
    /// </summary>
    public async Task<ExcelIndexResult> GenerateIndexWorkbookAsync(
        BundleManifest bundleManifest,
        string outputFilePath,
        ExcelIndexOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundleManifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFilePath);
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        var startTime = DateTime.UtcNow;
        var warnings = new List<string>();
        var sheets = new List<IndexSheetInfo>();
        var metrics = new IndexGenerationMetrics();

        try
        {
            // Ensure output directory exists
            var directory = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var workbook = new XLWorkbook();
            var colorPalette = ColorSchemes[options.ColorScheme];

            // Set workbook properties
            SetWorkbookProperties(workbook, bundleManifest, options);

            var analysisStopwatch = Stopwatch.StartNew();
            var bundleStats = AnalyzeBundleManifest(bundleManifest);
            analysisStopwatch.Stop();
            metrics = metrics with { ManifestAnalysisTime = analysisStopwatch.Elapsed };

            var sheetStopwatch = Stopwatch.StartNew();

            // Generate dashboard sheet
            if (options.IncludeDashboard)
            {
                var dashboardSheet = await CreateDashboardSheetAsync(workbook, bundleManifest, bundleStats, colorPalette, options, cancellationToken);
                sheets.Add(dashboardSheet);
            }

            // Generate table catalog
            if (options.IncludeTableCatalog)
            {
                var catalogSheets = await CreateTableCatalogSheetsAsync(workbook, bundleManifest, colorPalette, options, cancellationToken);
                sheets.AddRange(catalogSheets);
            }

            // Generate partition map
            if (options.IncludePartitionMap)
            {
                var partitionSheets = await CreatePartitionMapSheetsAsync(workbook, bundleManifest, colorPalette, options, cancellationToken);
                sheets.AddRange(partitionSheets);
            }

            // Generate data quality assessment
            if (options.IncludeDataQuality)
            {
                var qualitySheet = await CreateDataQualitySheetAsync(workbook, bundleManifest, colorPalette, options, cancellationToken);
                sheets.Add(qualitySheet);
            }

            // Generate data preview sheets
            if (options.IncludeDataPreview)
            {
                var previewSheets = await CreateDataPreviewSheetsAsync(workbook, bundleManifest, colorPalette, options, cancellationToken);
                sheets.AddRange(previewSheets);
            }

            // Generate performance metrics
            if (options.IncludePerformanceMetrics)
            {
                var performanceSheet = await CreatePerformanceMetricsSheetAsync(workbook, bundleManifest, colorPalette, options, cancellationToken);
                sheets.Add(performanceSheet);
            }

            // Generate transformation log
            if (options.IncludeTransformationLog && bundleManifest.Transformations != null)
            {
                var transformationSheet = await CreateTransformationLogSheetAsync(workbook, bundleManifest, colorPalette, options, cancellationToken);
                sheets.Add(transformationSheet);
            }

            sheetStopwatch.Stop();
            metrics = metrics with { SheetCreationTime = sheetStopwatch.Elapsed };

            // Apply advanced formatting
            var formattingStopwatch = Stopwatch.StartNew();
            if (options.EnableAdvancedFormatting)
            {
                await ApplyAdvancedFormattingAsync(workbook, sheets, colorPalette, options, cancellationToken);
            }
            formattingStopwatch.Stop();
            metrics = metrics with { FormattingTime = formattingStopwatch.Elapsed };

            // Save workbook
            var writeStopwatch = Stopwatch.StartNew();
            workbook.SaveAs(outputFilePath);
            writeStopwatch.Stop();
            metrics = metrics with { FileWriteTime = writeStopwatch.Elapsed };

            stopwatch.Stop();
            var endTime = DateTime.UtcNow;

            var fileInfo = new FileInfo(outputFilePath);

            return new ExcelIndexResult
            {
                FilePath = outputFilePath,
                SheetCount = sheets.Count,
                TableCount = bundleManifest.Tables.Count,
                PartitionCount = bundleManifest.Tables.Sum(t => t.Partitioning.PartitionCount),
                FileSizeBytes = fileInfo.Length,
                GenerationStartTime = startTime,
                GenerationEndTime = endTime,
                Metrics = metrics,
                Sheets = sheets.AsReadOnly(),
                Warnings = warnings.AsReadOnly(),
                IsSuccessful = true,
                BundleInfo = new IndexedBundleInfo
                {
                    ExportTimestamp = bundleManifest.ExportTimestamp,
                    SourceDatabase = bundleManifest.SourceDatabase.FilePath,
                    TotalRecordCount = bundleManifest.Statistics.TotalRecordCount,
                    TotalDataSizeBytes = bundleManifest.Statistics.TotalFileSizeBytes,
                    ExportFormats = bundleManifest.Configuration.ExportFormats,
                    PartitioningStrategies = bundleManifest.Tables.Select(t => t.Partitioning.Strategy).Distinct().ToList().AsReadOnly(),
                    HasTransformations = bundleManifest.Transformations != null,
                    DataQualityScore = bundleManifest.DataQuality.OverallScore
                }
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ExcelIndexResult
            {
                FilePath = outputFilePath,
                GenerationStartTime = startTime,
                GenerationEndTime = DateTime.UtcNow,
                Warnings = warnings.AsReadOnly(),
                IsSuccessful = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Generates a focused table index for a specific set of tables.
    /// </summary>
    public async Task<ExcelIndexResult> GenerateTableIndexAsync(
        IReadOnlyList<TableManifest> tableManifests,
        string outputFilePath,
        ExcelIndexOptions options,
        CancellationToken cancellationToken = default)
    {
        // Create a minimal bundle manifest for the specified tables
        var bundleManifest = new BundleManifest
        {
            BundleId = Guid.NewGuid().ToString(),
            ExportTimestamp = DateTime.UtcNow,
            Tables = tableManifests,
            Statistics = new BundleStatistics
            {
                TableCount = tableManifests.Count,
                TotalRecordCount = tableManifests.Sum(t => t.Statistics.RecordCount),
                TotalFileSizeBytes = tableManifests.Sum(t => t.Statistics.SizeBytes)
            }
        };

        return await GenerateIndexWorkbookAsync(bundleManifest, outputFilePath, options, cancellationToken);
    }

    /// <summary>
    /// Updates an existing index workbook with new table data.
    /// </summary>
    public async Task<ExcelIndexResult> UpdateIndexWorkbookAsync(
        string existingIndexPath,
        BundleManifest updatedManifest,
        ExcelIndexOptions options,
        CancellationToken cancellationToken = default)
    {
        // For simplicity, regenerate the entire workbook
        // In a production implementation, this could be optimized to update only changed sheets
        var backupPath = existingIndexPath + ".backup." + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        
        if (File.Exists(existingIndexPath))
        {
            File.Copy(existingIndexPath, backupPath);
        }

        try
        {
            var result = await GenerateIndexWorkbookAsync(updatedManifest, existingIndexPath, options, cancellationToken);
            
            // Clean up backup if successful
            if (result.IsSuccessful && File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            return result;
        }
        catch
        {
            // Restore backup on failure
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, existingIndexPath, true);
                File.Delete(backupPath);
            }
            throw;
        }
    }

    /// <summary>
    /// Validates an index workbook against its source bundle.
    /// </summary>
    public async Task<IndexValidationResult> ValidateIndexAsync(
        string indexFilePath,
        BundleManifest bundleManifest,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Make async for consistency

        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var warnings = new List<string>();
        var missingItems = new List<string>();
        var inconsistencies = new List<string>();

        try
        {
            if (!File.Exists(indexFilePath))
            {
                errors.Add($"Index file not found: {indexFilePath}");
                return new IndexValidationResult { IsValid = false, Errors = errors.AsReadOnly() };
            }

            using var workbook = new XLWorkbook(indexFilePath);
            
            // Validate expected sheets exist
            var expectedSheets = GetExpectedSheetNames(bundleManifest);
            var actualSheets = workbook.Worksheets.Select(ws => ws.Name).ToHashSet();

            foreach (var expectedSheet in expectedSheets)
            {
                if (!actualSheets.Contains(expectedSheet))
                {
                    missingItems.Add($"Missing sheet: {expectedSheet}");
                }
            }

            // Validate table catalog consistency
            if (workbook.Worksheets.Contains("Table Catalog"))
            {
                var catalogSheet = workbook.Worksheet("Table Catalog");
                var catalogTables = ExtractTableNamesFromCatalog(catalogSheet);
                var manifestTables = bundleManifest.Tables.Select(t => t.TableName).ToHashSet();

                foreach (var manifestTable in manifestTables)
                {
                    if (!catalogTables.Contains(manifestTable))
                    {
                        inconsistencies.Add($"Table missing from catalog: {manifestTable}");
                    }
                }
            }

            stopwatch.Stop();

            var metrics = new IndexValidationMetrics
            {
                SheetsValidated = workbook.Worksheets.Count,
                ValidationTime = stopwatch.Elapsed
            };

            return new IndexValidationResult
            {
                IsValid = !errors.Any() && !inconsistencies.Any(),
                Errors = errors.AsReadOnly(),
                Warnings = warnings.AsReadOnly(),
                MissingItems = missingItems.AsReadOnly(),
                Inconsistencies = inconsistencies.AsReadOnly(),
                Metrics = metrics
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Validation failed: {ex.Message}");
            return new IndexValidationResult
            {
                IsValid = false,
                Errors = errors.AsReadOnly(),
                Metrics = new IndexValidationMetrics { ValidationTime = stopwatch.Elapsed }
            };
        }
    }

    #region Private Helper Methods

    private static void SetWorkbookProperties(XLWorkbook workbook, BundleManifest bundleManifest, ExcelIndexOptions options)
    {
        var properties = workbook.Properties;
        properties.Title = options.WorkbookTitle ?? $"DB2XL Bundle Index - {bundleManifest.SourceDatabase.FilePath}";
        properties.Author = options.Author ?? "DB2XL Bundle Export System";
        properties.Created = DateTime.UtcNow;
        // properties.LastModified = DateTime.UtcNow; // Not available in this version
        properties.Comments = $"Generated for bundle {bundleManifest.BundleId} at {bundleManifest.ExportTimestamp:yyyy-MM-dd HH:mm:ss}";
        
        // Custom properties not available in this version of ClosedXML
        // foreach (var (key, value) in options.CustomMetadata)
        // {
        //     properties.CustomProperties.Add(key, value);
        // }
    }

    private static BundleAnalysis AnalyzeBundleManifest(BundleManifest bundleManifest)
    {
        return new BundleAnalysis
        {
            TotalTables = bundleManifest.Tables.Count,
            TotalPartitions = bundleManifest.Tables.Sum(t => t.Partitioning.PartitionCount),
            TotalRecords = bundleManifest.Statistics.TotalRecordCount,
            TotalSizeBytes = bundleManifest.Statistics.TotalFileSizeBytes,
            LargestTable = bundleManifest.Tables.OrderByDescending(t => t.Statistics.RecordCount).FirstOrDefault(),
            PartitioningStrategies = bundleManifest.Tables.Select(t => t.Partitioning.Strategy).Distinct().ToList(),
            ExportFormats = bundleManifest.Configuration.ExportFormats.ToList(),
            AverageQualityScore = bundleManifest.Tables.Average(t => t.DataQuality.QualityScore)
        };
    }

    private static async Task<IndexSheetInfo> CreateDashboardSheetAsync(
        XLWorkbook workbook,
        BundleManifest bundleManifest,
        BundleAnalysis analysis,
        ExcelColorPalette colorPalette,
        ExcelIndexOptions options,
        CancellationToken cancellationToken)
    {
        await Task.Yield(); // Make async
        
        var worksheet = workbook.Worksheets.Add("Dashboard");
        var row = 1;

        // Title
        worksheet.Cell(row, 1).Value = "DB2XL Bundle Export Dashboard";
        worksheet.Cell(row, 1).Style.Font.FontSize = 18;
        worksheet.Cell(row, 1).Style.Font.Bold = true;
        worksheet.Cell(row, 1).Style.Font.FontColor = colorPalette.Primary;
        row += 2;

        // Bundle Information Section
        worksheet.Cell(row, 1).Value = "Bundle Information";
        worksheet.Cell(row, 1).Style.Font.FontSize = 14;
        worksheet.Cell(row, 1).Style.Font.Bold = true;
        row++;

        var bundleInfo = new[]
        {
            ("Bundle ID", bundleManifest.BundleId),
            ("Export Date", bundleManifest.ExportTimestamp.ToString("yyyy-MM-dd HH:mm:ss")),
            ("Source Database", bundleManifest.SourceDatabase.FilePath),
            ("Database Size", FormatBytes(bundleManifest.SourceDatabase.FileSizeBytes)),
            ("SQLite Version", bundleManifest.SourceDatabase.SqliteVersion)
        };

        foreach (var (label, value) in bundleInfo)
        {
            worksheet.Cell(row, 1).Value = label;
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            worksheet.Cell(row, 2).Value = value;
            row++;
        }

        row += 2;

        // Statistics Section
        worksheet.Cell(row, 1).Value = "Export Statistics";
        worksheet.Cell(row, 1).Style.Font.FontSize = 14;
        worksheet.Cell(row, 1).Style.Font.Bold = true;
        row++;

        var statistics = new[]
        {
            ("Total Tables", analysis.TotalTables.ToString("N0")),
            ("Total Records", analysis.TotalRecords.ToString("N0")),
            ("Total Data Size", FormatBytes(analysis.TotalSizeBytes)),
            ("Total Partitions", analysis.TotalPartitions.ToString("N0")),
            ("Export Duration", bundleManifest.Statistics.ExportDuration.ToString(@"hh\:mm\:ss")),
            ("Average Quality Score", analysis.AverageQualityScore.ToString("F1") + "/100")
        };

        foreach (var (label, value) in statistics)
        {
            worksheet.Cell(row, 1).Value = label;
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            worksheet.Cell(row, 2).Value = value;
            row++;
        }

        // Auto-fit columns
        worksheet.Columns().AdjustToContents();

        return new IndexSheetInfo
        {
            Name = "Dashboard",
            Type = IndexSheetType.Dashboard,
            DataRowCount = row - 1,
            ColumnCount = 2,
            Description = "High-level overview of bundle export statistics and metadata"
        };
    }

    private static async Task<IReadOnlyList<IndexSheetInfo>> CreateTableCatalogSheetsAsync(
        XLWorkbook workbook,
        BundleManifest bundleManifest,
        ExcelColorPalette colorPalette,
        ExcelIndexOptions options,
        CancellationToken cancellationToken)
    {
        await Task.Yield(); // Make async
        
        var sheets = new List<IndexSheetInfo>();
        var tables = bundleManifest.Tables.ToList();
        var tablesPerSheet = Math.Min(options.MaxRowsPerSheet, tables.Count);
        var sheetCount = (int)Math.Ceiling((double)tables.Count / tablesPerSheet);

        for (int sheetIndex = 0; sheetIndex < sheetCount; sheetIndex++)
        {
            var sheetName = sheetCount > 1 ? $"Table Catalog ({sheetIndex + 1})" : "Table Catalog";
            var worksheet = workbook.Worksheets.Add(sheetName);
            var row = 1;

            // Headers
            var headers = new[] { "Table Name", "Record Count", "Columns", "Primary Key", "Partitions", "Formats", "Quality Score", "Export Size" };
            for (int col = 0; col < headers.Length; col++)
            {
                var cell = worksheet.Cell(row, col + 1);
                cell.Value = headers[col];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = colorPalette.Primary;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
            row++;

            // Data rows
            var startIndex = sheetIndex * tablesPerSheet;
            var endIndex = Math.Min(startIndex + tablesPerSheet, tables.Count);
            
            for (int i = startIndex; i < endIndex; i++)
            {
                var table = tables[i];
                var dataRow = new object[]
                {
                    table.TableName,
                    table.Statistics.RecordCount.ToString("N0"),
                    table.Schema.Columns.Count,
                    string.Join(", ", table.Schema.PrimaryKeyColumns),
                    table.Partitioning.PartitionCount,
                    string.Join(", ", table.Exports.Select(e => e.Format)),
                    table.DataQuality.QualityScore + "/100",
                    FormatBytes(table.Statistics.SizeBytes)
                };

                for (int col = 0; col < dataRow.Length; col++)
                {
                    var cell = worksheet.Cell(row, col + 1);
                    cell.SetValue(dataRow[col]?.ToString() ?? "");
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    
                    // Apply conditional formatting for quality scores
                    if (col == 6 && options.EnableConditionalFormatting) // Quality Score column
                    {
                        var score = table.DataQuality.QualityScore;
                        cell.Style.Fill.BackgroundColor = score >= 80 ? colorPalette.Success :
                                                         score >= 60 ? colorPalette.Warning : colorPalette.Error;
                    }
                }
                row++;
            }

            // Auto-fit columns
            worksheet.Columns().AdjustToContents();

            // Freeze panes
            if (options.EnableAdvancedFormatting)
            {
                worksheet.SheetView.FreezeRows(1);
            }

            sheets.Add(new IndexSheetInfo
            {
                Name = sheetName,
                Type = IndexSheetType.TableCatalog,
                DataRowCount = endIndex - startIndex,
                ColumnCount = headers.Length,
                HasConditionalFormatting = options.EnableConditionalFormatting,
                Description = "Detailed catalog of all exported tables with statistics and metadata",
                RelatedTables = tables.Skip(startIndex).Take(endIndex - startIndex).Select(t => t.TableName).ToList().AsReadOnly()
            });
        }

        return sheets.AsReadOnly();
    }

    private static async Task<IReadOnlyList<IndexSheetInfo>> CreatePartitionMapSheetsAsync(
        XLWorkbook workbook,
        BundleManifest bundleManifest,
        ExcelColorPalette colorPalette,
        ExcelIndexOptions options,
        CancellationToken cancellationToken)
    {
        await Task.Yield(); // Make async
        
        var worksheet = workbook.Worksheets.Add("Partition Map");
        var row = 1;

        // Headers
        var headers = new[] { "Table Name", "Partition Label", "Strategy", "Record Count", "File Path", "Size", "Format" };
        for (int col = 0; col < headers.Length; col++)
        {
            var cell = worksheet.Cell(row, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = colorPalette.Primary;
            cell.Style.Font.FontColor = XLColor.White;
        }
        row++;

        // Data rows
        foreach (var table in bundleManifest.Tables)
        {
            foreach (var partition in table.Partitioning.Partitions)
            {
                foreach (var export in table.Exports)
                {
                    foreach (var filePath in export.FilePaths)
                    {
                        var relativePath = options.BundleRootPath != null && filePath.StartsWith(options.BundleRootPath)
                            ? Path.GetRelativePath(options.BundleRootPath, filePath)
                            : filePath;

                        var dataRow = new object[]
                        {
                            table.TableName,
                            partition.PartitionLabel,
                            table.Partitioning.Strategy,
                            partition.RowCount.ToString("N0"),
                            relativePath,
                            FormatBytes(new FileInfo(filePath).Exists ? new FileInfo(filePath).Length : 0),
                            export.Format
                        };

                        for (int col = 0; col < dataRow.Length; col++)
                        {
                            var cell = worksheet.Cell(row, col + 1);
                            cell.SetValue(dataRow[col]?.ToString() ?? "");
                            
                            // Add hyperlink to file if enabled and file exists
                            if (col == 4 && options.IncludeFileHyperlinks && File.Exists(filePath))
                            {
                                cell.SetHyperlink(new XLHyperlink(filePath));
                                cell.Style.Font.FontColor = colorPalette.Primary;
                                cell.Style.Font.Underline = XLFontUnderlineValues.Single;
                            }
                        }
                        row++;
                    }
                }
            }
        }

        // Auto-fit columns
        worksheet.Columns().AdjustToContents();

        return new List<IndexSheetInfo>
        {
            new()
            {
                Name = "Partition Map",
                Type = IndexSheetType.PartitionMap,
                DataRowCount = row - 1,
                ColumnCount = headers.Length,
                HasHyperlinks = options.IncludeFileHyperlinks,
                Description = "Complete mapping of all partitions to their corresponding files"
            }
        }.AsReadOnly();
    }

    private static async Task<IndexSheetInfo> CreateDataQualitySheetAsync(
        XLWorkbook workbook,
        BundleManifest bundleManifest,
        ExcelColorPalette colorPalette,
        ExcelIndexOptions options,
        CancellationToken cancellationToken)
    {
        await Task.Yield(); // Make async
        
        var worksheet = workbook.Worksheets.Add("Data Quality");
        var row = 1;

        // Headers
        var headers = new[] { "Table Name", "Quality Score", "Null Values", "Duplicates", "Issues", "Record Count" };
        for (int col = 0; col < headers.Length; col++)
        {
            var cell = worksheet.Cell(row, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = colorPalette.Primary;
            cell.Style.Font.FontColor = XLColor.White;
        }
        row++;

        // Data rows
        foreach (var table in bundleManifest.Tables)
        {
            var dataRow = new object[]
            {
                table.TableName,
                table.DataQuality.QualityScore + "/100",
                table.DataQuality.NullValueCount.ToString("N0"),
                table.DataQuality.DuplicateRecordCount.ToString("N0"),
                table.DataQuality.DataIssues.Count,
                table.Statistics.RecordCount.ToString("N0")
            };

            for (int col = 0; col < dataRow.Length; col++)
            {
                var cell = worksheet.Cell(row, col + 1);
                cell.SetValue(dataRow[col]?.ToString() ?? "");
                
                // Apply conditional formatting for quality scores
                if (col == 1 && options.EnableConditionalFormatting)
                {
                    var score = table.DataQuality.QualityScore;
                    cell.Style.Fill.BackgroundColor = score >= 80 ? colorPalette.Success :
                                                     score >= 60 ? colorPalette.Warning : colorPalette.Error;
                }
            }
            row++;
        }

        // Auto-fit columns
        worksheet.Columns().AdjustToContents();

        return new IndexSheetInfo
        {
            Name = "Data Quality",
            Type = IndexSheetType.DataQuality,
            DataRowCount = row - 1,
            ColumnCount = headers.Length,
            HasConditionalFormatting = options.EnableConditionalFormatting,
            Description = "Data quality assessment for all exported tables"
        };
    }

    private static async Task<IReadOnlyList<IndexSheetInfo>> CreateDataPreviewSheetsAsync(
        XLWorkbook workbook,
        BundleManifest bundleManifest,
        ExcelColorPalette colorPalette,
        ExcelIndexOptions options,
        CancellationToken cancellationToken)
    {
        await Task.Yield(); // Make async
        
        // For demonstration, create a simple preview summary
        // In a full implementation, this would read actual data samples from the exported files
        var worksheet = workbook.Worksheets.Add("Data Preview");
        var row = 1;

        worksheet.Cell(row, 1).Value = "Data Preview Summary";
        worksheet.Cell(row, 1).Style.Font.FontSize = 14;
        worksheet.Cell(row, 1).Style.Font.Bold = true;
        row += 2;

        worksheet.Cell(row, 1).Value = "Note: This is a preview summary. Access individual data files for complete data.";
        worksheet.Cell(row, 1).Style.Font.Italic = true;
        row += 2;

        // Sample table information
        foreach (var table in bundleManifest.Tables.Take(5)) // Limit to first 5 tables
        {
            worksheet.Cell(row, 1).Value = $"Table: {table.TableName}";
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            row++;
            
            worksheet.Cell(row, 1).Value = $"Columns: {string.Join(", ", table.Schema.Columns.Take(10).Select(c => c.Name))}";
            row++;
            
            worksheet.Cell(row, 1).Value = $"Record Count: {table.Statistics.RecordCount:N0}";
            row += 2;
        }

        worksheet.Columns().AdjustToContents();

        return new List<IndexSheetInfo>
        {
            new()
            {
                Name = "Data Preview",
                Type = IndexSheetType.DataPreview,
                DataRowCount = row - 1,
                ColumnCount = 1,
                Description = "Sample data preview for exported tables"
            }
        }.AsReadOnly();
    }

    private static async Task<IndexSheetInfo> CreatePerformanceMetricsSheetAsync(
        XLWorkbook workbook,
        BundleManifest bundleManifest,
        ExcelColorPalette colorPalette,
        ExcelIndexOptions options,
        CancellationToken cancellationToken)
    {
        await Task.Yield(); // Make async
        
        var worksheet = workbook.Worksheets.Add("Performance");
        var row = 1;

        // Title
        worksheet.Cell(row, 1).Value = "Export Performance Metrics";
        worksheet.Cell(row, 1).Style.Font.FontSize = 14;
        worksheet.Cell(row, 1).Style.Font.Bold = true;
        row += 2;

        // Overall metrics
        var overallMetrics = new[]
        {
            ("Total Export Duration", bundleManifest.Statistics.ExportDuration.ToString(@"hh\:mm\:ss")),
            ("Total Records Processed", bundleManifest.Statistics.TotalRecordCount.ToString("N0")),
            ("Total Data Size", FormatBytes(bundleManifest.Statistics.TotalFileSizeBytes)),
            ("Average Processing Rate", $"{bundleManifest.Statistics.TotalRecordCount / Math.Max(bundleManifest.Statistics.ExportDuration.TotalSeconds, 1):N0} records/sec")
        };

        foreach (var (label, value) in overallMetrics)
        {
            worksheet.Cell(row, 1).Value = label;
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            worksheet.Cell(row, 2).Value = value;
            row++;
        }

        row += 2;

        // Per-table performance
        worksheet.Cell(row, 1).Value = "Per-Table Performance";
        worksheet.Cell(row, 1).Style.Font.Bold = true;
        row++;

        var headers = new[] { "Table Name", "Records", "Export Time", "Rate (rec/sec)", "Size" };
        for (int col = 0; col < headers.Length; col++)
        {
            var cell = worksheet.Cell(row, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = colorPalette.Secondary;
            cell.Style.Font.FontColor = XLColor.White;
        }
        row++;

        foreach (var table in bundleManifest.Tables)
        {
            var rate = table.Statistics.RecordCount / Math.Max(table.Statistics.ExportTime.TotalSeconds, 1);
            var dataRow = new object[]
            {
                table.TableName,
                table.Statistics.RecordCount.ToString("N0"),
                table.Statistics.ExportTime.ToString(@"hh\:mm\:ss"),
                rate.ToString("N0"),
                FormatBytes(table.Statistics.SizeBytes)
            };

            for (int col = 0; col < dataRow.Length; col++)
            {
                worksheet.Cell(row, col + 1).SetValue(dataRow[col]?.ToString() ?? "");
            }
            row++;
        }

        worksheet.Columns().AdjustToContents();

        return new IndexSheetInfo
        {
            Name = "Performance",
            Type = IndexSheetType.PerformanceMetrics,
            DataRowCount = row - 1,
            ColumnCount = headers.Length,
            Description = "Export performance metrics and timing information"
        };
    }

    private static async Task<IndexSheetInfo> CreateTransformationLogSheetAsync(
        XLWorkbook workbook,
        BundleManifest bundleManifest,
        ExcelColorPalette colorPalette,
        ExcelIndexOptions options,
        CancellationToken cancellationToken)
    {
        await Task.Yield(); // Make async
        
        var worksheet = workbook.Worksheets.Add("Transformations");
        var row = 1;

        worksheet.Cell(row, 1).Value = "Transformation Summary";
        worksheet.Cell(row, 1).Style.Font.FontSize = 14;
        worksheet.Cell(row, 1).Style.Font.Bold = true;
        row += 2;

        if (bundleManifest.Transformations != null)
        {
            var summary = new[]
            {
                ("Total Transformations Applied", bundleManifest.Transformations.TransformationsApplied.ToString("N0")),
                ("Transformer Types Used", string.Join(", ", bundleManifest.Transformations.TransformerTypes)),
                ("Tables with Transformations", bundleManifest.Transformations.TransformationsByTable.Count.ToString("N0"))
            };

            foreach (var (label, value) in summary)
            {
                worksheet.Cell(row, 1).Value = label;
                worksheet.Cell(row, 1).Style.Font.Bold = true;
                worksheet.Cell(row, 2).Value = value;
                row++;
            }

            row += 2;

            // Per-table transformation details
            worksheet.Cell(row, 1).Value = "Transformations by Table";
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            row++;

            var headers = new[] { "Table Name", "Transformations Applied" };
            for (int col = 0; col < headers.Length; col++)
            {
                var cell = worksheet.Cell(row, col + 1);
                cell.Value = headers[col];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = colorPalette.Accent;
                cell.Style.Font.FontColor = XLColor.White;
            }
            row++;

            foreach (var (tableName, count) in bundleManifest.Transformations.TransformationsByTable)
            {
                worksheet.Cell(row, 1).Value = tableName;
                worksheet.Cell(row, 2).Value = count.ToString("N0");
                row++;
            }
        }
        else
        {
            worksheet.Cell(row, 1).Value = "No transformations were applied during this export.";
            row++;
        }

        worksheet.Columns().AdjustToContents();

        return new IndexSheetInfo
        {
            Name = "Transformations",
            Type = IndexSheetType.TransformationLog,
            DataRowCount = row - 1,
            ColumnCount = 2,
            Description = "Log of all data transformations applied during export"
        };
    }

    private static async Task ApplyAdvancedFormattingAsync(
        XLWorkbook workbook,
        IReadOnlyList<IndexSheetInfo> sheets,
        ExcelColorPalette colorPalette,
        ExcelIndexOptions options,
        CancellationToken cancellationToken)
    {
        await Task.Yield(); // Make async
        
        foreach (var worksheet in workbook.Worksheets)
        {
            // Apply freeze panes to header rows
            if (worksheet.Name != "Dashboard")
            {
                worksheet.SheetView.FreezeRows(1);
            }

            // Auto-filter on data tables
            if (worksheet.Name.Contains("Catalog") || worksheet.Name.Contains("Map") || worksheet.Name.Contains("Quality"))
            {
                var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
                var lastCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 1;
                
                if (lastRow > 1)
                {
                    worksheet.Range(1, 1, lastRow, lastCol).SetAutoFilter();
                }
            }
        }
    }

    private static List<string> GetExpectedSheetNames(BundleManifest bundleManifest)
    {
        var expectedSheets = new List<string> { "Dashboard" };
        
        if (bundleManifest.Tables.Any())
        {
            expectedSheets.Add("Table Catalog");
            expectedSheets.Add("Partition Map");
            expectedSheets.Add("Data Quality");
        }

        return expectedSheets;
    }

    private static HashSet<string> ExtractTableNamesFromCatalog(IXLWorksheet catalogSheet)
    {
        var tableNames = new HashSet<string>();
        var rows = catalogSheet.RowsUsed().Skip(1); // Skip header

        foreach (var row in rows)
        {
            var tableName = row.Cell(1).GetValue<string>();
            if (!string.IsNullOrWhiteSpace(tableName))
            {
                tableNames.Add(tableName);
            }
        }

        return tableNames;
    }

    private static string FormatBytes(long bytes)
    {
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var size = (double)bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:F1} {units[unitIndex]}";
    }

    #endregion

    #region Helper Classes

    private sealed record ExcelColorPalette
    {
        public required XLColor Primary { get; init; }
        public required XLColor Secondary { get; init; }
        public required XLColor Accent { get; init; }
        public required XLColor Background { get; init; }
        public required XLColor Text { get; init; }
        public required XLColor Success { get; init; }
        public required XLColor Warning { get; init; }
        public required XLColor Error { get; init; }
    }

    private sealed record BundleAnalysis
    {
        public int TotalTables { get; init; }
        public int TotalPartitions { get; init; }
        public long TotalRecords { get; init; }
        public long TotalSizeBytes { get; init; }
        public TableManifest? LargestTable { get; init; }
        public List<string> PartitioningStrategies { get; init; } = new();
        public List<string> ExportFormats { get; init; } = new();
        public double AverageQualityScore { get; init; }
    }

    #endregion
}