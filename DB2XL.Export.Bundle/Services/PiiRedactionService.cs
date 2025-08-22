using DB2XL.Core.Models;
using DB2XL.Core.Services;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DB2XL.Export.Bundle.Services;

/// <summary>
/// Implementation of PII redaction service for privacy protection in database exports.
/// Provides automatic detection and configurable redaction of personally identifiable information.
/// </summary>
public sealed class PiiRedactionService : IPiiRedactionService
{
    private static readonly Dictionary<PiiDataType, List<PiiPattern>> _builtInPatterns = InitializeBuiltInPatterns();
    
    /// <inheritdoc />
    public async Task<PiiAnalysisResult> AnalyzePiiAsync(string connectionString, PiiAnalysisOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var detectedPii = new Dictionary<string, List<PiiColumnDetection>>();
        var stats = new PiiAnalysisStats();

        try
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();

            // Get tables to analyze
            var tablesToAnalyze = await GetTablesToAnalyze(connection, options);
            var columnsAnalyzed = 0;
            var rowsSampled = 0L;
            var piiDetections = 0;
            var typeDistribution = new Dictionary<PiiDataType, int>();
            var methodStats = new Dictionary<PiiDetectionMethod, int>();

            foreach (var tableName in tablesToAnalyze)
            {
                try
                {
                    var tableDetections = await AnalyzeTablePiiAsync(connection, tableName, options);
                    if (tableDetections.Count > 0)
                    {
                        detectedPii[tableName] = tableDetections;
                        piiDetections += tableDetections.Count;

                        // Update statistics
                        foreach (var detection in tableDetections)
                        {
                            typeDistribution[detection.PiiType] = typeDistribution.GetValueOrDefault(detection.PiiType) + 1;
                            methodStats[detection.DetectionMethod] = methodStats.GetValueOrDefault(detection.DetectionMethod) + 1;
                        }
                    }

                    // Count columns and sample rows for this table
                    var tableColumns = await GetTableColumnCount(connection, tableName);
                    columnsAnalyzed += tableColumns;
                    rowsSampled += Math.Min(options.SampleSize, await GetTableRowCount(connection, tableName));
                }
                catch (Exception ex)
                {
                    errors.Add($"Error analyzing table {tableName}: {ex.Message}");
                }
            }

            // Generate recommendations
            var recommendations = GenerateRecommendations(detectedPii);

            stats = new PiiAnalysisStats
            {
                TablesAnalyzed = tablesToAnalyze.Count,
                ColumnsAnalyzed = columnsAnalyzed,
                PiiColumnsDetected = piiDetections,
                RowsSampled = rowsSampled,
                PiiTypeDistribution = typeDistribution,
                DetectionMethodStats = methodStats
            };

            return new PiiAnalysisResult
            {
                IsSuccess = true,
                DetectedPiiColumns = detectedPii.ToDictionary(
                    kvp => kvp.Key, 
                    kvp => (IReadOnlyList<PiiColumnDetection>)kvp.Value),
                Statistics = stats,
                Recommendations = recommendations,
                Errors = errors.AsReadOnly(),
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            errors.Add($"PII analysis failed: {ex.Message}");
            return new PiiAnalysisResult
            {
                IsSuccess = false,
                Statistics = stats,
                Errors = errors.AsReadOnly(),
                Duration = stopwatch.Elapsed
            };
        }
    }

    /// <inheritdoc />
    public async Task<PiiRedactionResult> RedactDataAsync(ExportDataSet data, PiiRedactionConfig config)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        var auditActions = new List<PiiRedactionAction>();

        try
        {
            if (!config.GlobalSettings.Enabled)
            {
                return new PiiRedactionResult
                {
                    IsSuccess = true,
                    RedactedData = data,
                    Duration = stopwatch.Elapsed
                };
            }

            var redactedTables = new Dictionary<string, TableData>();
            var totalRowsProcessed = 0L;
            var totalValuesRedacted = 0L;
            var strategyUsage = new Dictionary<PiiRedactionStrategy, int>();

            foreach (var (tableName, tableData) in data.Tables)
            {
                try
                {
                    var (redactedTable, tableActions) = await RedactTableDataAsync(tableData, config);
                    redactedTables[tableName] = redactedTable;
                    auditActions.AddRange(tableActions);

                    totalRowsProcessed += tableData.Rows.Count;
                    foreach (var action in tableActions)
                    {
                        totalValuesRedacted += action.ValuesRedacted;
                        strategyUsage[action.Strategy] = strategyUsage.GetValueOrDefault(action.Strategy) + 1;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Error redacting table {tableName}: {ex.Message}");
                    redactedTables[tableName] = tableData; // Keep original on error
                }
            }

            var redactedDataSet = new ExportDataSet
            {
                Tables = redactedTables,
                Metadata = data.Metadata
            };

            var auditLog = new PiiRedactionAuditLog
            {
                Statistics = new PiiRedactionStats
                {
                    TablesProcessed = redactedTables.Count,
                    ColumnsRedacted = auditActions.Select(a => $"{a.TableName}.{a.ColumnName}").Distinct().Count(),
                    RowsProcessed = totalRowsProcessed,
                    ValuesRedacted = totalValuesRedacted,
                    StrategyUsage = strategyUsage
                },
                Actions = auditActions.AsReadOnly(),
                Configuration = config
            };

            return new PiiRedactionResult
            {
                IsSuccess = errors.Count == 0,
                RedactedData = redactedDataSet,
                AuditLog = auditLog,
                Errors = errors.AsReadOnly(),
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            errors.Add($"Redaction failed: {ex.Message}");
            return new PiiRedactionResult
            {
                IsSuccess = false,
                Errors = errors.AsReadOnly(),
                Duration = stopwatch.Elapsed
            };
        }
    }

    /// <inheritdoc />
    public PiiRedactionConfig GenerateRedactionConfig(PiiAnalysisResult analysisResult, PiiRedactionPolicy policy)
    {
        var tableRules = new Dictionary<string, PiiTableRedactionRules>();
        var columnRules = new Dictionary<string, PiiColumnRedactionRule>();

        foreach (var (tableName, detections) in analysisResult.DetectedPiiColumns)
        {
            // Create table rule
            tableRules[tableName] = new PiiTableRedactionRules
            {
                TableName = tableName,
                Enabled = true,
                DefaultStrategy = policy.DefaultStrategies.GetValueOrDefault(PiiDataType.Unknown, PiiRedactionStrategy.Mask)
            };

            // Create column rules
            foreach (var detection in detections)
            {
                var strategy = policy.DefaultStrategies.GetValueOrDefault(detection.PiiType, detection.RecommendedStrategy);
                var columnId = $"{tableName}.{detection.ColumnName}";

                columnRules[columnId] = new PiiColumnRedactionRule
                {
                    ColumnIdentifier = columnId,
                    Strategy = strategy,
                    Parameters = GenerateStrategyParameters(strategy, detection)
                };
            }
        }

        return new PiiRedactionConfig
        {
            GlobalSettings = new PiiGlobalSettings
            {
                Enabled = true,
                DefaultStrategy = PiiRedactionStrategy.Mask,
                PreserveFormat = true,
                AuditLevel = PiiAuditLevel.Summary
            },
            TableRules = tableRules,
            ColumnRules = columnRules
        };
    }

    /// <inheritdoc />
    public PiiConfigValidationResult ValidateRedactionConfig(PiiRedactionConfig config)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var recommendations = new List<string>();

        // Validate global settings
        if (config.GlobalSettings.Enabled && string.IsNullOrEmpty(config.GlobalSettings.HashSalt))
        {
            warnings.Add("Using default hash salt may reduce security");
        }

        // Validate column rules
        foreach (var (columnId, rule) in config.ColumnRules)
        {
            if (!columnId.Contains('.'))
            {
                errors.Add($"Invalid column identifier format: {columnId}");
            }

            if (rule.Strategy == PiiRedactionStrategy.Encrypt && string.IsNullOrEmpty(config.GlobalSettings.EncryptionKey))
            {
                errors.Add($"Encryption strategy requires encryption key for column: {columnId}");
            }

            if (rule.Strategy == PiiRedactionStrategy.Substitute && !rule.Parameters.ContainsKey("substitutePattern"))
            {
                warnings.Add($"Substitute strategy without pattern for column: {columnId}");
            }
        }

        // Performance recommendations
        if (config.ColumnRules.Count > 100)
        {
            recommendations.Add("Large number of redaction rules may impact performance");
        }

        return new PiiConfigValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.AsReadOnly(),
            Warnings = warnings.AsReadOnly(),
            Recommendations = recommendations.AsReadOnly()
        };
    }

    private async Task<List<string>> GetTablesToAnalyze(SqliteConnection connection, PiiAnalysisOptions options)
    {
        var allTables = new List<string>();
        
        using var command = new SqliteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'", connection);
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            allTables.Add(reader.GetString(0));
        }

        // Apply filters
        var filteredTables = allTables
            .Where(t => options.IncludeTables == null || options.IncludeTables.Contains(t))
            .Where(t => !options.ExcludeTables.Contains(t))
            .ToList();

        return filteredTables;
    }

    private async Task<List<PiiColumnDetection>> AnalyzeTablePiiAsync(SqliteConnection connection, string tableName, PiiAnalysisOptions options)
    {
        var detections = new List<PiiColumnDetection>();
        
        // Get table schema
        var columns = await GetTableColumns(connection, tableName);
        
        foreach (var column in columns)
        {
            // Check column name patterns
            var nameDetection = AnalyzeColumnName(tableName, column.Name);
            if (nameDetection != null && nameDetection.Confidence >= options.ConfidenceThreshold)
            {
                detections.Add(nameDetection);
                continue;
            }

            // Check data content if enabled
            if (options.EnableContentAnalysis)
            {
                var contentDetection = await AnalyzeColumnContent(connection, tableName, column, options.SampleSize, options.ConfidenceThreshold);
                if (contentDetection != null)
                {
                    detections.Add(contentDetection);
                }
            }
        }

        // Apply custom patterns
        foreach (var pattern in options.CustomPatterns)
        {
            var customDetections = await ApplyCustomPattern(connection, tableName, pattern, options.SampleSize);
            detections.AddRange(customDetections.Where(d => d.Confidence >= options.ConfidenceThreshold));
        }

        return detections;
    }

    private PiiColumnDetection? AnalyzeColumnName(string tableName, string columnName)
    {
        var lowerColumnName = columnName.ToLowerInvariant();
        
        foreach (var (piiType, patterns) in _builtInPatterns)
        {
            foreach (var pattern in patterns.Where(p => p.ApplyToColumnNames))
            {
                if (Regex.IsMatch(lowerColumnName, pattern.Pattern, RegexOptions.IgnoreCase))
                {
                    return new PiiColumnDetection
                    {
                        TableName = tableName,
                        ColumnName = columnName,
                        PiiType = piiType,
                        Confidence = pattern.Confidence,
                        DetectionMethod = PiiDetectionMethod.ColumnNamePattern,
                        RecommendedStrategy = GetDefaultStrategy(piiType)
                    };
                }
            }
        }

        return null;
    }

    private async Task<PiiColumnDetection?> AnalyzeColumnContent(SqliteConnection connection, string tableName, ColumnInfo column, int sampleSize, double threshold)
    {
        var sampleValues = await GetColumnSampleValues(connection, tableName, column.Name, sampleSize);
        
        if (sampleValues.Count == 0)
            return null;

        var detectionCounts = new Dictionary<PiiDataType, int>();
        var totalValues = sampleValues.Count;

        foreach (var value in sampleValues)
        {
            if (value == null) continue;
            
            var stringValue = value.ToString() ?? "";
            
            foreach (var (piiType, patterns) in _builtInPatterns)
            {
                foreach (var pattern in patterns.Where(p => p.ApplyToContent))
                {
                    if (Regex.IsMatch(stringValue, pattern.Pattern, RegexOptions.IgnoreCase))
                    {
                        detectionCounts[piiType] = detectionCounts.GetValueOrDefault(piiType) + 1;
                        break; // Only count once per value
                    }
                }
            }
        }

        // Find the most likely PII type
        var bestMatch = detectionCounts
            .OrderByDescending(kvp => kvp.Value)
            .FirstOrDefault();

        if (bestMatch.Value == 0)
            return null;

        var confidence = (double)bestMatch.Value / totalValues;
        if (confidence < threshold)
            return null;

        return new PiiColumnDetection
        {
            TableName = tableName,
            ColumnName = column.Name,
            PiiType = bestMatch.Key,
            Confidence = confidence,
            DetectionMethod = PiiDetectionMethod.ContentAnalysis,
            SampleValues = sampleValues.Take(3).Select(v => MaskValue(v?.ToString())).ToList(),
            PiiPercentage = confidence * 100,
            RecommendedStrategy = GetDefaultStrategy(bestMatch.Key)
        };
    }

    private async Task<List<object?>> GetColumnSampleValues(SqliteConnection connection, string tableName, string columnName, int sampleSize)
    {
        var values = new List<object?>();
        
        var sql = $"SELECT \"{columnName.Replace("\"", "\"\"")}\" FROM \"{tableName.Replace("\"", "\"\"")}\" WHERE \"{columnName.Replace("\"", "\"\"")}\" IS NOT NULL LIMIT {sampleSize}";
        
        using var command = new SqliteCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetValue(0));
        }
        
        return values;
    }

    private async Task<List<ColumnInfo>> GetTableColumns(SqliteConnection connection, string tableName)
    {
        var columns = new List<ColumnInfo>();
        
        using var command = new SqliteCommand($"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")", connection);
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            columns.Add(new ColumnInfo(
                Name: reader.GetString(1), // name column
                Type: reader.GetString(2), // type column  
                NotNull: reader.GetBoolean(3), // notnull column
                DefaultValue: reader.IsDBNull(4) ? null : reader.GetValue(4), // dflt_value column
                IsPrimaryKey: reader.GetInt32(5) > 0 // pk column
            ));
        }
        
        return columns;
    }

    private async Task<int> GetTableColumnCount(SqliteConnection connection, string tableName)
    {
        using var command = new SqliteCommand($"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")", connection);
        using var reader = await command.ExecuteReaderAsync();
        
        var count = 0;
        while (await reader.ReadAsync())
        {
            count++;
        }
        
        return count;
    }

    private async Task<long> GetTableRowCount(SqliteConnection connection, string tableName)
    {
        using var command = new SqliteCommand($"SELECT COUNT(*) FROM \"{tableName.Replace("\"", "\"\"")}\"", connection);
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt64(result ?? 0);
    }

    private async Task<List<PiiColumnDetection>> ApplyCustomPattern(SqliteConnection connection, string tableName, PiiPattern pattern, int sampleSize)
    {
        var detections = new List<PiiColumnDetection>();
        
        if (pattern.ApplyToColumnNames)
        {
            var columns = await GetTableColumns(connection, tableName);
            foreach (var column in columns)
            {
                if (Regex.IsMatch(column.Name, pattern.Pattern, RegexOptions.IgnoreCase))
                {
                    detections.Add(new PiiColumnDetection
                    {
                        TableName = tableName,
                        ColumnName = column.Name,
                        PiiType = pattern.PiiType,
                        Confidence = pattern.Confidence,
                        DetectionMethod = PiiDetectionMethod.CustomRule,
                        RecommendedStrategy = GetDefaultStrategy(pattern.PiiType)
                    });
                }
            }
        }

        return detections;
    }

    private List<PiiRedactionRecommendation> GenerateRecommendations(Dictionary<string, List<PiiColumnDetection>> detectedPii)
    {
        var recommendations = new List<PiiRedactionRecommendation>();
        
        foreach (var (tableName, detections) in detectedPii)
        {
            foreach (var detection in detections)
            {
                var riskLevel = GetRiskLevel(detection.PiiType);
                var reason = GenerateRecommendationReason(detection);
                
                recommendations.Add(new PiiRedactionRecommendation
                {
                    TableName = tableName,
                    ColumnName = detection.ColumnName,
                    Strategy = detection.RecommendedStrategy,
                    Reason = reason,
                    RiskLevel = riskLevel
                });
            }
        }
        
        return recommendations;
    }

    private async Task<(TableData redactedTable, List<PiiRedactionAction> actions)> RedactTableDataAsync(TableData tableData, PiiRedactionConfig config)
    {
        var actions = new List<PiiRedactionAction>();
        var redactedRows = new List<IReadOnlyDictionary<string, object?>>();

        foreach (var row in tableData.Rows)
        {
            var redactedRow = new Dictionary<string, object?>();
            
            foreach (var (columnName, value) in row)
            {
                var columnId = $"{tableData.Name}.{columnName}";
                
                if (config.ColumnRules.TryGetValue(columnId, out var rule) && rule.Enabled)
                {
                    var redactedValue = await ApplyRedactionStrategy(value, rule, config.GlobalSettings);
                    redactedRow[columnName] = redactedValue;
                    
                    // Record action (simplified for this implementation)
                    var existingAction = actions.FirstOrDefault(a => a.TableName == tableData.Name && a.ColumnName == columnName);
                    if (existingAction == null)
                    {
                        actions.Add(new PiiRedactionAction
                        {
                            TableName = tableData.Name,
                            ColumnName = columnName,
                            Strategy = rule.Strategy,
                            ValuesRedacted = 1,
                            Parameters = rule.Parameters
                        });
                    }
                    else
                    {
                        // Update existing action count (would need mutable structure in real implementation)
                    }
                }
                else
                {
                    redactedRow[columnName] = value;
                }
            }
            
            redactedRows.Add(redactedRow);
        }

        var redactedTable = new TableData
        {
            Name = tableData.Name,
            Columns = tableData.Columns,
            Rows = redactedRows
        };

        return (redactedTable, actions);
    }

    private Task<object?> ApplyRedactionStrategy(object? value, PiiColumnRedactionRule rule, PiiGlobalSettings settings)
    {
        if (value == null)
            return Task.FromResult<object?>(null);

        var stringValue = value.ToString() ?? "";
        
        var result = rule.Strategy switch
        {
            PiiRedactionStrategy.Mask => "***REDACTED***",
            PiiRedactionStrategy.Hash => ComputeHash(stringValue, settings.HashSalt),
            PiiRedactionStrategy.PartialMask => ApplyPartialMask(stringValue),
            PiiRedactionStrategy.Substitute => ApplySubstitution(stringValue, rule.Parameters),
            PiiRedactionStrategy.Remove => null,
            PiiRedactionStrategy.None => value,
            _ => "***REDACTED***"
        };
        
        return Task.FromResult<object?>(result);
    }

    private static string ComputeHash(string value, string salt)
    {
        using var sha256 = SHA256.Create();
        var input = Encoding.UTF8.GetBytes(value + salt);
        var hash = sha256.ComputeHash(input);
        return Convert.ToBase64String(hash)[..12]; // Truncate for readability
    }

    private static string ApplyPartialMask(string value)
    {
        if (value.Length <= 4)
            return "***";
        
        return value[..2] + new string('*', value.Length - 4) + value[^2..];
    }

    private static string ApplySubstitution(string value, IReadOnlyDictionary<string, object> parameters)
    {
        if (parameters.TryGetValue("substitutePattern", out var pattern))
        {
            return pattern.ToString() ?? "***SUBSTITUTE***";
        }
        
        return "***SUBSTITUTE***";
    }

    private static string MaskValue(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= 2)
            return "***";
        
        return value[..1] + "***" + value[^1..];
    }

    private static PiiRedactionStrategy GetDefaultStrategy(PiiDataType piiType)
    {
        return piiType switch
        {
            PiiDataType.Email => PiiRedactionStrategy.PartialMask,
            PiiDataType.PhoneNumber => PiiRedactionStrategy.PartialMask,
            PiiDataType.SocialSecurityNumber => PiiRedactionStrategy.Hash,
            PiiDataType.CreditCardNumber => PiiRedactionStrategy.Mask,
            PiiDataType.PersonName => PiiRedactionStrategy.Substitute,
            PiiDataType.Address => PiiRedactionStrategy.Mask,
            _ => PiiRedactionStrategy.Mask
        };
    }

    private static PiiRiskLevel GetRiskLevel(PiiDataType piiType)
    {
        return piiType switch
        {
            PiiDataType.SocialSecurityNumber => PiiRiskLevel.Critical,
            PiiDataType.CreditCardNumber => PiiRiskLevel.Critical,
            PiiDataType.BankAccountNumber => PiiRiskLevel.Critical,
            PiiDataType.Email => PiiRiskLevel.High,
            PiiDataType.PhoneNumber => PiiRiskLevel.High,
            PiiDataType.PersonName => PiiRiskLevel.Medium,
            PiiDataType.Address => PiiRiskLevel.Medium,
            _ => PiiRiskLevel.Low
        };
    }

    private static string GenerateRecommendationReason(PiiColumnDetection detection)
    {
        return $"Detected {detection.PiiType} with {detection.Confidence:P1} confidence using {detection.DetectionMethod}";
    }

    private static IReadOnlyDictionary<string, object> GenerateStrategyParameters(PiiRedactionStrategy strategy, PiiColumnDetection detection)
    {
        var parameters = new Dictionary<string, object>();
        
        if (strategy == PiiRedactionStrategy.Substitute)
        {
            parameters["substitutePattern"] = GenerateSubstitutePattern(detection.PiiType);
        }
        
        return parameters;
    }

    private static string GenerateSubstitutePattern(PiiDataType piiType)
    {
        return piiType switch
        {
            PiiDataType.Email => "user@example.com",
            PiiDataType.PhoneNumber => "(555) 123-4567",
            PiiDataType.PersonName => "John Doe",
            PiiDataType.Address => "123 Main St, Anytown, ST 12345",
            _ => "***SUBSTITUTE***"
        };
    }

    private static Dictionary<PiiDataType, List<PiiPattern>> InitializeBuiltInPatterns()
    {
        return new Dictionary<PiiDataType, List<PiiPattern>>
        {
            [PiiDataType.Email] = new List<PiiPattern>
            {
                new() { Name = "email_column", Pattern = @"email|e_mail|mail", PiiType = PiiDataType.Email, Confidence = 0.9 },
                new() { Name = "email_content", Pattern = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", PiiType = PiiDataType.Email, Confidence = 0.95, ApplyToColumnNames = false }
            },
            [PiiDataType.PhoneNumber] = new List<PiiPattern>
            {
                new() { Name = "phone_column", Pattern = @"phone|tel|mobile|cell", PiiType = PiiDataType.PhoneNumber, Confidence = 0.8 },
                new() { Name = "phone_content", Pattern = @"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b", PiiType = PiiDataType.PhoneNumber, Confidence = 0.85, ApplyToColumnNames = false }
            },
            [PiiDataType.SocialSecurityNumber] = new List<PiiPattern>
            {
                new() { Name = "ssn_column", Pattern = @"ssn|social|security", PiiType = PiiDataType.SocialSecurityNumber, Confidence = 0.95 },
                new() { Name = "ssn_content", Pattern = @"\b\d{3}-?\d{2}-?\d{4}\b", PiiType = PiiDataType.SocialSecurityNumber, Confidence = 0.9, ApplyToColumnNames = false }
            },
            [PiiDataType.CreditCardNumber] = new List<PiiPattern>
            {
                new() { Name = "cc_column", Pattern = @"credit|card|cc_num|ccnum", PiiType = PiiDataType.CreditCardNumber, Confidence = 0.9 },
                new() { Name = "cc_content", Pattern = @"\b\d{4}[-\s]?\d{4}[-\s]?\d{4}[-\s]?\d{4}\b", PiiType = PiiDataType.CreditCardNumber, Confidence = 0.8, ApplyToColumnNames = false }
            },
            [PiiDataType.PersonName] = new List<PiiPattern>
            {
                new() { Name = "name_column", Pattern = @"name|first|last|fname|lname|full_name", PiiType = PiiDataType.PersonName, Confidence = 0.7 }
            },
            [PiiDataType.Address] = new List<PiiPattern>
            {
                new() { Name = "address_column", Pattern = @"address|addr|street|location", PiiType = PiiDataType.Address, Confidence = 0.8 }
            },
            [PiiDataType.PostalCode] = new List<PiiPattern>
            {
                new() { Name = "zip_column", Pattern = @"zip|postal|post_code|zipcode", PiiType = PiiDataType.PostalCode, Confidence = 0.9 },
                new() { Name = "zip_content", Pattern = @"\b\d{5}(-\d{4})?\b", PiiType = PiiDataType.PostalCode, Confidence = 0.8, ApplyToColumnNames = false }
            },
            [PiiDataType.DateOfBirth] = new List<PiiPattern>
            {
                new() { Name = "dob_column", Pattern = @"birth|dob|born|birthday", PiiType = PiiDataType.DateOfBirth, Confidence = 0.9 }
            },
            [PiiDataType.IpAddress] = new List<PiiPattern>
            {
                new() { Name = "ip_column", Pattern = @"ip|ip_addr|ipaddress", PiiType = PiiDataType.IpAddress, Confidence = 0.9 },
                new() { Name = "ip_content", Pattern = @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", PiiType = PiiDataType.IpAddress, Confidence = 0.8, ApplyToColumnNames = false }
            }
        };
    }
}