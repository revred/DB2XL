using DB2XL.Core.Models;
using DB2XL.Core.Services;
using DB2XL.Data.Schema;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Globalization;
using System.Text;

namespace DB2XL.Export.Bundle.Services;

/// <summary>
/// Production implementation of SQLite data extraction service.
/// Provides streaming access to table data with deterministic ordering and schema analysis.
/// </summary>
public sealed class SqliteDataExtractor : ISqliteDataExtractor
{
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(300);

    /// <summary>
    /// Extracts all rows from a table as streaming async enumerable.
    /// Uses memory-efficient streaming with deterministic ordering.
    /// </summary>
    public async IAsyncEnumerable<IReadOnlyDictionary<string, object?>> ExtractTableDataAsync(
        string connectionString,
        string tableName,
        ExtractionOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        ArgumentNullException.ThrowIfNull(options);

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var query = await BuildSelectQueryAsync(connection, tableName, options, cancellationToken);
        
        using var command = connection.CreateCommand();
        command.CommandText = query;
        command.CommandTimeout = options.CommandTimeoutSeconds;

        using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        
        var columns = GetColumnInfo(reader);
        long rowCount = 0;

        while (await reader.ReadAsync(cancellationToken))
        {
            if (options.MaxRows > 0 && rowCount >= options.MaxRows)
                break;

            yield return ReadRowData(reader, columns, options);
            rowCount++;
        }
    }

    /// <summary>
    /// Extracts table data in batches for memory-efficient bulk processing.
    /// </summary>
    public async IAsyncEnumerable<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExtractTableBatchesAsync(
        string connectionString,
        string tableName,
        ExtractionOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var batch = new List<IReadOnlyDictionary<string, object?>>(options.BatchSize);

        await foreach (var row in ExtractTableDataAsync(connectionString, tableName, options, cancellationToken))
        {
            batch.Add(row);

            if (batch.Count >= options.BatchSize)
            {
                yield return batch.AsReadOnly();
                batch.Clear();
            }
        }

        // Yield final partial batch if any rows remain
        if (batch.Count > 0)
        {
            yield return batch.AsReadOnly();
        }
    }

    /// <summary>
    /// Analyzes table structure and generates comprehensive metadata.
    /// </summary>
    public async Task<TableMetadata> AnalyzeTableAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var columns = await GetTableColumnsAsync(connection, tableName, cancellationToken);
        var primaryKeys = await GetPrimaryKeyColumnsAsync(connection, tableName, cancellationToken);
        var foreignKeys = await GetForeignKeysAsync(connection, tableName, cancellationToken);
        var indexes = await GetTableIndexesAsync(connection, tableName, cancellationToken);
        var createSql = await GetCreateSqlAsync(connection, tableName, cancellationToken);
        var rowCount = await EstimateRowCountInternalAsync(connection, tableName, cancellationToken);
        var isWithoutRowId = await IsWithoutRowIdTableAsync(connection, tableName, cancellationToken);

        var recommendedOrderBy = BuildRecommendedOrderBy(primaryKeys, isWithoutRowId);
        var dataQualityWarnings = AnalyzeDataQuality(columns, primaryKeys, indexes);

        return new TableMetadata
        {
            TableName = tableName,
            TableType = await GetTableTypeAsync(connection, tableName, cancellationToken),
            Columns = columns,
            PrimaryKeyColumns = primaryKeys,
            EstimatedRowCount = rowCount,
            HasRowId = !isWithoutRowId,
            IsWithoutRowId = isWithoutRowId,
            ForeignKeys = foreignKeys,
            Indexes = indexes,
            CreateSql = createSql,
            RecommendedOrderBy = recommendedOrderBy,
            DataQualityWarnings = dataQualityWarnings
        };
    }

    /// <summary>
    /// Gets list of tables and views from database with optional filtering.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetTablesAsync(
        string connectionString,
        bool includeViews = false,
        string? tableFilter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var typeCondition = includeViews ? "('table', 'view')" : "('table')";
        var filterCondition = string.IsNullOrEmpty(tableFilter) ? "" : " AND name LIKE @filter";

        var query = $"""
            SELECT name 
            FROM sqlite_master 
            WHERE type IN {typeCondition}
              AND name NOT LIKE 'sqlite_%'
              {filterCondition}
            ORDER BY name
            """;

        using var command = connection.CreateCommand();
        command.CommandText = query;
        
        if (!string.IsNullOrEmpty(tableFilter))
        {
            command.Parameters.AddWithValue("@filter", tableFilter);
        }

        var tables = new List<string>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables.AsReadOnly();
    }

    /// <summary>
    /// Estimates row count using efficient SQLite statistics.
    /// </summary>
    public async Task<long> EstimateRowCountAsync(
        string connectionString,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return await EstimateRowCountInternalAsync(connection, tableName, cancellationToken);
    }

    /// <summary>
    /// Validates database connection and read permissions.
    /// </summary>
    public async Task<bool> ValidateConnectionAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            // Test basic read access
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master";
            await command.ExecuteScalarAsync(cancellationToken);

            return true;
        }
        catch
        {
            return false;
        }
    }

    #region Private Helper Methods

    private async Task<string> BuildSelectQueryAsync(
        SqliteConnection connection,
        string tableName,
        ExtractionOptions options,
        CancellationToken cancellationToken)
    {
        var query = new StringBuilder("SELECT ");

        // Build column list
        var columns = await GetSelectableColumnsAsync(connection, tableName, options, cancellationToken);
        query.Append(string.Join(", ", columns.Select(QuoteIdentifier)));

        query.Append(" FROM ").Append(QuoteIdentifier(tableName));

        // Add WHERE clause
        if (!string.IsNullOrWhiteSpace(options.WhereClause))
        {
            query.Append(" WHERE ").Append(options.WhereClause);
        }

        // Add ORDER BY clause
        if (!string.IsNullOrWhiteSpace(options.CustomOrderBy))
        {
            query.Append(" ORDER BY ").Append(options.CustomOrderBy);
        }
        else if (options.DeterministicOrdering)
        {
            var orderBy = await GetDeterministicOrderByAsync(connection, tableName, cancellationToken);
            if (!string.IsNullOrEmpty(orderBy))
            {
                query.Append(" ORDER BY ").Append(orderBy);
            }
        }

        // Add LIMIT clause
        if (options.MaxRows > 0)
        {
            query.Append(" LIMIT ").Append(options.MaxRows);
        }

        return query.ToString();
    }

    private async Task<IReadOnlyList<string>> GetSelectableColumnsAsync(
        SqliteConnection connection,
        string tableName,
        ExtractionOptions options,
        CancellationToken cancellationToken)
    {
        var allColumns = await GetTableColumnsAsync(connection, tableName, cancellationToken);
        var columnNames = allColumns.Select(c => c.Name).ToList();

        // Apply include filter
        if (options.IncludeColumns?.Count > 0)
        {
            columnNames = columnNames.Intersect(options.IncludeColumns, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Apply exclude filter
        if (options.ExcludeColumns?.Count > 0)
        {
            columnNames = columnNames.Except(options.ExcludeColumns, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Handle BLOB columns
        if (options.BlobMode == BlobHandlingMode.Skip)
        {
            var blobColumns = allColumns.Where(c => c.IsBlobColumn).Select(c => c.Name);
            columnNames = columnNames.Except(blobColumns, StringComparer.OrdinalIgnoreCase).ToList();
        }

        return columnNames.AsReadOnly();
    }

    private async Task<string> GetDeterministicOrderByAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var primaryKeys = await GetPrimaryKeyColumnsAsync(connection, tableName, cancellationToken);
        
        if (primaryKeys.Count > 0)
        {
            return string.Join(", ", primaryKeys.Select(pk => $"{QuoteIdentifier(pk)} ASC"));
        }

        var isWithoutRowId = await IsWithoutRowIdTableAsync(connection, tableName, cancellationToken);
        if (!isWithoutRowId)
        {
            return "rowid ASC";
        }

        return string.Empty; // No deterministic ordering available
    }

    private static IReadOnlyDictionary<string, object?> ReadRowData(
        SqliteDataReader reader,
        IReadOnlyList<string> columns,
        ExtractionOptions options)
    {
        var row = new Dictionary<string, object?>(columns.Count);

        for (int i = 0; i < columns.Count; i++)
        {
            var columnName = columns[i];
            var value = reader.IsDBNull(i) ? null : ReadColumnValue(reader, i, options);
            row[columnName] = value;
        }

        return row;
    }

    private static object? ReadColumnValue(SqliteDataReader reader, int columnIndex, ExtractionOptions options)
    {
        var fieldType = reader.GetFieldType(columnIndex);

        return fieldType.Name switch
        {
            "Int64" => reader.GetInt64(columnIndex),
            "Double" => reader.GetDouble(columnIndex),
            "String" => reader.GetString(columnIndex),
            "Byte[]" => HandleBlobValue(reader, columnIndex, options),
            "Boolean" => reader.GetBoolean(columnIndex),
            "DateTime" => reader.GetDateTime(columnIndex),
            _ => reader.GetValue(columnIndex)
        };
    }

    private static object? HandleBlobValue(SqliteDataReader reader, int columnIndex, ExtractionOptions options)
    {
        return options.BlobMode switch
        {
            BlobHandlingMode.Skip or BlobHandlingMode.AsNull => null,
            BlobHandlingMode.SizeOnly => GetBlobSize(reader, columnIndex),
            BlobHandlingMode.Include => reader.GetValue(columnIndex),
            _ => reader.GetValue(columnIndex)
        };
    }

    private static long GetBlobSize(SqliteDataReader reader, int columnIndex)
    {
        var bytes = reader.GetValue(columnIndex) as byte[];
        return bytes?.Length ?? 0;
    }

    private static IReadOnlyList<string> GetColumnInfo(SqliteDataReader reader)
    {
        var columns = new List<string>(reader.FieldCount);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(reader.GetName(i));
        }
        return columns.AsReadOnly();
    }

    private async Task<IReadOnlyList<ColumnMetadata>> GetTableColumnsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        // Note: PRAGMA statements do not support parameterized queries in SQLite
        var quotedTableName = "\"" + tableName.Replace("\"", "\"\"") + "\"";
        var query = $"PRAGMA table_info({quotedTableName})";
        using var command = connection.CreateCommand();
        command.CommandText = query;

        var columns = new List<ColumnMetadata>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var column = new ColumnMetadata
            {
                OrdinalPosition = reader.GetInt32("cid"),
                Name = reader.GetString("name"),
                DeclaredType = reader.GetString("type"),
                TypeAffinity = DetermineTypeAffinity(reader.GetString("type")),
                IsNullable = reader.GetInt32("notnull") == 0,
                DefaultValue = reader.IsDBNull("dflt_value") ? null : reader.GetString("dflt_value"),
                IsPrimaryKey = reader.GetInt32("pk") > 0,
                PrimaryKeyPosition = reader.GetInt32("pk"),
                IsBlobColumn = IsBlobType(reader.GetString("type")),
                IsAutoIncrement = await IsAutoIncrementColumnAsync(connection, tableName, reader.GetString("name"), cancellationToken)
            };

            columns.Add(column);
        }

        return columns.AsReadOnly();
    }

    private async Task<IReadOnlyList<string>> GetPrimaryKeyColumnsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var columns = await GetTableColumnsAsync(connection, tableName, cancellationToken);
        return columns
            .Where(c => c.IsPrimaryKey)
            .OrderBy(c => c.PrimaryKeyPosition)
            .Select(c => c.Name)
            .ToList()
            .AsReadOnly();
    }

    private async Task<IReadOnlyList<ForeignKeyInfo>> GetForeignKeysAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        // Note: PRAGMA statements do not support parameterized queries in SQLite
        var quotedTableName = "\"" + tableName.Replace("\"", "\"\"") + "\"";
        var query = $"PRAGMA foreign_key_list({quotedTableName})";
        using var command = connection.CreateCommand();
        command.CommandText = query;

        var foreignKeys = new List<ForeignKeyInfo>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var fk = new ForeignKeyInfo
            {
                ColumnName = reader.GetString("from"),
                ReferencedTable = reader.GetString("table"),
                ReferencedColumn = reader.GetString("to"),
                ConstraintName = $"FK_{tableName}_{reader.GetString("from")}"
            };

            foreignKeys.Add(fk);
        }

        return foreignKeys.AsReadOnly();
    }

    private async Task<IReadOnlyList<Core.Models.IndexInfo>> GetTableIndexesAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        // Note: PRAGMA statements do not support parameterized queries in SQLite
        var quotedTableName = "\"" + tableName.Replace("\"", "\"\"") + "\"";
        var query = $"PRAGMA index_list({quotedTableName})";
        using var command = connection.CreateCommand();
        command.CommandText = query;

        var indexes = new List<Core.Models.IndexInfo>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var indexName = reader.GetString("name");
            var isUnique = reader.GetBoolean("unique");
            var isAutoGenerated = reader.GetBoolean("partial");

            var indexColumns = await GetIndexColumnsAsync(connection, indexName, cancellationToken);

            var index = new Core.Models.IndexInfo
            {
                Name = indexName,
                TableName = tableName,
                IsUnique = isUnique,
                Columns = indexColumns
            };

            indexes.Add(index);
        }

        return indexes.AsReadOnly();
    }

    private async Task<IReadOnlyList<string>> GetIndexColumnsAsync(
        SqliteConnection connection,
        string indexName,
        CancellationToken cancellationToken)
    {
        // Note: PRAGMA statements do not support parameterized queries in SQLite
        var quotedIndexName = "\"" + indexName.Replace("\"", "\"\"") + "\"";
        var query = $"PRAGMA index_info({quotedIndexName})";
        using var command = connection.CreateCommand();
        command.CommandText = query;

        var columns = new List<string>();
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull("name"))
            {
                columns.Add(reader.GetString("name"));
            }
        }

        return columns.AsReadOnly();
    }

    private async Task<string> GetCreateSqlAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var query = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = @tableName";
        using var command = connection.CreateCommand();
        command.CommandText = query;
        command.Parameters.AddWithValue("@tableName", tableName);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result?.ToString() ?? string.Empty;
    }

    private async Task<string> GetTableTypeAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var query = "SELECT type FROM sqlite_master WHERE name = @tableName";
        using var command = connection.CreateCommand();
        command.CommandText = query;
        command.Parameters.AddWithValue("@tableName", tableName);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result?.ToString() ?? "table";
    }

    private async Task<long> EstimateRowCountInternalAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        try
        {
            // Try ANALYZE statistics first (fast)
            var analyzeQuery = "SELECT stat FROM sqlite_stat1 WHERE tbl = @tableName LIMIT 1";
            using var analyzeCommand = connection.CreateCommand();
            analyzeCommand.CommandText = analyzeQuery;
            analyzeCommand.Parameters.AddWithValue("@tableName", tableName);

            var statResult = await analyzeCommand.ExecuteScalarAsync(cancellationToken);
            if (statResult != null && long.TryParse(statResult.ToString()?.Split(' ')[0], out var analyzedCount))
            {
                return analyzedCount;
            }

            // Fallback to COUNT(*) (slower but accurate)
            var countQuery = $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)}";
            using var countCommand = connection.CreateCommand();
            countCommand.CommandText = countQuery;

            var countResult = await countCommand.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(countResult);
        }
        catch
        {
            return 0; // Return 0 if unable to determine count
        }
    }

    private async Task<bool> IsWithoutRowIdTableAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        var createSql = await GetCreateSqlAsync(connection, tableName, cancellationToken);
        return createSql.Contains("WITHOUT ROWID", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> IsAutoIncrementColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        var createSql = await GetCreateSqlAsync(connection, tableName, cancellationToken);
        var pattern = $@"\b{columnName}\b.*\bAUTOINCREMENT\b";
        return System.Text.RegularExpressions.Regex.IsMatch(createSql, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string BuildRecommendedOrderBy(IReadOnlyList<string> primaryKeys, bool isWithoutRowId)
    {
        if (primaryKeys.Count > 0)
        {
            return string.Join(", ", primaryKeys.Select(pk => $"{QuoteIdentifier(pk)} ASC"));
        }

        return isWithoutRowId ? string.Empty : "rowid ASC";
    }

    private static IReadOnlyList<string> AnalyzeDataQuality(
        IReadOnlyList<ColumnMetadata> columns,
        IReadOnlyList<string> primaryKeys,
        IReadOnlyList<Core.Models.IndexInfo> indexes)
    {
        var warnings = new List<string>();

        if (primaryKeys.Count == 0)
        {
            warnings.Add("Table has no primary key - export ordering may not be deterministic");
        }

        var blobColumns = columns.Where(c => c.IsBlobColumn).ToList();
        if (blobColumns.Count > 0)
        {
            warnings.Add($"Table contains {blobColumns.Count} BLOB column(s) - may impact export performance");
        }

        var nullableColumns = columns.Where(c => c.IsNullable && !c.IsPrimaryKey).ToList();
        if (nullableColumns.Count == columns.Count - primaryKeys.Count)
        {
            warnings.Add("Most columns allow NULL values - verify data quality requirements");
        }

        return warnings.AsReadOnly();
    }

    private static string DetermineTypeAffinity(string declaredType)
    {
        if (string.IsNullOrEmpty(declaredType))
            return "BLOB";

        var type = declaredType.ToUpperInvariant();

        if (type.Contains("INT"))
            return "INTEGER";
        if (type.Contains("CHAR") || type.Contains("CLOB") || type.Contains("TEXT"))
            return "TEXT";
        if (type.Contains("BLOB") || string.IsNullOrEmpty(type))
            return "BLOB";
        if (type.Contains("REAL") || type.Contains("FLOA") || type.Contains("DOUB"))
            return "REAL";

        return "NUMERIC";
    }

    private static bool IsBlobType(string declaredType)
    {
        return declaredType.ToUpperInvariant().Contains("BLOB");
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    #endregion
}