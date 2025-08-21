using DB2XL.Core.Exceptions;
using System.Security.Cryptography;

namespace DB2XL.Core.Services;

/// <summary>
/// Service for calculating deterministic hashes of bundle export files and data.
/// Provides both file-based and streaming hash calculation with multiple algorithms.
/// Essential for bundle integrity verification and data provenance tracking.
/// </summary>
public interface IBundleHashCalculator
{
    /// <summary>
    /// Calculates SHA-256 hash of a file.
    /// Uses buffered reading for memory efficiency with large files.
    /// </summary>
    /// <param name="filePath">Absolute path to file to hash</param>
    /// <param name="cancellationToken">Cancellation token for long-running operations</param>
    /// <returns>Hexadecimal hash string (uppercase, no prefix)</returns>
    /// <exception cref="BundleHashException">When file access or hashing fails</exception>
    Task<string> CalculateFileHashAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates hash of streaming data without loading into memory.
    /// Useful for hashing data as it's being generated or read.
    /// </summary>
    /// <param name="dataStream">Stream containing data to hash</param>
    /// <param name="algorithm">Hash algorithm to use</param>
    /// <param name="cancellationToken">Cancellation token for long-running operations</param>
    /// <returns>Hexadecimal hash string (uppercase, no prefix)</returns>
    /// <exception cref="BundleHashException">When stream access or hashing fails</exception>
    Task<string> CalculateStreamHashAsync(
        Stream dataStream, 
        HashAlgorithm algorithm, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates deterministic hash of structured data (e.g., table rows).
    /// Uses consistent serialization format for reproducible hashes.
    /// </summary>
    /// <param name="data">Data rows to hash</param>
    /// <param name="tableName">Name of table (included in hash context)</param>
    /// <param name="cancellationToken">Cancellation token for processing</param>
    /// <returns>Hexadecimal hash string representing the data content</returns>
    /// <exception cref="BundleHashException">When data serialization or hashing fails</exception>
    Task<string> CalculateDataHashAsync(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> data,
        string tableName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates hash of a string using deterministic UTF-8 encoding.
    /// Suitable for configuration hashing and small data verification.
    /// </summary>
    /// <param name="content">String content to hash</param>
    /// <param name="algorithm">Hash algorithm to use</param>
    /// <returns>Hexadecimal hash string (uppercase, no prefix)</returns>
    string CalculateStringHash(string content, HashAlgorithm? algorithm = null);

    /// <summary>
    /// Verifies file hash matches expected value.
    /// Provides detailed error information for hash mismatches.
    /// </summary>
    /// <param name="filePath">Path to file to verify</param>
    /// <param name="expectedHash">Expected hash value (case-insensitive)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if hash matches, false otherwise</returns>
    /// <exception cref="BundleHashException">When file access fails</exception>
    Task<bool> VerifyFileHashAsync(
        string filePath, 
        string expectedHash, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates hashes for multiple files in parallel.
    /// Optimizes I/O throughput while managing memory usage.
    /// </summary>
    /// <param name="filePaths">Collection of file paths to hash</param>
    /// <param name="maxConcurrency">Maximum number of files to hash concurrently</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping file paths to their hash values</returns>
    Task<IReadOnlyDictionary<string, string>> CalculateMultipleFileHashesAsync(
        IEnumerable<string> filePaths,
        int maxConcurrency = 4,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Production implementation of bundle hash calculation service.
/// Optimized for performance, memory efficiency, and deterministic output.
/// </summary>
public sealed class BundleHashCalculator : IBundleHashCalculator, IDisposable
{
    private const int BufferSize = 81920; // 80KB buffer for file reading
    private const string DefaultAlgorithm = "SHA256";
    
    private readonly SemaphoreSlim _concurrencySemaphore;
    private bool _disposed;

    public BundleHashCalculator(int maxConcurrentOperations = 4)
    {
        _concurrencySemaphore = new SemaphoreSlim(maxConcurrentOperations, maxConcurrentOperations);
    }

    /// <inheritdoc />
    public async Task<string> CalculateFileHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ValidateFilePath(filePath);
        
        await _concurrencySemaphore.WaitAsync(cancellationToken);
        try
        {
            using var sha256 = SHA256.Create();
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
            
            return await CalculateStreamHashAsync(fileStream, sha256, cancellationToken);
        }
        catch (Exception ex) when (!(ex is BundleHashException))
        {
            throw new BundleHashException(
                $"Failed to calculate hash for file: {ex.Message}",
                filePath,
                DefaultAlgorithm,
                innerException: ex);
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string> CalculateStreamHashAsync(
        Stream dataStream, 
        HashAlgorithm algorithm, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataStream);
        ArgumentNullException.ThrowIfNull(algorithm);

        if (!dataStream.CanRead)
            throw new BundleHashException("Stream is not readable", string.Empty, algorithm.GetType().Name);

        try
        {
            var buffer = new byte[BufferSize];
            int bytesRead;
            
            while ((bytesRead = await dataStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                algorithm.TransformBlock(buffer, 0, bytesRead, null, 0);
            }
            
            algorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            
            return Convert.ToHexString(algorithm.Hash!);
        }
        catch (Exception ex) when (!(ex is BundleHashException))
        {
            throw new BundleHashException(
                $"Failed to calculate stream hash: {ex.Message}",
                string.Empty,
                algorithm.GetType().Name,
                innerException: ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> CalculateDataHashAsync(
        IAsyncEnumerable<IReadOnlyDictionary<string, object?>> data,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        using var sha256 = SHA256.Create();
        
        try
        {
            var hasher = new DeterministicDataHasher(sha256);
            hasher.Initialize(tableName);
            
            await foreach (var row in data.WithCancellation(cancellationToken))
            {
                hasher.ProcessRow(row);
            }
            
            return hasher.Finalize();
        }
        catch (Exception ex) when (!(ex is BundleHashException))
        {
            throw new BundleHashException(
                $"Failed to calculate data hash for table '{tableName}': {ex.Message}",
                tableName,
                DefaultAlgorithm,
                innerException: ex);
        }
    }

    /// <inheritdoc />
    public string CalculateStringHash(string content, HashAlgorithm? algorithm = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        
        using var hasher = algorithm ?? SHA256.Create();
        var contentBytes = System.Text.Encoding.UTF8.GetBytes(content);
        var hashBytes = hasher.ComputeHash(contentBytes);
        
        return Convert.ToHexString(hashBytes);
    }

    /// <inheritdoc />
    public async Task<bool> VerifyFileHashAsync(
        string filePath, 
        string expectedHash, 
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);
        
        try
        {
            var actualHash = await CalculateFileHashAsync(filePath, cancellationToken);
            return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (BundleHashException ex)
        {
            // Re-throw with verification context
            throw new BundleHashException(
                $"Hash verification failed: {ex.Message}",
                filePath,
                DefaultAlgorithm,
                expectedHash,
                null,
                ex);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> CalculateMultipleFileHashesAsync(
        IEnumerable<string> filePaths,
        int maxConcurrency = 4,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        
        var filePathList = filePaths.ToList();
        if (filePathList.Count == 0)
            return new Dictionary<string, string>();

        var results = new Dictionary<string, string>();
        var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        
        var tasks = filePathList.Select(async filePath =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var hash = await CalculateFileHashAsync(filePath, cancellationToken);
                lock (results)
                {
                    results[filePath] = hash;
                }
            }
            finally
            {
                semaphore.Release();
            }
        });
        
        await Task.WhenAll(tasks);
        return results;
    }

    private static void ValidateFilePath(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        
        if (!File.Exists(filePath))
        {
            throw new BundleHashException(
                $"File not found: {filePath}",
                filePath,
                DefaultAlgorithm);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _concurrencySemaphore?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Helper class for calculating deterministic hashes of structured data rows.
/// Ensures consistent serialization format for reproducible hashes across runs.
/// </summary>
internal sealed class DeterministicDataHasher
{
    private readonly HashAlgorithm _algorithm;
    private const byte NullMarker = 0x00;
    private const byte FieldSeparator = 0x1F;  // Unit Separator
    private const byte RecordSeparator = 0x1E; // Record Separator
    
    public DeterministicDataHasher(HashAlgorithm algorithm)
    {
        _algorithm = algorithm ?? throw new ArgumentNullException(nameof(algorithm));
    }
    
    public void Initialize(string tableName)
    {
        // Include table name in hash context for uniqueness
        var tableNameBytes = System.Text.Encoding.UTF8.GetBytes(tableName);
        _algorithm.TransformBlock(tableNameBytes, 0, tableNameBytes.Length, null, 0);
        _algorithm.TransformBlock(new[] { RecordSeparator }, 0, 1, null, 0);
    }
    
    public void ProcessRow(IReadOnlyDictionary<string, object?> row)
    {
        // Sort columns by name for deterministic ordering
        var sortedColumns = row.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        
        for (int i = 0; i < sortedColumns.Length; i++)
        {
            if (i > 0)
            {
                // Add field separator between columns
                _algorithm.TransformBlock(new[] { FieldSeparator }, 0, 1, null, 0);
            }
            
            var columnName = sortedColumns[i];
            var value = row[columnName];
            
            if (value == null || value == DBNull.Value)
            {
                _algorithm.TransformBlock(new[] { NullMarker }, 0, 1, null, 0);
            }
            else
            {
                // Convert to string using invariant culture for consistency
                var stringValue = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                var valueBytes = System.Text.Encoding.UTF8.GetBytes(stringValue);
                _algorithm.TransformBlock(valueBytes, 0, valueBytes.Length, null, 0);
            }
        }
        
        // Add record separator after each row
        _algorithm.TransformBlock(new[] { RecordSeparator }, 0, 1, null, 0);
    }
    
    public string Finalize()
    {
        _algorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(_algorithm.Hash!);
    }
}