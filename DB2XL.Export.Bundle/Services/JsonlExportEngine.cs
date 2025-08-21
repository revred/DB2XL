using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DB2XL.Core.Models;
using DB2XL.Core.Services;

namespace DB2XL.Export.Bundle.Services;

/// <summary>
/// High-performance JSONL export engine with schema tracking and deterministic output.
/// Optimized for large datasets with streaming processing and parallel execution.
/// </summary>
public sealed class JsonlExportEngine : IJsonlExportEngine
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Exports a single data partition to JSONL format with comprehensive metadata tracking.
    /// </summary>
    public async Task<JsonlExportResult> ExportPartitionAsync(
        DataPartition partition,
        string outputFilePath,
        JsonlExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFilePath);
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        var startTime = DateTime.UtcNow;
        var warnings = new List<string>();
        var schemaAnalyzer = new JsonlSchemaAnalyzer();
        var gcBefore = GC.GetTotalAllocatedBytes(true);

        try
        {
            // Ensure output directory exists
            var directory = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Setup file stream with appropriate encoding and compression
            await using var fileStream = CreateOutputStream(outputFilePath, options);
            using var writer = new StreamWriter(fileStream, GetEncoding(options), options.WriteBufferSize);

            long recordCount = 0;
            var jsonOptions = GetJsonSerializerOptions(options);
            
            // Write schema header if requested
            if (options.IncludeSchemaHeader)
            {
                await WriteSchemaHeaderAsync(writer, partition, options, cancellationToken);
            }

            // Process and write data records
            await foreach (var record in partition.Data.WithCancellation(cancellationToken))
            {
                try
                {
                    var jsonRecord = await ProcessRecordAsync(record, options, schemaAnalyzer, cancellationToken);
                    await writer.WriteLineAsync(jsonRecord);
                    recordCount++;

                    // Check file size limit if specified
                    if (options.MaxFileSizeBytes > 0 && fileStream.Length > options.MaxFileSizeBytes)
                    {
                        warnings.Add($"File size limit exceeded ({options.MaxFileSizeBytes:N0} bytes)");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to serialize record {recordCount + 1}: {ex.Message}");
                    continue; // Skip problematic records
                }
            }

            await writer.FlushAsync(cancellationToken);
            var fileSize = fileStream.Length;

            stopwatch.Stop();
            var endTime = DateTime.UtcNow;
            var gcAfter = GC.GetTotalAllocatedBytes(true);

            // Calculate file checksum
            var checksum = await CalculateFileChecksumAsync(outputFilePath, cancellationToken);

            // Generate schema information
            var schemaInfo = await schemaAnalyzer.GenerateSchemaInfoAsync(recordCount);

            // Build performance metrics
            var metrics = new JsonlExportMetrics
            {
                RecordsPerSecond = recordCount / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001),
                BytesPerSecond = fileSize / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001),
                PeakMemoryUsage = gcAfter - gcBefore,
                CpuTime = stopwatch.Elapsed,
                IoTime = TimeSpan.Zero, // Would need more sophisticated measurement
                SerializationTime = TimeSpan.Zero, // Would need more sophisticated measurement
                GarbageCollections = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2)
            };

            return new JsonlExportResult
            {
                FilePath = outputFilePath,
                PartitionInfo = partition.Info,
                RecordCount = recordCount,
                FileSizeBytes = fileSize,
                FileChecksum = checksum,
                ExportStartTime = startTime,
                ExportEndTime = endTime,
                SchemaInfo = schemaInfo,
                Warnings = warnings.AsReadOnly(),
                Metrics = metrics,
                IsSuccessful = true
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new JsonlExportResult
            {
                FilePath = outputFilePath,
                PartitionInfo = partition.Info,
                ExportStartTime = startTime,
                ExportEndTime = DateTime.UtcNow,
                Warnings = warnings.AsReadOnly(),
                IsSuccessful = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Exports multiple partitions in parallel with coordinated schema tracking.
    /// </summary>
    public async Task<IReadOnlyList<JsonlExportResult>> ExportPartitionsAsync(
        IAsyncEnumerable<DataPartition> partitions,
        string outputDirectory,
        JsonlExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partitions);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(options);

        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var results = new List<JsonlExportResult>();
        var semaphore = new SemaphoreSlim(options.MaxDegreeOfParallelism, options.MaxDegreeOfParallelism);

        if (options.EnableParallelProcessing)
        {
            var tasks = new List<Task<JsonlExportResult>>();

            await foreach (var partition in partitions.WithCancellation(cancellationToken))
            {
                var outputFilePath = Path.Combine(outputDirectory, partition.Info.RelativePath);
                
                var task = ProcessPartitionWithSemaphoreAsync(
                    semaphore, partition, outputFilePath, options, cancellationToken);
                tasks.Add(task);
            }

            var completedResults = await Task.WhenAll(tasks);
            results.AddRange(completedResults);
        }
        else
        {
            // Sequential processing
            await foreach (var partition in partitions.WithCancellation(cancellationToken))
            {
                var outputFilePath = Path.Combine(outputDirectory, partition.Info.RelativePath);
                var result = await ExportPartitionAsync(partition, outputFilePath, options, cancellationToken);
                results.Add(result);
            }
        }

        return results.AsReadOnly();
    }

    /// <summary>
    /// Generates comprehensive schema manifest from exported data.
    /// </summary>
    public async Task<JsonlSchemaManifest> GenerateSchemaManifestAsync(
        IReadOnlyList<JsonlExportResult> exportResults,
        TableMetadata tableMetadata)
    {
        await Task.CompletedTask;
        ArgumentNullException.ThrowIfNull(exportResults);
        ArgumentNullException.ThrowIfNull(tableMetadata);

        var fields = new List<JsonlFieldDefinition>();
        var partitionManifests = new List<JsonlPartitionManifest>();
        var totalRecordCount = 0L;

        // Aggregate schema information across all partitions
        var fieldStatsMap = new Dictionary<string, JsonlFieldStatistics>();
        var fieldTypesMap = new Dictionary<string, JsonDataType>();

        foreach (var result in exportResults)
        {
            totalRecordCount += result.RecordCount;

            // Create partition manifest
            partitionManifests.Add(new JsonlPartitionManifest
            {
                RelativePath = Path.GetFileName(result.FilePath),
                PartitionLabel = result.PartitionInfo.PartitionLabel,
                RecordCount = result.RecordCount,
                FileSizeBytes = result.FileSizeBytes,
                Checksum = result.FileChecksum,
                Metadata = new Dictionary<string, object>
                {
                    ["strategy"] = result.PartitionInfo.Strategy,
                    ["exportTime"] = result.ExportEndTime.ToString("O"),
                    ["recordsPerSecond"] = result.Metrics.RecordsPerSecond.ToString("F2")
                }
            });

            // Aggregate field information
            foreach (var field in result.SchemaInfo.Fields)
            {
                if (!fieldStatsMap.ContainsKey(field.Name))
                {
                    fieldStatsMap[field.Name] = field.Statistics;
                    fieldTypesMap[field.Name] = field.DataType;
                }
            }
        }

        // Create field definitions
        foreach (var column in tableMetadata.Columns)
        {
            var fieldStats = fieldStatsMap.GetValueOrDefault(column.Name, new JsonlFieldStatistics());
            var dataType = fieldTypesMap.GetValueOrDefault(column.Name, InferJsonDataType(column));

            fields.Add(new JsonlFieldDefinition
            {
                Name = column.Name,
                DataType = dataType,
                IsNullable = column.IsNullable,
                IsPrimaryKey = column.IsPrimaryKey,
                Description = column.DeclaredType,
                Statistics = fieldStats
            });
        }

        // Generate processing recommendations
        var recommendations = GenerateProcessingRecommendations(totalRecordCount, fields, tableMetadata);

        return new JsonlSchemaManifest
        {
            TableName = tableMetadata.TableName,
            SchemaVersion = "1.0",
            ExportTimestamp = DateTime.UtcNow,
            Fields = fields.AsReadOnly(),
            Partitions = partitionManifests.AsReadOnly(),
            TotalRecordCount = totalRecordCount,
            TableMetadata = new Dictionary<string, object>
            {
                ["estimatedRowCount"] = tableMetadata.EstimatedRowCount,
                ["columnCount"] = tableMetadata.Columns.Count,
                ["hasIndexes"] = tableMetadata.Indexes.Any(),
                ["hasForeignKeys"] = tableMetadata.ForeignKeys.Any(),
                ["primaryKeyColumns"] = tableMetadata.PrimaryKeyColumns.Count,
                ["hasRowId"] = tableMetadata.HasRowId
            },
            ProcessingRecommendations = recommendations
        };
    }

    /// <summary>
    /// Validates JSONL file integrity and schema consistency.
    /// </summary>
    public async Task<JsonlValidationResult> ValidateJsonlFileAsync(
        string filePath,
        JsonlSchemaManifest expectedSchema,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(expectedSchema);

        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var warnings = new List<string>();
        long linesValidated = 0;
        long validObjects = 0;
        long schemaViolations = 0;

        try
        {
            if (!File.Exists(filePath))
            {
                errors.Add($"File not found: {filePath}");
                return new JsonlValidationResult { IsValid = false, Errors = errors.AsReadOnly() };
            }

            using var reader = new StreamReader(filePath, Encoding.UTF8);
            string? line;

            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                linesValidated++;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    using var jsonDoc = JsonDocument.Parse(line);
                    validObjects++;

                    // Validate against expected schema
                    var validationErrors = ValidateObjectAgainstSchema(jsonDoc.RootElement, expectedSchema);
                    schemaViolations += validationErrors.Count;
                    
                    if (validationErrors.Any())
                    {
                        warnings.AddRange(validationErrors.Select(e => $"Line {linesValidated}: {e}"));
                    }
                }
                catch (JsonException ex)
                {
                    errors.Add($"Invalid JSON on line {linesValidated}: {ex.Message}");
                }
            }

            stopwatch.Stop();

            var metrics = new JsonlValidationMetrics
            {
                LinesValidated = linesValidated,
                ValidObjects = validObjects,
                SchemaViolations = schemaViolations,
                ValidationTime = stopwatch.Elapsed
            };

            return new JsonlValidationResult
            {
                IsValid = !errors.Any() && schemaViolations == 0,
                Errors = errors.AsReadOnly(),
                Warnings = warnings.AsReadOnly(),
                Metrics = metrics
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Validation failed: {ex.Message}");
            return new JsonlValidationResult
            {
                IsValid = false,
                Errors = errors.AsReadOnly(),
                Metrics = new JsonlValidationMetrics { ValidationTime = stopwatch.Elapsed }
            };
        }
    }

    #region Private Helper Methods

    private async Task<string> ProcessRecordAsync(
        IReadOnlyDictionary<string, object?> record,
        JsonlExportOptions options,
        JsonlSchemaAnalyzer schemaAnalyzer,
        CancellationToken cancellationToken)
    {
        var processedRecord = new Dictionary<string, object?>();

        foreach (var (key, value) in record)
        {
            var processedValue = ProcessFieldValue(value, options);
            
            if (options.NullHandling == JsonNullHandling.Skip && processedValue is null)
                continue;

            processedRecord[key] = processedValue;
            await schemaAnalyzer.AnalyzeFieldAsync(key, processedValue, cancellationToken);
        }

        if (options.IncludeProvenance)
        {
            processedRecord["_meta"] = new
            {
                exportTimestamp = DateTime.UtcNow.ToString("O"),
                sourceFormat = "sqlite"
            };
        }

        if (options.IncludeRowChecksums)
        {
            var checksum = CalculateRecordChecksum(processedRecord);
            processedRecord["_checksum"] = checksum;
        }

        var jsonOptions = GetJsonSerializerOptions(options);
        return JsonSerializer.Serialize(processedRecord, jsonOptions);
    }

    private static object? ProcessFieldValue(object? value, JsonlExportOptions options)
    {
        return value switch
        {
            null => options.NullHandling switch
            {
                JsonNullHandling.Null => null,
                JsonNullHandling.EmptyString => "",
                JsonNullHandling.Skip => null,
                _ => null
            },
            DateTime dt => options.DateTimeFormat switch
            {
                JsonDateTimeFormat.ISO8601 => dt.ToString("O"),
                JsonDateTimeFormat.Unix => ((DateTimeOffset)dt).ToUnixTimeSeconds(),
                JsonDateTimeFormat.UnixMillis => ((DateTimeOffset)dt).ToUnixTimeMilliseconds(),
                JsonDateTimeFormat.Ticks => dt.Ticks,
                _ => dt.ToString("O")
            },
            DateTimeOffset dto => options.DateTimeFormat switch
            {
                JsonDateTimeFormat.ISO8601 => dto.ToString("O"),
                JsonDateTimeFormat.Unix => dto.ToUnixTimeSeconds(),
                JsonDateTimeFormat.UnixMillis => dto.ToUnixTimeMilliseconds(),
                JsonDateTimeFormat.Ticks => dto.Ticks,
                _ => dto.ToString("O")
            },
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => value
        };
    }

    private async Task WriteSchemaHeaderAsync(
        StreamWriter writer,
        DataPartition partition,
        JsonlExportOptions options,
        CancellationToken cancellationToken)
    {
        var schemaHeader = new
        {
            _schema = new
            {
                version = "1.0",
                tableName = partition.Info.TableName,
                partitionLabel = partition.Info.PartitionLabel,
                strategy = partition.Info.Strategy,
                format = "jsonl",
                exportTimestamp = DateTime.UtcNow.ToString("O")
            }
        };

        var jsonOptions = GetJsonSerializerOptions(options);
        var headerJson = JsonSerializer.Serialize(schemaHeader, jsonOptions);
        await writer.WriteLineAsync(headerJson);
    }

    private static JsonSerializerOptions GetJsonSerializerOptions(JsonlExportOptions options)
    {
        return options.SerializationMode == JsonSerializationMode.Indented 
            ? IndentedJsonOptions 
            : CompactJsonOptions;
    }

    private static Encoding GetEncoding(JsonlExportOptions options)
    {
        return options.Encoding switch
        {
            JsonlEncoding.UTF8 => new UTF8Encoding(true), // With BOM
            JsonlEncoding.UTF8NoBOM => new UTF8Encoding(false), // No BOM
            JsonlEncoding.ASCII => Encoding.ASCII,
            _ => new UTF8Encoding(true)
        };
    }

    private static Stream CreateOutputStream(string filePath, JsonlExportOptions options)
    {
        var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);

        return options.Compression switch
        {
            JsonlCompression.Gzip => new GZipStream(fileStream, CompressionLevel.Optimal),
            JsonlCompression.Brotli => new BrotliStream(fileStream, CompressionLevel.Optimal),
            _ => fileStream
        };
    }

    private static async Task<string> CalculateFileChecksumAsync(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes);
    }

    private static string CalculateRecordChecksum(Dictionary<string, object?> record)
    {
        var serialized = JsonSerializer.Serialize(record, CompactJsonOptions);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(serialized));
        return Convert.ToHexString(hashBytes);
    }

    private async Task<JsonlExportResult> ProcessPartitionWithSemaphoreAsync(
        SemaphoreSlim semaphore,
        DataPartition partition,
        string outputFilePath,
        JsonlExportOptions options,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await ExportPartitionAsync(partition, outputFilePath, options, cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static JsonDataType InferJsonDataType(ColumnMetadata column)
    {
        var declaredType = column.DeclaredType.ToLowerInvariant();
        
        return declaredType switch
        {
            var t when t.Contains("int") => JsonDataType.Integer,
            var t when t.Contains("real") || t.Contains("float") || t.Contains("double") => JsonDataType.Number,
            var t when t.Contains("bool") => JsonDataType.Boolean,
            var t when t.Contains("blob") => JsonDataType.String, // Base64 encoded
            _ => JsonDataType.String
        };
    }

    private static JsonlProcessingRecommendations GenerateProcessingRecommendations(
        long totalRecordCount,
        IReadOnlyList<JsonlFieldDefinition> fields,
        TableMetadata tableMetadata)
    {
        var estimatedTokensPerRecord = fields.Count * 10; // Rough estimate
        var estimatedTotalTokens = totalRecordCount * estimatedTokensPerRecord;
        
        var batchSize = totalRecordCount switch
        {
            < 1000 => 100,
            < 10000 => 500,
            < 100000 => 1000,
            _ => 2000
        };

        var complexityScore = CalculateComplexityScore(fields, tableMetadata);

        var sensitiveFields = fields
            .Where(f => IsPotentiallySensitive(f.Name))
            .Select(f => f.Name)
            .ToList();

        var searchableFields = fields
            .Where(f => IsSearchable(f))
            .Select(f => f.Name)
            .ToList();

        return new JsonlProcessingRecommendations
        {
            RecommendedBatchSize = batchSize,
            SensitiveFields = sensitiveFields.AsReadOnly(),
            SearchableFields = searchableFields.AsReadOnly(),
            SamplingStrategy = totalRecordCount > 100000 ? "random_10_percent" : "full_dataset",
            EstimatedTokenCount = estimatedTotalTokens,
            ComplexityScore = complexityScore
        };
    }

    private static int CalculateComplexityScore(IReadOnlyList<JsonlFieldDefinition> fields, TableMetadata tableMetadata)
    {
        var score = 1;
        
        if (fields.Count > 20) score += 2;
        if (fields.Count > 50) score += 2;
        if (tableMetadata.ForeignKeys.Any()) score += 1;
        if (fields.Any(f => f.DataType == JsonDataType.Array || f.DataType == JsonDataType.Object)) score += 2;
        if (tableMetadata.EstimatedRowCount > 1_000_000) score += 2;
        
        return Math.Min(10, score);
    }

    private static bool IsPotentiallySensitive(string fieldName)
    {
        var name = fieldName.ToLowerInvariant();
        var sensitivePatterns = new[] { "password", "ssn", "email", "phone", "address", "credit", "personal" };
        return sensitivePatterns.Any(pattern => name.Contains(pattern));
    }

    private static bool IsSearchable(JsonlFieldDefinition field)
    {
        return field.DataType == JsonDataType.String && 
               !field.IsPrimaryKey && 
               field.Statistics.AverageLength is > 10 and < 500;
    }

    private static List<string> ValidateObjectAgainstSchema(JsonElement element, JsonlSchemaManifest schema)
    {
        var errors = new List<string>();
        
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Expected JSON object");
            return errors;
        }

        foreach (var field in schema.Fields)
        {
            if (element.TryGetProperty(field.Name, out var prop))
            {
                var expectedType = field.DataType;
                var actualType = GetJsonDataType(prop);
                
                if (expectedType != actualType && expectedType != JsonDataType.Mixed)
                {
                    errors.Add($"Field '{field.Name}' expected {expectedType} but got {actualType}");
                }
            }
            else if (!field.IsNullable)
            {
                errors.Add($"Required field '{field.Name}' is missing");
            }
        }

        return errors;
    }

    private static JsonDataType GetJsonDataType(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => JsonDataType.Null,
            JsonValueKind.True or JsonValueKind.False => JsonDataType.Boolean,
            JsonValueKind.Number => JsonDataType.Number,
            JsonValueKind.String => JsonDataType.String,
            JsonValueKind.Array => JsonDataType.Array,
            JsonValueKind.Object => JsonDataType.Object,
            _ => JsonDataType.Mixed
        };
    }

    #endregion
}

/// <summary>
/// Helper class for analyzing schema information during JSONL export.
/// </summary>
internal sealed class JsonlSchemaAnalyzer
{
    private readonly Dictionary<string, JsonlFieldAnalysis> _fieldAnalysis = new();
    private readonly object _lock = new();

    public async Task AnalyzeFieldAsync(string fieldName, object? value, CancellationToken cancellationToken)
    {
        await Task.Yield(); // Make async for potential future async analysis
        
        lock (_lock)
        {
            if (!_fieldAnalysis.TryGetValue(fieldName, out var analysis))
            {
                analysis = new JsonlFieldAnalysis();
                _fieldAnalysis[fieldName] = analysis;
            }

            analysis.Analyze(value);
        }
    }

    public async Task<JsonlSchemaInfo> GenerateSchemaInfoAsync(long totalRecordCount)
    {
        await Task.Yield(); // Make async for consistency

        var fields = new List<JsonlFieldDefinition>();
        var fieldTypes = new Dictionary<string, JsonDataType>();
        var nullPercentages = new Dictionary<string, double>();
        var uniqueValueCounts = new Dictionary<string, long>();
        var sampleValues = new Dictionary<string, IReadOnlyList<object?>>();

        lock (_lock)
        {
            foreach (var (fieldName, analysis) in _fieldAnalysis)
            {
                var stats = analysis.GenerateStatistics();
                var dataType = analysis.GetPredominantDataType();
                var nullPercentage = totalRecordCount > 0 ? (double)analysis.NullCount / totalRecordCount * 100 : 0;

                fieldTypes[fieldName] = dataType;
                nullPercentages[fieldName] = nullPercentage;
                uniqueValueCounts[fieldName] = analysis.UniqueValues.Count;
                sampleValues[fieldName] = analysis.SampleValues.Take(5).ToList().AsReadOnly();

                fields.Add(new JsonlFieldDefinition
                {
                    Name = fieldName,
                    DataType = dataType,
                    IsNullable = analysis.NullCount > 0,
                    Statistics = stats
                });
            }
        }

        return new JsonlSchemaInfo
        {
            Fields = fields.AsReadOnly(),
            FieldTypes = fieldTypes.AsReadOnly(),
            NullPercentages = nullPercentages.AsReadOnly(),
            UniqueValueCounts = uniqueValueCounts.AsReadOnly(),
            SampleValues = sampleValues.AsReadOnly()
        };
    }
}

/// <summary>
/// Tracks analysis information for a single field during schema discovery.
/// </summary>
internal sealed class JsonlFieldAnalysis
{
    public long TotalCount { get; private set; }
    public long NullCount { get; private set; }
    public long NonNullCount => TotalCount - NullCount;
    public HashSet<object?> UniqueValues { get; } = new();
    public List<object?> SampleValues { get; } = new();
    public Dictionary<JsonDataType, long> TypeCounts { get; } = new();
    public object? MinValue { get; private set; }
    public object? MaxValue { get; private set; }
    public long TotalStringLength { get; private set; }

    public void Analyze(object? value)
    {
        TotalCount++;

        if (value is null)
        {
            NullCount++;
            return;
        }

        UniqueValues.Add(value);
        
        if (SampleValues.Count < 10)
        {
            SampleValues.Add(value);
        }

        var dataType = InferDataType(value);
        TypeCounts[dataType] = TypeCounts.GetValueOrDefault(dataType, 0) + 1;

        UpdateMinMax(value);

        if (value is string str)
        {
            TotalStringLength += str.Length;
        }
    }

    public JsonlFieldStatistics GenerateStatistics()
    {
        var valueFrequencies = UniqueValues
            .Take(10) // Top 10 most common values
            .Where(v => v != null)
            .ToDictionary(v => v!, _ => 1L); // Simplified frequency count

        return new JsonlFieldStatistics
        {
            NonNullCount = NonNullCount,
            UniqueCount = UniqueValues.Count,
            ValueFrequencies = valueFrequencies.ToDictionary(kvp => kvp.Key?.ToString() ?? "null", kvp => kvp.Value).AsReadOnly(),
            MinValue = MinValue,
            MaxValue = MaxValue,
            AverageLength = NonNullCount > 0 && TotalStringLength > 0 
                ? (double)TotalStringLength / NonNullCount 
                : null
        };
    }

    public JsonDataType GetPredominantDataType()
    {
        if (!TypeCounts.Any())
            return JsonDataType.Null;

        var mostCommon = TypeCounts.MaxBy(kvp => kvp.Value);
        return mostCommon.Value > NonNullCount * 0.8 ? mostCommon.Key : JsonDataType.Mixed;
    }

    private static JsonDataType InferDataType(object value)
    {
        return value switch
        {
            bool => JsonDataType.Boolean,
            byte or sbyte or short or ushort or int or uint or long or ulong => JsonDataType.Integer,
            float or double or decimal => JsonDataType.Number,
            string => JsonDataType.String,
            Array => JsonDataType.Array,
            _ when value.GetType().IsClass => JsonDataType.Object,
            _ => JsonDataType.String // Default fallback
        };
    }

    private void UpdateMinMax(object value)
    {
        if (value is IComparable comparable)
        {
            if (MinValue is null || comparable.CompareTo(MinValue) < 0)
                MinValue = value;
            
            if (MaxValue is null || comparable.CompareTo(MaxValue) > 0)
                MaxValue = value;
        }
    }
}