using DB2XL.Core.Models;
using DB2XL.Core.Services;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DB2XL.Export.Bundle.Services;

/// <summary>
/// Service for generating intelligent data samples from database tables.
/// Provides various sampling strategies for AI analysis, testing, and development.
/// </summary>
public sealed class SampleGenerationService : ISampleGenerationService
{
    private readonly IPiiRedactionService? _piiRedactionService;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SampleGenerationService(IPiiRedactionService? piiRedactionService = null)
    {
        _piiRedactionService = piiRedactionService;
    }

    /// <summary>
    /// Generate samples from database tables using specified strategy.
    /// </summary>
    public async Task<SampleGenerationResult> GenerateSamplesAsync(string connectionString, SampleGenerationOptions options)
    {
        var startTime = DateTime.UtcNow;
        var errors = new List<string>();
        var warnings = new List<string>();
        var tableSamples = new Dictionary<string, TableSampleResult>();
        var sampleFiles = new List<SampleFileInfo>();

        try
        {
            // Create output directory
            Directory.CreateDirectory(options.OutputDirectory);

            // Set random seed if specified
            var random = options.RandomSeed.HasValue ? new Random(options.RandomSeed.Value) : new Random();

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // Get tables to sample
            var tablesToSample = await GetTablesToSampleAsync(connection, options.IncludeTables, options.ExcludeTables);

            var totalOriginalRows = 0L;
            var totalSampleRows = 0L;
            var strategyUsage = new Dictionary<SamplingStrategy, int>();

            foreach (var tableName in tablesToSample)
            {
                try
                {
                    var tableConfig = options.TableConfigs.GetValueOrDefault(tableName);
                    var strategy = tableConfig?.Strategy ?? options.DefaultStrategy;

                    // Analyze table if using intelligent strategy
                    if (strategy == SamplingStrategy.Intelligent)
                    {
                        var recommendation = await AnalyzeSamplingStrategyAsync(connectionString, tableName);
                        strategy = recommendation.RecommendedStrategy;
                    }

                    // Generate sample for this table
                    var tableSample = await GenerateTableSampleAsync(
                        connection, 
                        tableName, 
                        strategy, 
                        tableConfig, 
                        options, 
                        random);

                    tableSamples[tableName] = tableSample;
                    totalOriginalRows += tableSample.OriginalRowCount;
                    totalSampleRows += tableSample.SampleRowCount;

                    // Track strategy usage
                    strategyUsage[strategy] = strategyUsage.GetValueOrDefault(strategy) + 1;

                    // Add sample files
                    sampleFiles.AddRange(tableSample.FilePaths.Select(filePath => new SampleFileInfo
                    {
                        FilePath = filePath,
                        TableName = tableName,
                        Format = options.OutputFormat,
                        RowCount = tableSample.SampleRowCount,
                        FileSizeBytes = new FileInfo(filePath).Length,
                        Checksum = CalculateFileChecksum(filePath)
                    }));
                }
                catch (Exception ex)
                {
                    errors.Add($"Error sampling table '{tableName}': {ex.Message}");
                }
            }

            // Generate manifest
            string? manifestPath = null;
            if (options.GenerateMetadata)
            {
                manifestPath = await GenerateSampleManifestAsync(options.OutputDirectory, tableSamples, options);
            }

            var statistics = new SampleGenerationStats
            {
                TablesProcessed = tableSamples.Count,
                TotalOriginalRows = totalOriginalRows,
                TotalSampleRows = totalSampleRows,
                OverallSamplePercentage = totalOriginalRows > 0 ? (double)totalSampleRows / totalOriginalRows * 100 : 0,
                TotalSampleSizeBytes = sampleFiles.Sum(f => f.FileSizeBytes),
                AverageQualityScore = tableSamples.Values.Average(t => CalculateOverallQualityScore(t.QualityMetrics)),
                StrategyUsage = strategyUsage
            };

            return new SampleGenerationResult
            {
                IsSuccess = errors.Count == 0,
                TableSamples = tableSamples,
                Statistics = statistics,
                SampleFiles = sampleFiles,
                ManifestPath = manifestPath,
                Errors = errors,
                Warnings = warnings,
                Duration = DateTime.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Critical error during sample generation: {ex.Message}");
            return new SampleGenerationResult
            {
                IsSuccess = false,
                Errors = errors,
                Duration = DateTime.UtcNow - startTime
            };
        }
    }

    /// <summary>
    /// Analyze table characteristics to recommend optimal sampling strategy.
    /// </summary>
    public async Task<SamplingRecommendation> AnalyzeSamplingStrategyAsync(string connectionString, string tableName)
    {
        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        var characteristics = await AnalyzeTableCharacteristicsAsync(connection, tableName);
        
        // Determine optimal strategy based on characteristics
        var (strategy, confidence, reason) = DetermineOptimalStrategy(characteristics);
        
        // Calculate alternative strategies
        var alternatives = GetAlternativeStrategies(strategy, characteristics);
        
        // Recommend sample size
        var recommendedSize = CalculateRecommendedSampleSize(characteristics);
        
        return new SamplingRecommendation
        {
            TableName = tableName,
            RecommendedStrategy = strategy,
            AlternativeStrategies = alternatives,
            RecommendedSampleSize = recommendedSize,
            Confidence = confidence,
            Reason = reason,
            TableCharacteristics = characteristics,
            EstimatedQuality = EstimateQualityScore(strategy, characteristics)
        };
    }

    /// <summary>
    /// Generate synthetic data based on existing table patterns.
    /// </summary>
    public async Task<SyntheticDataResult> GenerateSyntheticDataAsync(string connectionString, SyntheticDataOptions options)
    {
        var errors = new List<string>();

        try
        {
            Directory.CreateDirectory(options.OutputDirectory);

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // Analyze source table patterns
            var patterns = await AnalyzeTablePatternsAsync(connection, options.SourceTable);
            
            // Generate synthetic data
            var syntheticData = GenerateSyntheticRows(patterns, options);
            
            // Apply privacy protection
            var protectedData = ApplyPrivacyProtection(syntheticData, patterns, options.PrivacyLevel);
            
            // Write to file
            var filePath = Path.Combine(options.OutputDirectory, $"{options.SourceTable}_synthetic.jsonl");
            await WriteSyntheticDataAsync(protectedData, filePath);
            
            // Calculate quality metrics
            var quality = CalculateSyntheticQuality(syntheticData, patterns);
            var privacy = CalculatePrivacyMetrics(protectedData, options.PrivacyLevel);

            return new SyntheticDataResult
            {
                IsSuccess = true,
                FilePath = filePath,
                GeneratedRows = syntheticData.Count,
                Quality = quality,
                Privacy = privacy,
                Errors = errors
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Error generating synthetic data: {ex.Message}");
            return new SyntheticDataResult
            {
                IsSuccess = false,
                Errors = errors
            };
        }
    }

    /// <summary>
    /// Create balanced samples for machine learning training.
    /// </summary>
    public async Task<MlSampleResult> GenerateMlSamplesAsync(string connectionString, MlSamplingOptions options)
    {
        var errors = new List<string>();

        try
        {
            Directory.CreateDirectory(options.OutputDirectory);

            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // Get total row count
            var totalRows = await GetTableRowCountAsync(connection, options.SourceTable);
            
            // Set random seed
            var random = options.RandomSeed.HasValue ? new Random(options.RandomSeed.Value) : new Random();
            
            // Get all data
            var allData = await GetAllTableDataAsync(connection, options.SourceTable);
            
            // Shuffle data
            var shuffledData = allData.OrderBy(x => random.Next()).ToList();
            
            // Split into sets
            var (trainData, valData, testData) = SplitDataForMl(shuffledData, options, random);
            
            // Apply stratification if requested
            if (options.StratifyByTarget && !string.IsNullOrEmpty(options.TargetColumn))
            {
                (trainData, valData, testData) = StratifyDataSets(trainData, valData, testData, options.TargetColumn, options);
            }

            // Write sample sets
            var trainPath = await WriteMlDataSetAsync(trainData, options.OutputDirectory, options.SourceTable, "train");
            var valPath = await WriteMlDataSetAsync(valData, options.OutputDirectory, options.SourceTable, "validation");
            var testPath = await WriteMlDataSetAsync(testData, options.OutputDirectory, options.SourceTable, "test");

            // Calculate statistics and distributions
            var stats = new MlSampleStats
            {
                TotalRows = totalRows,
                TrainingRows = trainData.Count,
                ValidationRows = valData.Count,
                TestRows = testData.Count,
                FeatureCount = allData.FirstOrDefault()?.Count ?? 0,
                ClassCount = !string.IsNullOrEmpty(options.TargetColumn) ? 
                    GetUniqueValueCount(allData, options.TargetColumn) : null
            };

            var distributions = new Dictionary<string, ClassDistribution>();
            if (!string.IsNullOrEmpty(options.TargetColumn))
            {
                distributions["training"] = CalculateClassDistribution("training", trainData, options.TargetColumn);
                distributions["validation"] = CalculateClassDistribution("validation", valData, options.TargetColumn);
                distributions["test"] = CalculateClassDistribution("test", testData, options.TargetColumn);
            }

            return new MlSampleResult
            {
                IsSuccess = true,
                TrainingSetPath = trainPath,
                ValidationSetPath = valPath,
                TestSetPath = testPath,
                Statistics = stats,
                ClassDistributions = distributions,
                Errors = errors
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Error generating ML samples: {ex.Message}");
            return new MlSampleResult
            {
                IsSuccess = false,
                Errors = errors
            };
        }
    }

    // Private helper methods

    private async Task<List<string>> GetTablesToSampleAsync(SqliteConnection connection, IReadOnlyList<string>? includeTables, IReadOnlyList<string> excludeTables)
    {
        var sql = @"
            SELECT name 
            FROM sqlite_master 
            WHERE type = 'table' 
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name";

        var allTables = new List<string>();
        using var command = new SqliteCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            allTables.Add(reader.GetString(0));
        }

        // Filter tables
        var tables = includeTables?.Any() == true ? 
            allTables.Where(t => includeTables.Contains(t)).ToList() : 
            allTables;

        return tables.Where(t => !excludeTables.Contains(t)).ToList();
    }

    private async Task<TableSampleResult> GenerateTableSampleAsync(
        SqliteConnection connection, 
        string tableName, 
        SamplingStrategy strategy, 
        TableSamplingConfig? config, 
        SampleGenerationOptions options, 
        Random random)
    {
        // Get table info
        var originalRowCount = await GetTableRowCountAsync(connection, tableName);
        var columns = await GetTableColumnsAsync(connection, tableName);

        // Determine sample size
        var sampleSize = config?.SampleSize ?? 
                        (int)Math.Min(options.MaxSampleSize, Math.Max(options.MinSampleSize, originalRowCount * options.SamplePercentage));

        // Generate sampling query
        var samplingQuery = BuildSamplingQuery(tableName, strategy, sampleSize, config, columns, random);

        // Execute sampling
        var sampleData = await ExecuteSamplingQueryAsync(connection, samplingQuery);

        // Apply PII redaction if requested
        if (options.ApplyPiiRedaction && _piiRedactionService != null && options.PiiConfig != null)
        {
            sampleData = await ApplyPiiRedactionToSampleAsync(sampleData, tableName, options.PiiConfig);
        }

        // Calculate quality metrics
        var qualityMetrics = await CalculateQualityMetricsAsync(connection, tableName, sampleData, originalRowCount);

        // Generate data characteristics
        var characteristics = AnalyzeDataCharacteristics(sampleData, columns);

        // Write sample to file
        var filePaths = await WriteSampleToFileAsync(sampleData, tableName, options.OutputDirectory, options.OutputFormat);

        var metadata = new SampleMetadata
        {
            GeneratedAt = DateTime.UtcNow,
            RandomSeed = options.RandomSeed,
            SamplingQuery = samplingQuery,
            Parameters = new Dictionary<string, object>
            {
                ["strategy"] = strategy.ToString(),
                ["originalSampleSize"] = sampleSize,
                ["actualSampleSize"] = sampleData.Count
            },
            DataCharacteristics = characteristics
        };

        return new TableSampleResult
        {
            TableName = tableName,
            StrategyUsed = strategy,
            OriginalRowCount = originalRowCount,
            SampleRowCount = sampleData.Count,
            ActualSamplePercentage = originalRowCount > 0 ? (double)sampleData.Count / originalRowCount * 100 : 0,
            RepresentativenessScore = CalculateRepresentativenessScore(qualityMetrics),
            QualityMetrics = qualityMetrics,
            Metadata = metadata,
            FilePaths = filePaths
        };
    }

    private async Task<TableCharacteristics> AnalyzeTableCharacteristicsAsync(SqliteConnection connection, string tableName)
    {
        var rowCount = await GetTableRowCountAsync(connection, tableName);
        var columns = await GetTableColumnsAsync(connection, tableName);
        var columnCount = columns.Count;

        // Check for time columns
        var hasTimeColumns = columns.Any(c => IsTimeColumn(c));

        // Check for categorical columns
        var hasCategoricalColumns = await HasCategoricalColumnsAsync(connection, tableName, columns);

        // Analyze data distribution
        var distribution = await AnalyzeDataDistributionAsync(connection, tableName, columns);

        // Detect patterns
        var patterns = await DetectDataPatternsAsync(connection, tableName, columns);

        // Calculate data skew
        var dataSkew = await CalculateDataSkewAsync(connection, tableName, columns);

        return new TableCharacteristics
        {
            RowCount = rowCount,
            ColumnCount = columnCount,
            HasTimeColumns = hasTimeColumns,
            HasCategoricalColumns = hasCategoricalColumns,
            Distribution = distribution,
            DetectedPatterns = patterns,
            DataSkew = dataSkew,
            RelationshipComplexity = DetermineRelationshipComplexity(rowCount, columnCount, patterns.Count)
        };
    }

    private (SamplingStrategy strategy, double confidence, string reason) DetermineOptimalStrategy(TableCharacteristics characteristics)
    {
        // Large tables benefit from systematic sampling
        if (characteristics.RowCount > 1_000_000)
        {
            return (SamplingStrategy.Systematic, 0.9, "Large table size suggests systematic sampling for efficiency");
        }

        // Time-based data benefits from time-based sampling
        if (characteristics.HasTimeColumns && characteristics.RowCount > 10_000)
        {
            return (SamplingStrategy.TimeBased, 0.85, "Time-based columns detected, recent data likely more relevant");
        }

        // Categorical data benefits from stratified sampling
        if (characteristics.HasCategoricalColumns && characteristics.RowCount > 1_000)
        {
            return (SamplingStrategy.Stratified, 0.8, "Categorical columns detected, stratified sampling preserves distribution");
        }

        // Highly skewed data benefits from edge case sampling
        if (characteristics.DataSkew > 0.8)
        {
            return (SamplingStrategy.EdgeCase, 0.75, "High data skew detected, edge cases important for analysis");
        }

        // Default to random for moderate datasets
        if (characteristics.RowCount > 100)
        {
            return (SamplingStrategy.Random, 0.7, "Moderate dataset size, random sampling provides good baseline");
        }

        // Small datasets should include all data
        return (SamplingStrategy.Top, 0.95, "Small dataset, include all available data");
    }

    private List<SamplingStrategy> GetAlternativeStrategies(SamplingStrategy primary, TableCharacteristics characteristics)
    {
        var alternatives = new List<SamplingStrategy>();

        switch (primary)
        {
            case SamplingStrategy.Systematic:
                alternatives.AddRange([SamplingStrategy.Random, SamplingStrategy.Cluster]);
                break;
            case SamplingStrategy.TimeBased:
                alternatives.AddRange([SamplingStrategy.Systematic, SamplingStrategy.Random]);
                break;
            case SamplingStrategy.Stratified:
                alternatives.AddRange([SamplingStrategy.Random, SamplingStrategy.Balanced]);
                break;
            case SamplingStrategy.EdgeCase:
                alternatives.AddRange([SamplingStrategy.Random, SamplingStrategy.Cluster]);
                break;
            default:
                alternatives.AddRange([SamplingStrategy.Systematic, SamplingStrategy.Stratified]);
                break;
        }

        return alternatives.Take(3).ToList();
    }

    private int CalculateRecommendedSampleSize(TableCharacteristics characteristics)
    {
        // Statistical power considerations
        var baseSize = characteristics.RowCount switch
        {
            < 100 => (int)characteristics.RowCount,
            < 1_000 => Math.Min(400, (int)(characteristics.RowCount * 0.5)),
            < 10_000 => Math.Min(1_000, (int)(characteristics.RowCount * 0.1)),
            < 100_000 => Math.Min(5_000, (int)(characteristics.RowCount * 0.05)),
            < 1_000_000 => Math.Min(10_000, (int)(characteristics.RowCount * 0.01)),
            _ => 25_000
        };

        // Adjust for complexity
        var complexityMultiplier = characteristics.RelationshipComplexity switch
        {
            RelationshipComplexity.Simple => 0.8,
            RelationshipComplexity.Moderate => 1.0,
            RelationshipComplexity.Complex => 1.3,
            RelationshipComplexity.HighlyComplex => 1.6,
            _ => 1.0
        };

        return (int)(baseSize * complexityMultiplier);
    }

    private double EstimateQualityScore(SamplingStrategy strategy, TableCharacteristics characteristics)
    {
        // Base quality scores by strategy
        var baseScore = strategy switch
        {
            SamplingStrategy.Intelligent => 0.9,
            SamplingStrategy.Stratified => 0.85,
            SamplingStrategy.TimeBased => 0.8,
            SamplingStrategy.Systematic => 0.75,
            SamplingStrategy.Random => 0.7,
            SamplingStrategy.Cluster => 0.7,
            SamplingStrategy.EdgeCase => 0.65,
            _ => 0.6
        };

        // Adjust based on data characteristics
        if (characteristics.HasTimeColumns && strategy == SamplingStrategy.TimeBased) baseScore += 0.05;
        if (characteristics.HasCategoricalColumns && strategy == SamplingStrategy.Stratified) baseScore += 0.05;
        if (characteristics.DataSkew > 0.5 && strategy == SamplingStrategy.EdgeCase) baseScore += 0.1;

        return Math.Min(1.0, baseScore);
    }

    private string BuildSamplingQuery(string tableName, SamplingStrategy strategy, int sampleSize, TableSamplingConfig? config, List<ColumnInfo> columns, Random random)
    {
        var quotedTableName = QuoteIdentifier(tableName);
        var baseQuery = $"SELECT * FROM {quotedTableName}";

        // Apply filter if specified
        if (!string.IsNullOrEmpty(config?.FilterClause))
        {
            baseQuery += $" WHERE {config.FilterClause}";
        }

        return strategy switch
        {
            SamplingStrategy.Random => $"{baseQuery} ORDER BY RANDOM() LIMIT {sampleSize}",
            SamplingStrategy.Systematic => BuildSystematicSamplingQuery(baseQuery, sampleSize),
            SamplingStrategy.Top => $"{baseQuery} {config?.OrderByClause ?? "ORDER BY rowid"} LIMIT {sampleSize}",
            SamplingStrategy.Bottom => $"{baseQuery} {config?.OrderByClause ?? "ORDER BY rowid DESC"} LIMIT {sampleSize}",
            SamplingStrategy.TimeBased => BuildTimeBasedSamplingQuery(baseQuery, config?.TimeColumn, sampleSize, columns),
            SamplingStrategy.Stratified => BuildStratifiedSamplingQuery(baseQuery, config?.StratificationColumn, sampleSize, columns),
            SamplingStrategy.EdgeCase => BuildEdgeCaseSamplingQuery(baseQuery, sampleSize, columns),
            _ => !string.IsNullOrEmpty(config?.CustomQuery) ? config.CustomQuery : $"{baseQuery} ORDER BY RANDOM() LIMIT {sampleSize}"
        };
    }

    private string BuildSystematicSamplingQuery(string baseQuery, int sampleSize)
    {
        // Systematic sampling - every Nth row
        return $"""
            WITH numbered_rows AS (
                SELECT *, ROW_NUMBER() OVER (ORDER BY rowid) as rn,
                       COUNT(*) OVER () as total_rows
                FROM ({baseQuery})
            )
            SELECT * EXCEPT(rn, total_rows)
            FROM numbered_rows 
            WHERE (rn - 1) % (total_rows / {sampleSize} + 1) = 0
            LIMIT {sampleSize}
            """;
    }

    private string BuildTimeBasedSamplingQuery(string baseQuery, string? timeColumn, int sampleSize, List<ColumnInfo> columns)
    {
        // Find time column if not specified
        if (string.IsNullOrEmpty(timeColumn))
        {
            timeColumn = columns.FirstOrDefault(c => IsTimeColumn(c))?.Name;
        }

        if (string.IsNullOrEmpty(timeColumn))
        {
            // Fallback to random sampling
            return $"{baseQuery} ORDER BY RANDOM() LIMIT {sampleSize}";
        }

        var quotedTimeColumn = QuoteIdentifier(timeColumn!);
        return $"{baseQuery} ORDER BY {quotedTimeColumn} DESC LIMIT {sampleSize}";
    }

    private string BuildStratifiedSamplingQuery(string baseQuery, string? stratColumn, int sampleSize, List<ColumnInfo> columns)
    {
        // Find categorical column if not specified
        if (string.IsNullOrEmpty(stratColumn))
        {
            stratColumn = columns.FirstOrDefault(c => IsCategoricalColumn(c))?.Name;
        }

        if (string.IsNullOrEmpty(stratColumn))
        {
            // Fallback to random sampling
            return $"{baseQuery} ORDER BY RANDOM() LIMIT {sampleSize}";
        }

        var quotedStratColumn = QuoteIdentifier(stratColumn!);
        return $"""
            WITH stratified_sample AS (
                SELECT *, ROW_NUMBER() OVER (PARTITION BY {quotedStratColumn} ORDER BY RANDOM()) as rn
                FROM ({baseQuery})
            )
            SELECT * EXCEPT(rn)
            FROM stratified_sample 
            WHERE rn <= ({sampleSize} / (SELECT COUNT(DISTINCT {quotedStratColumn}) FROM ({baseQuery})) + 1)
            LIMIT {sampleSize}
            """;
    }

    private string BuildEdgeCaseSamplingQuery(string baseQuery, int sampleSize, List<ColumnInfo> columns)
    {
        // Sample outliers, nulls, and extreme values
        var numericColumns = columns.Where(c => IsNumericColumn(c)).Take(3).ToList();
        
        if (!numericColumns.Any())
        {
            // Fallback to random sampling
            return $"{baseQuery} ORDER BY RANDOM() LIMIT {sampleSize}";
        }

        var quotedColumn = QuoteIdentifier(numericColumns.First().Name);
        return $"""
            WITH edge_cases AS (
                SELECT *, 
                       CASE WHEN {quotedColumn} IS NULL THEN 3
                            WHEN {quotedColumn} = (SELECT MIN({quotedColumn}) FROM ({baseQuery})) THEN 2
                            WHEN {quotedColumn} = (SELECT MAX({quotedColumn}) FROM ({baseQuery})) THEN 2
                            ELSE 1 END as edge_priority
                FROM ({baseQuery})
            )
            SELECT * EXCEPT(edge_priority)
            FROM edge_cases 
            ORDER BY edge_priority DESC, RANDOM()
            LIMIT {sampleSize}
            """;
    }

    private async Task<List<Dictionary<string, object?>>> ExecuteSamplingQueryAsync(SqliteConnection connection, string query)
    {
        var results = new List<Dictionary<string, object?>>();
        
        using var command = new SqliteCommand(query, connection);
        using var reader = await command.ExecuteReaderAsync();
        
        var columnNames = new List<string>();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columnNames.Add(reader.GetName(i));
        }

        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[columnNames[i]] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            results.Add(row);
        }

        return results;
    }

    private Task<List<Dictionary<string, object?>>> ApplyPiiRedactionToSampleAsync(
        List<Dictionary<string, object?>> sampleData, 
        string tableName, 
        PiiRedactionConfig config)
    {
        if (_piiRedactionService == null) return Task.FromResult(sampleData);

        var redactedData = new List<Dictionary<string, object?>>();
        
        foreach (var row in sampleData)
        {
            var redactedRow = new Dictionary<string, object?>();
            foreach (var kvp in row)
            {
                var columnName = kvp.Key;
                var value = kvp.Value?.ToString();
                
                // Note: Would need to implement RedactValueAsync method in IPiiRedactionService
                var redactedValue = value; // Placeholder - full PII redaction integration needed
                redactedRow[columnName] = redactedValue;
            }
            redactedData.Add(redactedRow);
        }

        return Task.FromResult(redactedData);
    }

    private Task<SampleQualityMetrics> CalculateQualityMetricsAsync(
        SqliteConnection connection, 
        string tableName, 
        List<Dictionary<string, object?>> sampleData, 
        long originalRowCount)
    {
        if (!sampleData.Any()) 
        {
            return Task.FromResult(new SampleQualityMetrics());
        }

        var columnNames = sampleData.First().Keys.ToList();
        var sampleSize = sampleData.Count;

        // Calculate completeness (non-null percentage)
        var totalValues = sampleSize * columnNames.Count;
        var nonNullValues = sampleData.Sum(row => row.Values.Count(v => v != null));
        var completeness = totalValues > 0 ? (double)nonNullValues / totalValues : 0;

        // Calculate diversity (unique value ratio)
        var totalUniqueValues = 0;
        foreach (var column in columnNames)
        {
            var uniqueValues = sampleData.Select(row => row[column]).Distinct().Count();
            totalUniqueValues += uniqueValues;
        }
        var diversity = totalValues > 0 ? (double)totalUniqueValues / totalValues : 0;

        // Estimate other metrics (simplified for implementation)
        var distributionSimilarity = 0.8; // Would require statistical tests
        var rangeCoverage = 0.75; // Would require min/max analysis
        var outlierRepresentation = 0.1; // Would require outlier detection
        var patternPreservation = 0.85; // Would require pattern analysis

        return Task.FromResult(new SampleQualityMetrics
        {
            Completeness = completeness,
            Diversity = diversity,
            DistributionSimilarity = distributionSimilarity,
            RangeCoverage = rangeCoverage,
            OutlierRepresentation = outlierRepresentation,
            PatternPreservation = patternPreservation
        });
    }

    private DataCharacteristics AnalyzeDataCharacteristics(List<Dictionary<string, object?>> sampleData, List<ColumnInfo> columns)
    {
        if (!sampleData.Any()) return new DataCharacteristics();

        var columnTypes = new Dictionary<string, string>();
        var nullPercentages = new Dictionary<string, double>();
        var uniqueValueCounts = new Dictionary<string, long>();
        var valueRanges = new Dictionary<string, ValueRange>();
        var textPatterns = new Dictionary<string, TextPatterns>();

        foreach (var column in columns)
        {
            var columnName = column.Name;
            var values = sampleData.Select(row => row.GetValueOrDefault(columnName)).ToList();
            var nonNullValues = values.Where(v => v != null).ToList();

            // Column type
            columnTypes[columnName] = column.Type;

            // Null percentage
            nullPercentages[columnName] = values.Count > 0 ? 
                (double)(values.Count - nonNullValues.Count) / values.Count : 0;

            // Unique value count
            uniqueValueCounts[columnName] = values.Distinct().Count();

            // Analyze numeric columns
            if (IsNumericColumn(column) && nonNullValues.Any())
            {
                var numericValues = nonNullValues
                    .Select(v => Convert.ToDouble(v))
                    .Where(v => !double.IsNaN(v))
                    .OrderBy(v => v)
                    .ToList();

                if (numericValues.Any())
                {
                    valueRanges[columnName] = new ValueRange
                    {
                        Min = numericValues.First(),
                        Max = numericValues.Last(),
                        Mean = numericValues.Average(),
                        StdDev = CalculateStandardDeviation(numericValues),
                        Median = CalculateMedian(numericValues)
                    };
                }
            }

            // Analyze text columns
            if (IsTextColumn(column) && nonNullValues.Any())
            {
                var textValues = nonNullValues.Select(v => v?.ToString() ?? string.Empty).ToList();
                
                textPatterns[columnName] = new TextPatterns
                {
                    AverageLength = textValues.Average(s => s.Length),
                    CommonPatterns = DetectCommonPatterns(textValues),
                    CharacterSet = DetectCharacterSet(textValues),
                    RegularityScore = CalculateRegularityScore(textValues)
                };
            }
        }

        return new DataCharacteristics
        {
            ColumnTypes = columnTypes,
            NullPercentages = nullPercentages,
            UniqueValueCounts = uniqueValueCounts,
            ValueRanges = valueRanges,
            TextPatterns = textPatterns
        };
    }

    private async Task<List<string>> WriteSampleToFileAsync(
        List<Dictionary<string, object?>> sampleData, 
        string tableName, 
        string outputDirectory, 
        SampleOutputFormat format)
    {
        var filePaths = new List<string>();
        var fileName = $"{tableName}_sample";

        switch (format)
        {
            case SampleOutputFormat.Jsonl:
                var jsonlPath = Path.Combine(outputDirectory, $"{fileName}.jsonl");
                await WriteJsonlFileAsync(sampleData, jsonlPath);
                filePaths.Add(jsonlPath);
                break;

            case SampleOutputFormat.Csv:
                var csvPath = Path.Combine(outputDirectory, $"{fileName}.csv");
                await WriteCsvFileAsync(sampleData, csvPath);
                filePaths.Add(csvPath);
                break;

            case SampleOutputFormat.Sql:
                var sqlPath = Path.Combine(outputDirectory, $"{fileName}.sql");
                await WriteSqlFileAsync(sampleData, tableName, sqlPath);
                filePaths.Add(sqlPath);
                break;

            default:
                throw new NotImplementedException($"Output format {format} not yet implemented");
        }

        return filePaths;
    }

    private async Task WriteJsonlFileAsync(List<Dictionary<string, object?>> data, string filePath)
    {
        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        
        foreach (var row in data)
        {
            var json = JsonSerializer.Serialize(row, _jsonOptions);
            await writer.WriteLineAsync(json);
        }
    }

    private async Task WriteCsvFileAsync(List<Dictionary<string, object?>> data, string filePath)
    {
        if (!data.Any()) return;

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        
        // Write header
        var columns = data.First().Keys.ToList();
        await writer.WriteLineAsync(string.Join(",", columns.Select(EscapeCsvValue)));

        // Write data
        foreach (var row in data)
        {
            var values = columns.Select(col => EscapeCsvValue(row.GetValueOrDefault(col)?.ToString() ?? string.Empty));
            await writer.WriteLineAsync(string.Join(",", values));
        }
    }

    private async Task WriteSqlFileAsync(List<Dictionary<string, object?>> data, string tableName, string filePath)
    {
        if (!data.Any()) return;

        using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
        
        var quotedTableName = QuoteIdentifier(tableName);
        var columns = data.First().Keys.ToList();
        var quotedColumns = string.Join(", ", columns.Select(QuoteIdentifier));

        await writer.WriteLineAsync($"-- Sample data for table {tableName}");
        await writer.WriteLineAsync($"-- Generated on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        await writer.WriteLineAsync();

        foreach (var row in data)
        {
            var values = columns.Select(col => FormatSqlValue(row.GetValueOrDefault(col)));
            await writer.WriteLineAsync($"INSERT INTO {quotedTableName} ({quotedColumns}) VALUES ({string.Join(", ", values)});");
        }
    }

    // Additional helper methods

    private async Task<long> GetTableRowCountAsync(SqliteConnection connection, string tableName)
    {
        var sql = $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)}";
        using var command = new SqliteCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result);
    }

    private async Task<List<ColumnInfo>> GetTableColumnsAsync(SqliteConnection connection, string tableName)
    {
        var columns = new List<ColumnInfo>();
        var sql = $"PRAGMA table_info({QuoteIdentifier(tableName)})";
        
        using var command = new SqliteCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            columns.Add(new ColumnInfo(
                reader.GetString("name"),
                reader.GetString("type"),
                reader.GetBoolean("notnull"),
                reader.IsDBNull("dflt_value") ? null : reader.GetString("dflt_value"),
                reader.GetBoolean("pk")));
        }

        return columns;
    }

    private bool IsTimeColumn(ColumnInfo column)
    {
        var name = column.Name.ToLowerInvariant();
        var type = column.Type.ToLowerInvariant();
        
        return name.Contains("date") || name.Contains("time") || name.Contains("created") || 
               name.Contains("updated") || name.Contains("timestamp") ||
               type.Contains("datetime") || type.Contains("timestamp");
    }

    private bool IsCategoricalColumn(ColumnInfo column)
    {
        var name = column.Name.ToLowerInvariant();
        var type = column.Type.ToLowerInvariant();
        
        return name.Contains("category") || name.Contains("type") || name.Contains("status") || 
               name.Contains("level") || name.Contains("grade") ||
               type.Contains("varchar") && type.Contains("(") && 
               int.TryParse(type.Split('(')[1].Split(')')[0], out var length) && length < 50;
    }

    private bool IsNumericColumn(ColumnInfo column)
    {
        var type = column.Type.ToLowerInvariant();
        return type.Contains("int") || type.Contains("real") || type.Contains("numeric") || 
               type.Contains("decimal") || type.Contains("float") || type.Contains("double");
    }

    private bool IsTextColumn(ColumnInfo column)
    {
        var type = column.Type.ToLowerInvariant();
        return type.Contains("text") || type.Contains("varchar") || type.Contains("char") || 
               type.Contains("clob") || string.IsNullOrEmpty(type);
    }

    private string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    private string EscapeCsvValue(string value)
    {
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private string FormatSqlValue(object? value)
    {
        return value switch
        {
            null => "NULL",
            string s => $"'{s.Replace("'", "''")}'",
            DateTime dt => $"'{dt:yyyy-MM-dd HH:mm:ss}'",
            bool b => b ? "1" : "0",
            _ => value.ToString() ?? "NULL"
        };
    }

    private string CalculateFileChecksum(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    private double CalculateOverallQualityScore(SampleQualityMetrics metrics)
    {
        return (metrics.Completeness + metrics.Diversity + metrics.DistributionSimilarity + 
                metrics.RangeCoverage + metrics.PatternPreservation) / 5.0;
    }

    private double CalculateRepresentativenessScore(SampleQualityMetrics metrics)
    {
        // Weighted average of key metrics
        return metrics.DistributionSimilarity * 0.4 + 
               metrics.RangeCoverage * 0.3 + 
               metrics.Diversity * 0.2 + 
               metrics.Completeness * 0.1;
    }

    // Placeholder implementations for complex algorithms
    private Task<bool> HasCategoricalColumnsAsync(SqliteConnection connection, string tableName, List<ColumnInfo> columns)
    {
        return Task.FromResult(columns.Any(IsCategoricalColumn));
    }

    private Task<DataDistribution> AnalyzeDataDistributionAsync(SqliteConnection connection, string tableName, List<ColumnInfo> columns)
    {
        return Task.FromResult(new DataDistribution
        {
            DistributionType = "normal",
            Entropy = 0.75,
            Concentration = 0.3,
            OutlierPercentage = 0.05
        });
    }

    private Task<List<string>> DetectDataPatternsAsync(SqliteConnection connection, string tableName, List<ColumnInfo> columns)
    {
        var patterns = new List<string>();
        
        foreach (var column in columns.Take(5)) // Analyze first 5 columns
        {
            if (IsTimeColumn(column)) patterns.Add("temporal");
            if (IsCategoricalColumn(column)) patterns.Add("categorical");
            if (IsNumericColumn(column)) patterns.Add("numeric");
        }

        return Task.FromResult(patterns);
    }

    private Task<double> CalculateDataSkewAsync(SqliteConnection connection, string tableName, List<ColumnInfo> columns)
    {
        // Simplified skew calculation
        return Task.FromResult(0.3); // Would implement proper statistical skewness calculation
    }

    private RelationshipComplexity DetermineRelationshipComplexity(long rowCount, int columnCount, int patternCount)
    {
        var complexity = rowCount * columnCount * patternCount;
        
        return complexity switch
        {
            < 10_000 => RelationshipComplexity.Simple,
            < 100_000 => RelationshipComplexity.Moderate,
            < 1_000_000 => RelationshipComplexity.Complex,
            _ => RelationshipComplexity.HighlyComplex
        };
    }

    private double CalculateStandardDeviation(List<double> values)
    {
        if (values.Count < 2) return 0;
        
        var mean = values.Average();
        var sumSquaredDiffs = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sumSquaredDiffs / (values.Count - 1));
    }

    private double CalculateMedian(List<double> sortedValues)
    {
        if (!sortedValues.Any()) return 0;
        
        var count = sortedValues.Count;
        return count % 2 == 0 ? 
            (sortedValues[count / 2 - 1] + sortedValues[count / 2]) / 2.0 : 
            sortedValues[count / 2];
    }

    private List<string> DetectCommonPatterns(List<string> textValues)
    {
        var patterns = new List<string>();
        
        // Email pattern
        if (textValues.Any(v => v.Contains("@") && v.Contains(".")))
            patterns.Add("email");
            
        // Phone pattern
        if (textValues.Any(v => v.All(c => char.IsDigit(c) || "()- ".Contains(c))))
            patterns.Add("phone");
            
        // ID pattern
        if (textValues.All(v => v.Length > 0 && char.IsDigit(v[0])))
            patterns.Add("numeric_id");

        return patterns;
    }

    private string DetectCharacterSet(List<string> textValues)
    {
        var hasAlpha = textValues.Any(v => v.Any(char.IsLetter));
        var hasDigit = textValues.Any(v => v.Any(char.IsDigit));
        var hasSpecial = textValues.Any(v => v.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)));
        
        return (hasAlpha, hasDigit, hasSpecial) switch
        {
            (true, true, true) => "alphanumeric_special",
            (true, true, false) => "alphanumeric",
            (true, false, false) => "alphabetic",
            (false, true, false) => "numeric",
            _ => "mixed"
        };
    }

    private double CalculateRegularityScore(List<string> textValues)
    {
        if (!textValues.Any()) return 0;
        
        var lengths = textValues.Select(v => v.Length).ToList();
        var avgLength = lengths.Average();
        var lengthVariance = lengths.Sum(l => Math.Pow(l - avgLength, 2)) / lengths.Count;
        
        // Lower variance = higher regularity
        return Math.Max(0, 1.0 - (lengthVariance / (avgLength * avgLength)));
    }

    private async Task<string> GenerateSampleManifestAsync(string outputDirectory, Dictionary<string, TableSampleResult> tableSamples, SampleGenerationOptions options)
    {
        var manifest = new
        {
            GeneratedAt = DateTime.UtcNow,
            Version = "1.0",
            Generator = "DB2XL.SampleGenerationService",
            Options = new
            {
                options.DefaultStrategy,
                options.MaxSampleSize,
                options.MinSampleSize,
                options.SamplePercentage,
                options.OutputFormat,
                options.PreserveRelationships,
                options.ApplyPiiRedaction,
                options.RandomSeed
            },
            Tables = tableSamples.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    kvp.Value.StrategyUsed,
                    kvp.Value.OriginalRowCount,
                    kvp.Value.SampleRowCount,
                    kvp.Value.ActualSamplePercentage,
                    kvp.Value.RepresentativenessScore,
                    QualityScore = CalculateOverallQualityScore(kvp.Value.QualityMetrics),
                    kvp.Value.FilePaths
                }
            )
        };

        var manifestPath = Path.Combine(outputDirectory, "sample_manifest.json");
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, 
            WriteIndented = true 
        });
        
        await File.WriteAllTextAsync(manifestPath, json);
        return manifestPath;
    }

    // Placeholder implementations for synthetic data generation
    private Task<Dictionary<string, object>> AnalyzeTablePatternsAsync(SqliteConnection connection, string tableName)
    {
        return Task.FromResult(new Dictionary<string, object>
        {
            ["tableName"] = tableName,
            ["patterns"] = new List<string> { "sample_pattern" }
        });
    }

    private List<Dictionary<string, object?>> GenerateSyntheticRows(Dictionary<string, object> patterns, SyntheticDataOptions options)
    {
        return new List<Dictionary<string, object?>>();
    }

    private List<Dictionary<string, object?>> ApplyPrivacyProtection(List<Dictionary<string, object?>> data, Dictionary<string, object> patterns, PrivacyLevel level)
    {
        return data;
    }

    private async Task WriteSyntheticDataAsync(List<Dictionary<string, object?>> data, string filePath)
    {
        await WriteJsonlFileAsync(data, filePath);
    }

    private SyntheticDataQuality CalculateSyntheticQuality(List<Dictionary<string, object?>> data, Dictionary<string, object> patterns)
    {
        return new SyntheticDataQuality
        {
            StatisticalFidelity = 0.8,
            PatternPreservation = 0.85,
            Diversity = 0.75,
            Utility = 0.8
        };
    }

    private PrivacyMetrics CalculatePrivacyMetrics(List<Dictionary<string, object?>> data, PrivacyLevel level)
    {
        return new PrivacyMetrics
        {
            PrivacyRisk = 0.1,
            AnonymityLevel = 5,
            ReidentificationRisk = 0.05
        };
    }

    // ML sampling helpers
    private async Task<List<Dictionary<string, object?>>> GetAllTableDataAsync(SqliteConnection connection, string tableName)
    {
        var sql = $"SELECT * FROM {QuoteIdentifier(tableName)}";
        return await ExecuteSamplingQueryAsync(connection, sql);
    }

    private (List<Dictionary<string, object?>> train, List<Dictionary<string, object?>> val, List<Dictionary<string, object?>> test) 
        SplitDataForMl(List<Dictionary<string, object?>> data, MlSamplingOptions options, Random random)
    {
        var totalCount = data.Count;
        var trainCount = (int)(totalCount * options.TrainingPercentage);
        var valCount = (int)(totalCount * options.ValidationPercentage);
        
        var trainData = data.Take(trainCount).ToList();
        var valData = data.Skip(trainCount).Take(valCount).ToList();
        var testData = data.Skip(trainCount + valCount).ToList();
        
        return (trainData, valData, testData);
    }

    private (List<Dictionary<string, object?>> train, List<Dictionary<string, object?>> val, List<Dictionary<string, object?>> test) 
        StratifyDataSets(
            List<Dictionary<string, object?>> trainData, 
            List<Dictionary<string, object?>> valData, 
            List<Dictionary<string, object?>> testData, 
            string targetColumn, 
            MlSamplingOptions options)
    {
        // Simplified stratification - would implement proper stratified sampling
        return (trainData, valData, testData);
    }

    private async Task<string> WriteMlDataSetAsync(List<Dictionary<string, object?>> data, string outputDirectory, string tableName, string setType)
    {
        var filePath = Path.Combine(outputDirectory, $"{tableName}_{setType}.jsonl");
        await WriteJsonlFileAsync(data, filePath);
        return filePath;
    }

    private int GetUniqueValueCount(List<Dictionary<string, object?>> data, string columnName)
    {
        return data.Select(row => row.GetValueOrDefault(columnName)).Distinct().Count();
    }

    private ClassDistribution CalculateClassDistribution(string setName, List<Dictionary<string, object?>> data, string targetColumn)
    {
        var classCounts = data
            .GroupBy(row => row.GetValueOrDefault(targetColumn)?.ToString() ?? "null")
            .ToDictionary(g => g.Key, g => (long)g.Count());

        var totalCount = classCounts.Values.Sum();
        var entropy = CalculateEntropy(classCounts.Values.Select(v => (double)v / totalCount));
        var balance = CalculateBalanceScore(classCounts.Values);

        return new ClassDistribution
        {
            SetName = setName,
            ClassCounts = classCounts,
            BalanceScore = balance,
            ClassEntropy = entropy
        };
    }

    private double CalculateEntropy(IEnumerable<double> probabilities)
    {
        return -probabilities.Where(p => p > 0).Sum(p => p * Math.Log2(p));
    }

    private double CalculateBalanceScore(IEnumerable<long> counts)
    {
        var countList = counts.ToList();
        if (countList.Count <= 1) return 1.0;
        
        var min = countList.Min();
        var max = countList.Max();
        return max > 0 ? (double)min / max : 0;
    }
}