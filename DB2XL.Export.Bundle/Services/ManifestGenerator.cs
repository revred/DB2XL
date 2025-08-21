using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DB2XL.Core.Models;
using DB2XL.Core.Services;

namespace DB2XL.Export.Bundle.Services;

/// <summary>
/// High-performance manifest generator that creates comprehensive documentation and metadata tracking
/// for bundle exports with full provenance tracking and validation capabilities.
/// </summary>
public sealed class ManifestGenerator : IManifestGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Generates a complete bundle manifest from export results and metadata.
    /// </summary>
    public async Task<BundleManifest> GenerateBundleManifestAsync(
        string bundleId,
        DatabaseMetadata sourceDatabase,
        IReadOnlyList<TableExportResult> exportResults,
        BundleExportConfiguration configuration,
        ManifestGenerationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId);
        ArgumentNullException.ThrowIfNull(sourceDatabase);
        ArgumentNullException.ThrowIfNull(exportResults);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        var manifestStartTime = DateTime.UtcNow;

        // Generate table manifests
        var tableManifests = new List<TableManifest>();
        foreach (var exportResult in exportResults)
        {
            var tableManifest = await GenerateTableManifestAsync(exportResult, options, cancellationToken);
            tableManifests.Add(tableManifest);
        }

        // Calculate bundle statistics
        var statistics = CalculateBundleStatistics(exportResults, manifestStartTime);

        // Generate data quality assessment
        var dataQuality = GenerateDataQualityAssessment(exportResults);

        // Generate transformation summary if applicable
        var transformations = GenerateTransformationSummary(exportResults);

        // Create source database info
        var sourceInfo = new SourceDatabaseInfo
        {
            FilePath = sourceDatabase.FilePath,
            FileSizeBytes = sourceDatabase.FileSizeBytes,
            LastModified = sourceDatabase.LastModified,
            SqliteVersion = sourceDatabase.SqliteVersion
        };

        // Create bundle manifest
        var manifest = new BundleManifest
        {
            BundleId = bundleId,
            ExportTimestamp = manifestStartTime,
            SourceDatabase = sourceInfo,
            Tables = tableManifests.AsReadOnly(),
            Configuration = configuration,
            Statistics = statistics,
            DataQuality = dataQuality,
            Transformations = transformations
        };

        return manifest;
    }

    /// <summary>
    /// Generates manifest files in multiple formats.
    /// </summary>
    public async Task<ManifestFileResult> WriteManifestFilesAsync(
        BundleManifest manifest,
        string outputDirectory,
        ManifestGenerationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(options);

        var startTime = DateTime.UtcNow;
        var warnings = new List<string>();
        var generatedFiles = new List<GeneratedFile>();

        try
        {
            // Ensure output directory exists
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            // Generate machine-readable formats
            if (options.OutputFormats.HasFlag(ManifestOutputFormats.Json))
            {
                var jsonFile = await WriteJsonManifestAsync(manifest, outputDirectory, cancellationToken);
                generatedFiles.Add(jsonFile);
            }

            if (options.OutputFormats.HasFlag(ManifestOutputFormats.Yaml))
            {
                var yamlFile = await WriteYamlManifestAsync(manifest, outputDirectory, cancellationToken);
                generatedFiles.Add(yamlFile);
            }

            if (options.OutputFormats.HasFlag(ManifestOutputFormats.Xml))
            {
                var xmlFile = await WriteXmlManifestAsync(manifest, outputDirectory, cancellationToken);
                generatedFiles.Add(xmlFile);
            }

            // Generate human-readable formats
            if (options.OutputFormats.HasFlag(ManifestOutputFormats.Markdown))
            {
                var markdownFile = await WriteMarkdownManifestAsync(manifest, outputDirectory, options, cancellationToken);
                generatedFiles.Add(markdownFile);
            }

            if (options.OutputFormats.HasFlag(ManifestOutputFormats.Html))
            {
                var htmlFile = await WriteHtmlManifestAsync(manifest, outputDirectory, options, cancellationToken);
                generatedFiles.Add(htmlFile);
            }

            // Generate additional documentation if requested
            if (options.GenerateDocumentation)
            {
                var docFiles = await GenerateDocumentationAsync(manifest, outputDirectory, options, cancellationToken);
                generatedFiles.AddRange(docFiles);
            }

            var endTime = DateTime.UtcNow;
            var totalSize = generatedFiles.Sum(f => f.SizeBytes);

            return new ManifestFileResult
            {
                Files = generatedFiles.AsReadOnly(),
                TotalSizeBytes = totalSize,
                GenerationStartTime = startTime,
                GenerationEndTime = endTime,
                Warnings = warnings.AsReadOnly(),
                IsSuccessful = true
            };
        }
        catch (Exception ex)
        {
            return new ManifestFileResult
            {
                Files = generatedFiles.AsReadOnly(),
                GenerationStartTime = startTime,
                GenerationEndTime = DateTime.UtcNow,
                Warnings = warnings.AsReadOnly(),
                IsSuccessful = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Validates a manifest against actual exported files.
    /// </summary>
    public async Task<ManifestValidationResult> ValidateManifestAsync(
        BundleManifest manifest,
        string bundleRootPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleRootPath);

        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var warnings = new List<string>();
        var missingFiles = new List<string>();
        var orphanedFiles = new List<string>();
        var checksumResults = new List<ChecksumValidationResult>();

        try
        {
            var referencedFiles = new HashSet<string>();
            var filesValidated = 0;
            long totalSizeValidated = 0;

            // Validate each table's files
            foreach (var table in manifest.Tables)
            {
                foreach (var export in table.Exports)
                {
                    foreach (var filePath in export.FilePaths)
                    {
                        var fullPath = Path.IsPathFullyQualified(filePath) 
                            ? filePath 
                            : Path.Combine(bundleRootPath, filePath);

                        referencedFiles.Add(fullPath);

                        if (!File.Exists(fullPath))
                        {
                            missingFiles.Add(filePath);
                            continue;
                        }

                        var fileInfo = new FileInfo(fullPath);
                        totalSizeValidated += fileInfo.Length;
                        filesValidated++;

                        // Validate checksum if available
                        if (export.Metadata.TryGetValue("checksum", out var checksumObj) && 
                            checksumObj is string expectedChecksum)
                        {
                            var actualChecksum = await CalculateFileChecksumAsync(fullPath, cancellationToken);
                            var checksumResult = new ChecksumValidationResult
                            {
                                FilePath = filePath,
                                ExpectedChecksum = expectedChecksum,
                                ActualChecksum = actualChecksum,
                                IsValid = string.Equals(expectedChecksum, actualChecksum, StringComparison.OrdinalIgnoreCase)
                            };

                            if (!checksumResult.IsValid)
                            {
                                checksumResult = checksumResult with 
                                { 
                                    ErrorMessage = "Checksum mismatch - file may have been modified" 
                                };
                            }

                            checksumResults.Add(checksumResult);
                        }
                    }
                }
            }

            // Find orphaned files (files in bundle directory not referenced in manifest)
            if (Directory.Exists(bundleRootPath))
            {
                var allFiles = Directory.GetFiles(bundleRootPath, "*", SearchOption.AllDirectories);
                foreach (var file in allFiles)
                {
                    if (!referencedFiles.Contains(file) && !IsManifestFile(file))
                    {
                        orphanedFiles.Add(Path.GetRelativePath(bundleRootPath, file));
                    }
                }
            }

            stopwatch.Stop();

            var metrics = new ManifestValidationMetrics
            {
                FilesValidated = filesValidated,
                ChecksumsVerified = checksumResults.Count,
                ValidationTime = stopwatch.Elapsed,
                TotalSizeValidated = totalSizeValidated
            };

            var isValid = !errors.Any() && !missingFiles.Any() && 
                         checksumResults.All(r => r.IsValid);

            return new ManifestValidationResult
            {
                IsValid = isValid,
                Errors = errors.AsReadOnly(),
                Warnings = warnings.AsReadOnly(),
                MissingFiles = missingFiles.AsReadOnly(),
                OrphanedFiles = orphanedFiles.AsReadOnly(),
                ChecksumResults = checksumResults.AsReadOnly(),
                Metrics = metrics
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Validation failed: {ex.Message}");
            return new ManifestValidationResult
            {
                IsValid = false,
                Errors = errors.AsReadOnly(),
                Metrics = new ManifestValidationMetrics { ValidationTime = stopwatch.Elapsed }
            };
        }
    }

    /// <summary>
    /// Merges multiple manifests into a consolidated view.
    /// </summary>
    public async Task<BundleManifest> MergeManifestsAsync(
        IReadOnlyList<BundleManifest> manifests,
        ManifestMergeOptions mergeOptions,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Make async for consistency

        ArgumentNullException.ThrowIfNull(manifests);
        ArgumentNullException.ThrowIfNull(mergeOptions);

        if (!manifests.Any())
        {
            throw new ArgumentException("At least one manifest is required for merging", nameof(manifests));
        }

        var mergedBundleId = $"{mergeOptions.MergedBundlePrefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        var mergedTables = new Dictionary<string, TableManifest>();

        // Merge tables from all manifests
        foreach (var manifest in manifests)
        {
            foreach (var table in manifest.Tables)
            {
                if (mergedTables.ContainsKey(table.TableName))
                {
                    // Handle conflicts based on strategy
                    switch (mergeOptions.ConflictResolution)
                    {
                        case ConflictResolutionStrategy.LatestWins:
                            if (manifest.ExportTimestamp > mergedTables[table.TableName].Statistics.LastUpdated)
                            {
                                mergedTables[table.TableName] = table;
                            }
                            break;
                        case ConflictResolutionStrategy.EarliestWins:
                            // Keep existing (first encountered)
                            break;
                        case ConflictResolutionStrategy.FailOnConflict:
                            throw new InvalidOperationException($"Conflict detected for table {table.TableName}");
                        case ConflictResolutionStrategy.Merge:
                            // Implement merge logic here
                            mergedTables[table.TableName] = MergeTables(mergedTables[table.TableName], table);
                            break;
                    }
                }
                else
                {
                    mergedTables[table.TableName] = table;
                }
            }
        }

        // Calculate merged statistics
        var totalRecords = mergedTables.Values.Sum(t => t.Statistics.RecordCount);
        var totalSize = mergedTables.Values.Sum(t => t.Statistics.SizeBytes);
        var totalDuration = manifests.Aggregate(TimeSpan.Zero, (acc, m) => acc + m.Statistics.ExportDuration);

        var mergedStatistics = new BundleStatistics
        {
            TableCount = mergedTables.Count,
            TotalRecordCount = totalRecords,
            TotalFileSizeBytes = totalSize,
            ExportDuration = totalDuration
        };

        // Use the latest manifest as the base for other properties
        var latestManifest = manifests.OrderByDescending(m => m.ExportTimestamp).First();

        return new BundleManifest
        {
            BundleId = mergedBundleId,
            ExportTimestamp = DateTime.UtcNow,
            SourceDatabase = latestManifest.SourceDatabase,
            Tables = mergedTables.Values.ToList().AsReadOnly(),
            Configuration = latestManifest.Configuration,
            Statistics = mergedStatistics,
            DataQuality = latestManifest.DataQuality,
            Transformations = latestManifest.Transformations
        };
    }

    /// <summary>
    /// Generates a comparison report between two manifests.
    /// </summary>
    public async Task<ManifestComparisonReport> CompareManifestsAsync(
        BundleManifest baselineManifest,
        BundleManifest currentManifest,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Make async for consistency

        ArgumentNullException.ThrowIfNull(baselineManifest);
        ArgumentNullException.ThrowIfNull(currentManifest);

        var baselineTables = baselineManifest.Tables.ToDictionary(t => t.TableName);
        var currentTables = currentManifest.Tables.ToDictionary(t => t.TableName);

        // Find added, removed, and modified tables
        var addedTables = currentTables.Keys.Except(baselineTables.Keys).ToList();
        var removedTables = baselineTables.Keys.Except(currentTables.Keys).ToList();
        var commonTables = baselineTables.Keys.Intersect(currentTables.Keys).ToList();

        var modifiedTables = new List<TableModification>();
        var schemaDifferences = new List<SchemaDifference>();
        var qualityChanges = new List<QualityChange>();

        // Analyze common tables for modifications
        foreach (var tableName in commonTables)
        {
            var baselineTable = baselineTables[tableName];
            var currentTable = currentTables[tableName];

            var modifications = CompareTableMetadata(baselineTable, currentTable);
            if (modifications.Any())
            {
                modifiedTables.Add(new TableModification
                {
                    TableName = tableName,
                    ModificationType = ModificationType.Modified,
                    Changes = modifications,
                    ImpactAssessment = AssessImpact(modifications)
                });
            }

            // Compare schemas
            var schemaDiffs = CompareSchemas(baselineTable.Schema, currentTable.Schema, tableName);
            schemaDifferences.AddRange(schemaDiffs);

            // Compare quality metrics
            var qualityChange = CompareDataQuality(baselineTable.DataQuality, currentTable.DataQuality, tableName);
            if (qualityChange != null)
            {
                qualityChanges.Add(qualityChange);
            }
        }

        // Generate summary
        var summary = new ComparisonSummary
        {
            Baseline = new ManifestInfo
            {
                BundleId = baselineManifest.BundleId,
                ExportTimestamp = baselineManifest.ExportTimestamp,
                TableCount = baselineManifest.Tables.Count,
                TotalRecords = baselineManifest.Statistics.TotalRecordCount,
                TotalSize = baselineManifest.Statistics.TotalFileSizeBytes
            },
            Current = new ManifestInfo
            {
                BundleId = currentManifest.BundleId,
                ExportTimestamp = currentManifest.ExportTimestamp,
                TableCount = currentManifest.Tables.Count,
                TotalRecords = currentManifest.Statistics.TotalRecordCount,
                TotalSize = currentManifest.Statistics.TotalFileSizeBytes
            },
            SimilarityScore = CalculateSimilarityScore(addedTables.Count, removedTables.Count, modifiedTables.Count, baselineTables.Count),
            ChangeCount = addedTables.Count + removedTables.Count + modifiedTables.Count,
            ComparisonTime = DateTime.UtcNow
        };

        // Generate performance comparison
        var performanceComparison = new PerformanceComparison
        {
            ExportDurationDelta = currentManifest.Statistics.ExportDuration - baselineManifest.Statistics.ExportDuration,
            ProcessingRateDelta = CalculateProcessingRateDelta(baselineManifest, currentManifest),
            FileSizeDelta = currentManifest.Statistics.TotalFileSizeBytes - baselineManifest.Statistics.TotalFileSizeBytes,
            Summary = GeneratePerformanceSummary(baselineManifest, currentManifest)
        };

        // Generate detailed markdown report
        var detailedReport = GenerateDetailedComparisonReport(summary, addedTables, removedTables, modifiedTables, schemaDifferences, qualityChanges, performanceComparison);

        return new ManifestComparisonReport
        {
            Summary = summary,
            AddedTables = addedTables.AsReadOnly(),
            RemovedTables = removedTables.AsReadOnly(),
            ModifiedTables = modifiedTables.AsReadOnly(),
            SchemaDifferences = schemaDifferences.AsReadOnly(),
            QualityChanges = qualityChanges.AsReadOnly(),
            PerformanceComparison = performanceComparison,
            DetailedReport = detailedReport
        };
    }

    #region Private Helper Methods

    private async Task<TableManifest> GenerateTableManifestAsync(
        TableExportResult exportResult,
        ManifestGenerationOptions options,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Make async for consistency

        var exports = new List<TableExportInfo>();
        foreach (var format in exportResult.Formats)
        {
            var exportInfo = new TableExportInfo
            {
                Format = format.Format,
                FilePaths = format.Files.Select(f => f.FilePath).ToList().AsReadOnly(),
                TotalSizeBytes = format.TotalSizeBytes,
                Metadata = format.Metadata
            };
            exports.Add(exportInfo);
        }

        return new TableManifest
        {
            TableName = exportResult.TableName,
            Schema = ConvertSchemaMetadata(exportResult.Schema),
            Exports = exports.AsReadOnly(),
            Partitioning = ConvertPartitioningMetadata(exportResult.Partitioning),
            Statistics = ConvertTableStatistics(exportResult.Statistics),
            DataQuality = ConvertDataQuality(exportResult.DataQuality)
        };
    }

    private BundleStatistics CalculateBundleStatistics(IReadOnlyList<TableExportResult> exportResults, DateTime startTime)
    {
        var totalRecords = exportResults.Sum(r => r.Statistics.RecordCount);
        var totalSize = exportResults.Sum(r => r.Formats.Sum(f => f.TotalSizeBytes));
        var totalDuration = exportResults.Aggregate(TimeSpan.Zero, (acc, r) => acc + r.Performance.ExportDuration);

        return new BundleStatistics
        {
            TableCount = exportResults.Count,
            TotalRecordCount = totalRecords,
            TotalFileSizeBytes = totalSize,
            ExportDuration = totalDuration
        };
    }

    private DataQualityAssessment GenerateDataQualityAssessment(IReadOnlyList<TableExportResult> exportResults)
    {
        var averageScore = exportResults.Average(r => r.DataQuality.QualityScore);
        var allIssues = exportResults.SelectMany(r => r.DataQuality.Issues).ToList();

        return new DataQualityAssessment
        {
            OverallScore = (int)Math.Round(averageScore),
            Issues = allIssues.AsReadOnly(),
            Recommendations = GenerateQualityRecommendations(allIssues).AsReadOnly()
        };
    }

    private TransformationSummary? GenerateTransformationSummary(IReadOnlyList<TableExportResult> exportResults)
    {
        var transformationResults = exportResults.Where(r => r.Transformations != null).ToList();
        if (!transformationResults.Any())
        {
            return null;
        }

        var totalTransformations = transformationResults.Sum(r => r.Transformations!.TransformationCount);
        var allTransformerTypes = transformationResults
            .SelectMany(r => r.Transformations!.TransformerTypes)
            .Distinct()
            .ToList();

        var transformationsByTable = transformationResults.ToDictionary(
            r => r.TableName,
            r => r.Transformations!.TransformationCount
        );

        return new TransformationSummary
        {
            TransformationsApplied = totalTransformations,
            TransformerTypes = allTransformerTypes.AsReadOnly(),
            TransformationsByTable = transformationsByTable.AsReadOnly()
        };
    }

    private async Task<GeneratedFile> WriteJsonManifestAsync(BundleManifest manifest, string outputDirectory, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(outputDirectory, "bundle-manifest.json");
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        var checksum = await CalculateFileChecksumAsync(filePath, cancellationToken);
        var fileInfo = new FileInfo(filePath);

        return new GeneratedFile
        {
            FilePath = filePath,
            Format = ManifestFormat.Json,
            SizeBytes = fileInfo.Length,
            Checksum = checksum,
            Description = "Machine-readable bundle manifest in JSON format",
            IsMachineReadable = true,
            Audience = FileAudience.Machine
        };
    }

    private async Task<GeneratedFile> WriteYamlManifestAsync(BundleManifest manifest, string outputDirectory, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(outputDirectory, "bundle-manifest.yaml");
        
        // For simplicity, convert JSON to YAML-like format
        // In production, use a proper YAML library like YamlDotNet
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        var yamlContent = ConvertJsonToYamlLike(json);
        
        await File.WriteAllTextAsync(filePath, yamlContent, cancellationToken);

        var checksum = await CalculateFileChecksumAsync(filePath, cancellationToken);
        var fileInfo = new FileInfo(filePath);

        return new GeneratedFile
        {
            FilePath = filePath,
            Format = ManifestFormat.Yaml,
            SizeBytes = fileInfo.Length,
            Checksum = checksum,
            Description = "Human-readable bundle manifest in YAML format",
            IsMachineReadable = true,
            Audience = FileAudience.Both
        };
    }

    private async Task<GeneratedFile> WriteXmlManifestAsync(BundleManifest manifest, string outputDirectory, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(outputDirectory, "bundle-manifest.xml");
        
        // Simple XML generation - in production, use proper XML serialization
        var xmlContent = GenerateXmlManifest(manifest);
        await File.WriteAllTextAsync(filePath, xmlContent, cancellationToken);

        var checksum = await CalculateFileChecksumAsync(filePath, cancellationToken);
        var fileInfo = new FileInfo(filePath);

        return new GeneratedFile
        {
            FilePath = filePath,
            Format = ManifestFormat.Xml,
            SizeBytes = fileInfo.Length,
            Checksum = checksum,
            Description = "XML-formatted bundle manifest",
            IsMachineReadable = true,
            Audience = FileAudience.Machine
        };
    }

    private async Task<GeneratedFile> WriteMarkdownManifestAsync(BundleManifest manifest, string outputDirectory, ManifestGenerationOptions options, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(outputDirectory, "README.md");
        var markdownContent = GenerateMarkdownDocumentation(manifest, options);
        await File.WriteAllTextAsync(filePath, markdownContent, cancellationToken);

        var checksum = await CalculateFileChecksumAsync(filePath, cancellationToken);
        var fileInfo = new FileInfo(filePath);

        return new GeneratedFile
        {
            FilePath = filePath,
            Format = ManifestFormat.Markdown,
            SizeBytes = fileInfo.Length,
            Checksum = checksum,
            Description = "Human-readable documentation in Markdown format",
            IsMachineReadable = false,
            Audience = FileAudience.Human
        };
    }

    private async Task<GeneratedFile> WriteHtmlManifestAsync(BundleManifest manifest, string outputDirectory, ManifestGenerationOptions options, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(outputDirectory, "bundle-manifest.html");
        var htmlContent = GenerateHtmlDocumentation(manifest, options);
        await File.WriteAllTextAsync(filePath, htmlContent, cancellationToken);

        var checksum = await CalculateFileChecksumAsync(filePath, cancellationToken);
        var fileInfo = new FileInfo(filePath);

        return new GeneratedFile
        {
            FilePath = filePath,
            Format = ManifestFormat.Html,
            SizeBytes = fileInfo.Length,
            Checksum = checksum,
            Description = "Interactive HTML documentation",
            IsMachineReadable = false,
            Audience = FileAudience.Human
        };
    }

    private async Task<IReadOnlyList<GeneratedFile>> GenerateDocumentationAsync(BundleManifest manifest, string outputDirectory, ManifestGenerationOptions options, CancellationToken cancellationToken)
    {
        var files = new List<GeneratedFile>();

        // Generate table-specific documentation
        foreach (var table in manifest.Tables)
        {
            var tableDocPath = Path.Combine(outputDirectory, "tables", $"{table.TableName}.md");
            var tableDir = Path.GetDirectoryName(tableDocPath);
            if (!Directory.Exists(tableDir))
            {
                Directory.CreateDirectory(tableDir!);
            }

            var tableDoc = GenerateTableDocumentation(table, options);
            await File.WriteAllTextAsync(tableDocPath, tableDoc, cancellationToken);

            var checksum = await CalculateFileChecksumAsync(tableDocPath, cancellationToken);
            var fileInfo = new FileInfo(tableDocPath);

            files.Add(new GeneratedFile
            {
                FilePath = tableDocPath,
                Format = ManifestFormat.Markdown,
                SizeBytes = fileInfo.Length,
                Checksum = checksum,
                Description = $"Documentation for table {table.TableName}",
                IsMachineReadable = false,
                Audience = FileAudience.Human
            });
        }

        return files.AsReadOnly();
    }

    private static async Task<string> CalculateFileChecksumAsync(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes);
    }

    private static bool IsManifestFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath).ToLowerInvariant();
        return fileName.Contains("manifest") || fileName == "readme.md" || 
               fileName.EndsWith(".yaml") || fileName.EndsWith(".yml") || 
               fileName.EndsWith(".json") || fileName.EndsWith(".xml");
    }

    private static TableManifest MergeTables(TableManifest existing, TableManifest incoming)
    {
        // Simple merge strategy - in production, implement more sophisticated merging
        return incoming.Statistics.LastUpdated > existing.Statistics.LastUpdated ? incoming : existing;
    }

    private static List<string> CompareTableMetadata(TableManifest baseline, TableManifest current)
    {
        var changes = new List<string>();

        if (baseline.Statistics.RecordCount != current.Statistics.RecordCount)
        {
            changes.Add($"Record count changed from {baseline.Statistics.RecordCount:N0} to {current.Statistics.RecordCount:N0}");
        }

        if (baseline.Schema.Columns.Count != current.Schema.Columns.Count)
        {
            changes.Add($"Column count changed from {baseline.Schema.Columns.Count} to {current.Schema.Columns.Count}");
        }

        return changes;
    }

    private static List<SchemaDifference> CompareSchemas(TableSchemaInfo baseline, TableSchemaInfo current, string tableName)
    {
        var differences = new List<SchemaDifference>();

        var baselineColumns = baseline.Columns.ToDictionary(c => c.Name);
        var currentColumns = current.Columns.ToDictionary(c => c.Name);

        // Find added columns
        foreach (var columnName in currentColumns.Keys.Except(baselineColumns.Keys))
        {
            differences.Add(new SchemaDifference
            {
                TableName = tableName,
                ColumnName = columnName,
                DifferenceType = DifferenceType.ColumnAdded,
                CurrentValue = currentColumns[columnName].DeclaredType,
                Description = $"Column '{columnName}' was added with type '{currentColumns[columnName].DeclaredType}'"
            });
        }

        // Find removed columns
        foreach (var columnName in baselineColumns.Keys.Except(currentColumns.Keys))
        {
            differences.Add(new SchemaDifference
            {
                TableName = tableName,
                ColumnName = columnName,
                DifferenceType = DifferenceType.ColumnRemoved,
                BaselineValue = baselineColumns[columnName].DeclaredType,
                Description = $"Column '{columnName}' was removed (was type '{baselineColumns[columnName].DeclaredType}')"
            });
        }

        return differences;
    }

    private static QualityChange? CompareDataQuality(TableDataQuality baseline, TableDataQuality current, string tableName)
    {
        if (baseline.QualityScore == current.QualityScore)
        {
            return null;
        }

        var direction = current.QualityScore > baseline.QualityScore ? ChangeDirection.Improved : ChangeDirection.Degraded;
        var scoreDiff = Math.Abs(current.QualityScore - baseline.QualityScore);
        var severity = scoreDiff >= 20 ? ChangeSeverity.High : scoreDiff >= 10 ? ChangeSeverity.Medium : ChangeSeverity.Low;

        return new QualityChange
        {
            TableName = tableName,
            Metric = "Quality Score",
            PreviousValue = baseline.QualityScore,
            CurrentValue = current.QualityScore,
            Direction = direction,
            Severity = severity
        };
    }

    private static double CalculateSimilarityScore(int added, int removed, int modified, int totalBaseline)
    {
        if (totalBaseline == 0) return 100.0;
        
        var unchanged = totalBaseline - removed - modified;
        return Math.Max(0, (double)unchanged / totalBaseline * 100);
    }

    private static double CalculateProcessingRateDelta(BundleManifest baseline, BundleManifest current)
    {
        var baselineRate = baseline.Statistics.TotalRecordCount / Math.Max(baseline.Statistics.ExportDuration.TotalSeconds, 1);
        var currentRate = current.Statistics.TotalRecordCount / Math.Max(current.Statistics.ExportDuration.TotalSeconds, 1);
        return currentRate - baselineRate;
    }

    private static string GeneratePerformanceSummary(BundleManifest baseline, BundleManifest current)
    {
        var durationChange = current.Statistics.ExportDuration - baseline.Statistics.ExportDuration;
        var sizeChange = current.Statistics.TotalFileSizeBytes - baseline.Statistics.TotalFileSizeBytes;
        
        var durationText = durationChange.TotalSeconds > 0 ? $"slower by {durationChange:mm\\:ss}" : $"faster by {durationChange.Negate():mm\\:ss}";
        var sizeText = sizeChange > 0 ? $"larger by {FormatBytes(sizeChange)}" : $"smaller by {FormatBytes(-sizeChange)}";
        
        return $"Export was {durationText} and {sizeText} compared to baseline";
    }

    private static string GenerateDetailedComparisonReport(
        ComparisonSummary summary,
        List<string> addedTables,
        List<string> removedTables,
        List<TableModification> modifiedTables,
        List<SchemaDifference> schemaDifferences,
        List<QualityChange> qualityChanges,
        PerformanceComparison performanceComparison)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("# Bundle Manifest Comparison Report");
        sb.AppendLine();
        sb.AppendLine($"**Comparison Date:** {summary.ComparisonTime:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"**Similarity Score:** {summary.SimilarityScore:F1}%");
        sb.AppendLine($"**Total Changes:** {summary.ChangeCount}");
        sb.AppendLine();
        
        sb.AppendLine("## Summary");
        sb.AppendLine($"- **Baseline:** {summary.Baseline.BundleId} ({summary.Baseline.ExportTimestamp:yyyy-MM-dd})");
        sb.AppendLine($"- **Current:** {summary.Current.BundleId} ({summary.Current.ExportTimestamp:yyyy-MM-dd})");
        sb.AppendLine();
        
        if (addedTables.Any())
        {
            sb.AppendLine("## Added Tables");
            foreach (var table in addedTables)
            {
                sb.AppendLine($"- {table}");
            }
            sb.AppendLine();
        }
        
        if (removedTables.Any())
        {
            sb.AppendLine("## Removed Tables");
            foreach (var table in removedTables)
            {
                sb.AppendLine($"- {table}");
            }
            sb.AppendLine();
        }
        
        if (modifiedTables.Any())
        {
            sb.AppendLine("## Modified Tables");
            foreach (var table in modifiedTables)
            {
                sb.AppendLine($"### {table.TableName}");
                foreach (var change in table.Changes)
                {
                    sb.AppendLine($"- {change}");
                }
                sb.AppendLine();
            }
        }
        
        sb.AppendLine("## Performance Comparison");
        sb.AppendLine(performanceComparison.Summary);
        
        return sb.ToString();
    }

    private static string ConvertJsonToYamlLike(string json)
    {
        // Simple JSON to YAML-like conversion for demonstration
        // In production, use a proper YAML library
        return json.Replace("{", "").Replace("}", "").Replace("[", "").Replace("]", "")
                   .Replace("\"", "").Replace(",", "");
    }

    private static string GenerateXmlManifest(BundleManifest manifest)
    {
        // Simple XML generation for demonstration
        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<BundleManifest>
    <BundleId>{manifest.BundleId}</BundleId>
    <ExportTimestamp>{manifest.ExportTimestamp:O}</ExportTimestamp>
    <TableCount>{manifest.Tables.Count}</TableCount>
    <TotalRecords>{manifest.Statistics.TotalRecordCount}</TotalRecords>
</BundleManifest>";
    }

    private static string GenerateMarkdownDocumentation(BundleManifest manifest, ManifestGenerationOptions options)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"# Bundle Export: {manifest.BundleId}");
        sb.AppendLine();
        sb.AppendLine($"**Export Date:** {manifest.ExportTimestamp:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"**Source Database:** {manifest.SourceDatabase.FilePath}");
        sb.AppendLine($"**Total Tables:** {manifest.Tables.Count}");
        sb.AppendLine($"**Total Records:** {manifest.Statistics.TotalRecordCount:N0}");
        sb.AppendLine($"**Total Size:** {FormatBytes(manifest.Statistics.TotalFileSizeBytes)}");
        sb.AppendLine();
        
        sb.AppendLine("## Tables");
        foreach (var table in manifest.Tables)
        {
            sb.AppendLine($"### {table.TableName}");
            sb.AppendLine($"- **Records:** {table.Statistics.RecordCount:N0}");
            sb.AppendLine($"- **Columns:** {table.Schema.Columns.Count}");
            sb.AppendLine($"- **Quality Score:** {table.DataQuality.QualityScore}/100");
            sb.AppendLine();
        }
        
        return sb.ToString();
    }

    private static string GenerateHtmlDocumentation(BundleManifest manifest, ManifestGenerationOptions options)
    {
        return $@"<!DOCTYPE html>
<html>
<head>
    <title>Bundle Export: {manifest.BundleId}</title>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 40px; }}
        .header {{ background: #f5f5f5; padding: 20px; border-radius: 5px; }}
        .table {{ border-collapse: collapse; width: 100%; }}
        .table th, .table td {{ border: 1px solid #ddd; padding: 8px; text-align: left; }}
    </style>
</head>
<body>
    <div class=""header"">
        <h1>Bundle Export: {manifest.BundleId}</h1>
        <p><strong>Export Date:</strong> {manifest.ExportTimestamp:yyyy-MM-dd HH:mm:ss} UTC</p>
        <p><strong>Total Tables:</strong> {manifest.Tables.Count}</p>
        <p><strong>Total Records:</strong> {manifest.Statistics.TotalRecordCount:N0}</p>
    </div>
    <h2>Tables</h2>
    <table class=""table"">
        <tr><th>Table Name</th><th>Records</th><th>Columns</th><th>Quality Score</th></tr>
        {string.Join("", manifest.Tables.Select(t => $"<tr><td>{t.TableName}</td><td>{t.Statistics.RecordCount:N0}</td><td>{t.Schema.Columns.Count}</td><td>{t.DataQuality.QualityScore}/100</td></tr>"))}
    </table>
</body>
</html>";
    }

    private static string GenerateTableDocumentation(TableManifest table, ManifestGenerationOptions options)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine($"# Table: {table.TableName}");
        sb.AppendLine();
        sb.AppendLine($"**Record Count:** {table.Statistics.RecordCount:N0}");
        sb.AppendLine($"**Column Count:** {table.Schema.Columns.Count}");
        sb.AppendLine($"**Quality Score:** {table.DataQuality.QualityScore}/100");
        sb.AppendLine();
        
        sb.AppendLine("## Schema");
        sb.AppendLine("| Column | Type | Nullable | Primary Key |");
        sb.AppendLine("|--------|------|----------|-------------|");
        
        foreach (var column in table.Schema.Columns)
        {
            sb.AppendLine($"| {column.Name} | {column.DataType} | {(column.IsNullable ? "Yes" : "No")} | {(column.IsPrimaryKey ? "Yes" : "No")} |");
        }
        
        return sb.ToString();
    }

    private static string AssessImpact(List<string> modifications)
    {
        return modifications.Count switch
        {
            1 => "Low impact - single change detected",
            <= 3 => "Medium impact - multiple changes detected",
            _ => "High impact - significant changes detected"
        };
    }

    private static List<string> GenerateQualityRecommendations(List<string> issues)
    {
        var recommendations = new List<string>();
        
        if (issues.Any(i => i.Contains("null", StringComparison.OrdinalIgnoreCase)))
        {
            recommendations.Add("Consider adding NOT NULL constraints to reduce null values");
        }
        
        if (issues.Any(i => i.Contains("duplicate", StringComparison.OrdinalIgnoreCase)))
        {
            recommendations.Add("Review data deduplication strategies");
        }
        
        return recommendations;
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

    // Conversion helper methods
    private static TableSchemaInfo ConvertSchemaMetadata(TableSchemaMetadata source)
    {
        return new TableSchemaInfo
        {
            Columns = source.Columns.Select(c => new ColumnInfo
            {
                Name = c.Name,
                DataType = c.DeclaredType,
                IsNullable = c.IsNullable,
                IsPrimaryKey = c.IsPrimaryKey
            }).ToList().AsReadOnly(),
            PrimaryKeyColumns = source.PrimaryKeyColumns,
            Indexes = source.Indexes
        };
    }

    private static TablePartitioningSummary ConvertPartitioningMetadata(PartitioningMetadata source)
    {
        return new TablePartitioningSummary
        {
            Strategy = source.Strategy,
            PartitionCount = source.PartitionCount,
            Partitions = new List<PartitionInfo>().AsReadOnly() // Would be populated from actual partition data
        };
    }

    private static DB2XL.Core.Services.TableStatistics ConvertTableStatistics(DB2XL.Core.Services.TableStatistics source)
    {
        return new DB2XL.Core.Services.TableStatistics
        {
            RecordCount = source.RecordCount,
            SizeBytes = source.SizeBytes,
            LastUpdated = source.LastUpdated
        };
    }

    private static TableDataQuality ConvertDataQuality(DataQualityMetrics source)
    {
        return new TableDataQuality
        {
            QualityScore = source.QualityScore,
            NullValueCount = source.NullCount,
            DuplicateRecordCount = source.DuplicateCount,
            DataIssues = source.Issues
        };
    }

    #endregion
}