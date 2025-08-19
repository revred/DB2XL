using DB2XL.Transformers;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace DB2XL.Configuration;

/// <summary>
/// Executes transformations based on configuration
/// </summary>
public class TransformationPipeline
{
    private readonly TransformationConfig _config;
    private readonly ITransformerRegistry _registry;
    private readonly ILogger? _logger;
    private readonly Dictionary<string, List<ICellTransformer>> _compiledCellTransformers = new();
    private readonly Dictionary<string, List<IRowTransformer>> _compiledRowTransformers = new();
    private int _errorCount = 0;

    public TransformationPipeline(TransformationConfig config, ITransformerRegistry registry, ILogger? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger;
        
        CompileTransformers();
    }

    /// <summary>
    /// Gets the configuration used by this pipeline
    /// </summary>
    public TransformationConfig Configuration => _config;

    /// <summary>
    /// Gets statistics about transformation errors
    /// </summary>
    public int ErrorCount => _errorCount;

    /// <summary>
    /// Checks if transformations are enabled globally
    /// </summary>
    public bool AreTransformationsEnabled => _config.Global.EnableTransformations;

    /// <summary>
    /// Applies cell transformations to a value
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <param name="columnName">Name of the column</param>
    /// <param name="value">Cell value to transform</param>
    /// <param name="context">Cell context information</param>
    /// <returns>Transformed value</returns>
    public string? TransformCell(string tableName, string columnName, string? value, CellContext context)
    {
        if (!_config.Global.EnableTransformations)
            return value;

        // Check if table has transformations disabled
        if (_config.Tables.TryGetValue(tableName, out var tableConfig) && !tableConfig.EnableTransformations)
            return value;

        try
        {
            var transformers = GetCellTransformersForColumn(tableName, columnName, context);
            
            var currentValue = value;
            foreach (var transformer in transformers)
            {
                if (ShouldStopDueToErrors())
                    break;

                try
                {
                    if (transformer.CanApply(context))
                    {
                        currentValue = transformer.Transform(context, currentValue);
                        _logger?.LogTrace("Applied transformer {TransformerType} to {Table}.{Column}", 
                            transformer.GetType().Name, tableName, columnName);
                    }
                }
                catch (Exception ex) when (HandleTransformerError(ex, transformer, tableName, columnName, value))
                {
                    // Error handled, continue or return based on error handling strategy
                    switch (_config.Global.ErrorHandling)
                    {
                        case ErrorHandling.StopOnError:
                            throw;
                        case ErrorHandling.UseOriginalOnError:
                            return value;
                        case ErrorHandling.SkipErrors:
                            continue;
                        case ErrorHandling.LogAndContinue:
                        default:
                            continue;
                    }
                }
            }

            return currentValue;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to transform cell {Table}.{Column}: {Error}", 
                tableName, columnName, ex.Message);
            
            if (_config.Global.ErrorHandling == ErrorHandling.UseOriginalOnError)
                return value;
            throw;
        }
    }

    /// <summary>
    /// Applies row transformations to an entire row
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <param name="row">Row data to transform</param>
    /// <param name="context">Row context information</param>
    /// <returns>Transformed row data</returns>
    public Dictionary<string, string?> TransformRow(string tableName, Dictionary<string, string?> row, DB2XL.Transformers.RowContext context)
    {
        if (!_config.Global.EnableTransformations)
            return row;

        // Check if table has transformations disabled
        if (_config.Tables.TryGetValue(tableName, out var tableConfig) && !tableConfig.EnableTransformations)
            return row;

        try
        {
            var transformers = GetRowTransformersForTable(tableName);
            
            var currentRow = new Dictionary<string, string?>(row);
            foreach (var transformer in transformers)
            {
                if (ShouldStopDueToErrors())
                    break;

                try
                {
                    if (transformer.CanApply(context))
                    {
                        var transformedRow = transformer.Transform(context, currentRow);
                        currentRow = new Dictionary<string, string?>(transformedRow);
                        _logger?.LogTrace("Applied row transformer {TransformerType} to {Table}", 
                            transformer.GetType().Name, tableName);
                    }
                }
                catch (Exception ex) when (HandleRowTransformerError(ex, transformer, tableName))
                {
                    // Error handled based on strategy
                    switch (_config.Global.ErrorHandling)
                    {
                        case ErrorHandling.StopOnError:
                            throw;
                        case ErrorHandling.UseOriginalOnError:
                            return row;
                        case ErrorHandling.SkipErrors:
                            continue;
                        case ErrorHandling.LogAndContinue:
                        default:
                            continue;
                    }
                }
            }

            return currentRow;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to transform row in table {Table}: {Error}", tableName, ex.Message);
            
            if (_config.Global.ErrorHandling == ErrorHandling.UseOriginalOnError)
                return row;
            throw;
        }
    }

    /// <summary>
    /// Gets the filters configured for a table
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <returns>Table filters or null if no filters configured</returns>
    public TableFilters? GetTableFilters(string tableName)
    {
        return _config.Tables.TryGetValue(tableName, out var tableConfig) 
            ? tableConfig.Filters 
            : null;
    }

    /// <summary>
    /// Checks if a column should be excluded from processing
    /// </summary>
    /// <param name="tableName">Name of the table</param>
    /// <param name="columnName">Name of the column</param>
    /// <returns>True if the column should be excluded</returns>
    public bool IsColumnExcluded(string tableName, string columnName)
    {
        var filters = GetTableFilters(tableName);
        if (filters == null) return false;

        // Check explicit exclusion
        if (filters.ExcludeColumns.Contains(columnName))
            return true;

        // Check inclusion list (if specified, only included columns are processed)
        if (filters.IncludeColumns.Count > 0 && !filters.IncludeColumns.Contains(columnName))
            return true;

        return false;
    }

    /// <summary>
    /// Pre-compiles transformers for efficient runtime execution
    /// </summary>
    private void CompileTransformers()
    {
        _logger?.LogInformation("Compiling transformation pipeline...");

        // Compile global cell transformers
        var globalCellTransformers = CreateCellTransformers(_config.GlobalTransformers);
        
        // Compile table-specific transformers
        foreach (var (tableName, tableConfig) in _config.Tables)
        {
            var tableCellTransformers = new Dictionary<string, List<ICellTransformer>>();
            
            foreach (var (columnName, transformerConfigs) in tableConfig.Columns)
            {
                var columnTransformers = CreateCellTransformers(transformerConfigs);
                // Combine global and column-specific transformers
                var allTransformers = globalCellTransformers.Concat(columnTransformers)
                    .OrderBy(t => GetTransformerPriority(t))
                    .ToList();
                
                tableCellTransformers[columnName] = allTransformers;
            }
            
            _compiledCellTransformers[tableName] = tableCellTransformers.SelectMany(kvp => kvp.Value).Distinct().ToList();

            // Compile row transformers
            var rowTransformers = CreateRowTransformers(tableConfig.RowTransformers)
                .OrderBy(t => GetRowTransformerPriority(t))
                .ToList();
            
            _compiledRowTransformers[tableName] = rowTransformers;
        }

        _logger?.LogInformation("Compiled {CellTransformerCount} cell transformers and {RowTransformerCount} row transformers", 
            _compiledCellTransformers.Values.SelectMany(x => x).Count(),
            _compiledRowTransformers.Values.SelectMany(x => x).Count());
    }

    /// <summary>
    /// Creates cell transformers from configuration
    /// </summary>
    private List<ICellTransformer> CreateCellTransformers(List<TransformerConfig> configs)
    {
        var transformers = new List<ICellTransformer>();

        foreach (var config in configs.Where(c => c.Enabled))
        {
            try
            {
                if (_registry.IsRegistered(config.Name))
                {
                    var transformer = _registry.CreateCell(config.Name, config.Config);
                    transformers.Add(transformer);
                    
                    _logger?.LogDebug("Created cell transformer: {Name}", config.Name);
                }
                else
                {
                    _logger?.LogWarning("Unknown cell transformer: {Name}", config.Name);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _errorCount);
                _logger?.LogError(ex, "Failed to create cell transformer {Name}: {Error}", config.Name, ex.Message);
                
                if (_config.Global.ErrorHandling == ErrorHandling.StopOnError)
                    throw;
            }
        }

        return transformers;
    }

    /// <summary>
    /// Creates row transformers from configuration
    /// </summary>
    private List<IRowTransformer> CreateRowTransformers(List<RowTransformerConfig> configs)
    {
        var transformers = new List<IRowTransformer>();

        foreach (var config in configs.Where(c => c.Enabled))
        {
            try
            {
                if (_registry.IsRowRegistered(config.Name))
                {
                    var transformer = _registry.CreateRow(config.Name, config.Config);
                    transformers.Add(transformer);
                    
                    _logger?.LogDebug("Created row transformer: {Name}", config.Name);
                }
                else
                {
                    _logger?.LogWarning("Unknown row transformer: {Name}", config.Name);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _errorCount);
                _logger?.LogError(ex, "Failed to create row transformer {Name}: {Error}", config.Name, ex.Message);
                
                if (_config.Global.ErrorHandling == ErrorHandling.StopOnError)
                    throw;
            }
        }

        return transformers;
    }

    /// <summary>
    /// Gets cell transformers for a specific column
    /// </summary>
    private List<ICellTransformer> GetCellTransformersForColumn(string tableName, string columnName, CellContext context)
    {
        var transformers = new List<ICellTransformer>();

        // Add global transformers that match this column
        foreach (var transformer in CreateCellTransformers(_config.GlobalTransformers))
        {
            if (DoesTransformerApplyToColumn(transformer, columnName, context, null))
            {
                transformers.Add(transformer);
            }
        }

        // Add table-specific transformers
        if (_config.Tables.TryGetValue(tableName, out var tableConfig))
        {
            if (tableConfig.Columns.TryGetValue(columnName, out var columnTransformers))
            {
                transformers.AddRange(CreateCellTransformers(columnTransformers));
            }

            // Check for pattern-based transformers in the table
            foreach (var (pattern, transformerConfigs) in tableConfig.Columns)
            {
                if (pattern != columnName && IsPatternMatch(pattern, columnName))
                {
                    transformers.AddRange(CreateCellTransformers(transformerConfigs));
                }
            }
        }

        return transformers.OrderBy(GetTransformerPriority).ToList();
    }

    /// <summary>
    /// Gets row transformers for a specific table
    /// </summary>
    private List<IRowTransformer> GetRowTransformersForTable(string tableName)
    {
        if (_compiledRowTransformers.TryGetValue(tableName, out var transformers))
        {
            return transformers;
        }

        return new List<IRowTransformer>();
    }

    /// <summary>
    /// Checks if a transformer should apply to a specific column based on conditions
    /// </summary>
    private bool DoesTransformerApplyToColumn(ICellTransformer transformer, string columnName, CellContext context, TransformerConditions? conditions)
    {
        if (conditions == null)
            return true;

        // Check column patterns
        if (conditions.ColumnPatterns.Count > 0)
        {
            var matches = conditions.ColumnPatterns.Any(pattern => IsPatternMatch(pattern, columnName));
            if (!matches) return false;
        }

        // Check excluded columns
        if (conditions.ExcludeColumns.Contains(columnName))
            return false;

        // Check data types
        if (conditions.DataTypes.Count > 0)
        {
            var affinityString = context.Affinity.ToString().ToLowerInvariant();
            if (!conditions.DataTypes.Any(dt => dt.Equals(affinityString, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if a string matches a pattern (supports wildcards)
    /// </summary>
    private bool IsPatternMatch(string pattern, string text)
    {
        if (string.IsNullOrEmpty(pattern))
            return true;

        // Simple wildcard support (* = any characters, ? = single character)
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".") + "$";
        
        return Regex.IsMatch(text, regexPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Gets the priority for a cell transformer
    /// </summary>
    private int GetTransformerPriority(ICellTransformer transformer)
    {
        // Try to extract priority from configuration if possible
        return 100; // Default priority
    }

    /// <summary>
    /// Gets the priority for a row transformer
    /// </summary>
    private int GetRowTransformerPriority(IRowTransformer transformer)
    {
        return 100; // Default priority
    }

    /// <summary>
    /// Handles transformer errors and updates error count
    /// </summary>
    private bool HandleTransformerError(Exception ex, ICellTransformer transformer, string tableName, string columnName, string? value)
    {
        Interlocked.Increment(ref _errorCount);
        
        _logger?.LogError(ex, "Cell transformer {TransformerType} failed on {Table}.{Column} with value '{Value}': {Error}",
            transformer.GetType().Name, tableName, columnName, value ?? "<null>", ex.Message);

        return true; // Always handle the exception (return to using clause)
    }

    /// <summary>
    /// Handles row transformer errors
    /// </summary>
    private bool HandleRowTransformerError(Exception ex, IRowTransformer transformer, string tableName)
    {
        Interlocked.Increment(ref _errorCount);
        
        _logger?.LogError(ex, "Row transformer {TransformerType} failed on table {Table}: {Error}",
            transformer.GetType().Name, tableName, ex.Message);

        return true; // Always handle the exception
    }

    /// <summary>
    /// Checks if processing should stop due to too many errors
    /// </summary>
    private bool ShouldStopDueToErrors()
    {
        return _config.Global.MaxErrors > 0 && _errorCount >= _config.Global.MaxErrors;
    }
}

