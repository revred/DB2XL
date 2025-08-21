using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace DB2XL.Export.Bundle.Services;

/// <summary>
/// Performance optimization utilities for high-throughput bundle export operations.
/// Provides streaming, batching, and parallel processing capabilities.
/// </summary>
public static class PerformanceOptimizer
{
    /// <summary>
    /// Optimized async enumerable that uses ConfigureAwait(false) for better performance.
    /// </summary>
    /// <typeparam name="T">Type of items in the enumerable</typeparam>
    /// <param name="source">Source async enumerable</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Performance-optimized async enumerable</returns>
    public static async IAsyncEnumerable<T> WithOptimizedPerformance<T>(
        this IAsyncEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    /// <summary>
    /// Buffers items from an async enumerable into batches for more efficient processing.
    /// Reduces the overhead of individual async operations.
    /// </summary>
    /// <typeparam name="T">Type of items</typeparam>
    /// <param name="source">Source async enumerable</param>
    /// <param name="batchSize">Number of items per batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Batched items</returns>
    public static async IAsyncEnumerable<IReadOnlyList<T>> Batch<T>(
        this IAsyncEnumerable<T> source,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0) throw new ArgumentException("Batch size must be greater than 0", nameof(batchSize));

        var batch = new List<T>(batchSize);
        
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            batch.Add(item);
            
            if (batch.Count >= batchSize)
            {
                yield return batch.AsReadOnly();
                batch.Clear();
            }
        }
        
        if (batch.Count > 0)
        {
            yield return batch.AsReadOnly();
        }
    }

    /// <summary>
    /// Processes items in parallel while maintaining streaming semantics.
    /// Items are processed concurrently but results are yielded in order.
    /// </summary>
    /// <typeparam name="TInput">Input type</typeparam>
    /// <typeparam name="TOutput">Output type</typeparam>
    /// <param name="source">Source async enumerable</param>
    /// <param name="processor">Processing function</param>
    /// <param name="maxConcurrency">Maximum number of concurrent operations</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processed items in order</returns>
    public static async IAsyncEnumerable<TOutput> ProcessInParallel<TInput, TOutput>(
        this IAsyncEnumerable<TInput> source,
        Func<TInput, CancellationToken, Task<TOutput>> processor,
        int maxConcurrency = 4,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = new List<Task<TOutput>>();
        
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            
            var task = ProcessItemAsync(item, processor, semaphore, cancellationToken);
            tasks.Add(task);
            
            // Yield completed tasks while maintaining order
            if (tasks.Count >= maxConcurrency)
            {
                var completed = await Task.WhenAny(tasks).ConfigureAwait(false);
                var result = await completed.ConfigureAwait(false);
                yield return result;
                tasks.Remove(completed);
            }
        }
        
        // Process remaining tasks
        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks).ConfigureAwait(false);
            var result = await completed.ConfigureAwait(false);
            yield return result;
            tasks.Remove(completed);
        }
    }
    
    private static async Task<TOutput> ProcessItemAsync<TInput, TOutput>(
        TInput item,
        Func<TInput, CancellationToken, Task<TOutput>> processor,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        try
        {
            return await processor(item, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Creates a high-performance memory pool for reducing allocations during export operations.
    /// </summary>
    /// <typeparam name="T">Type of pooled objects</typeparam>
    /// <param name="factory">Factory function to create new objects</param>
    /// <param name="resetAction">Action to reset objects before returning to pool</param>
    /// <param name="maxSize">Maximum pool size</param>
    /// <returns>Object pool</returns>
    public static ObjectPool<T> CreateObjectPool<T>(
        Func<T> factory,
        Action<T>? resetAction = null,
        int maxSize = 100) where T : class
    {
        return new ObjectPool<T>(factory, resetAction, maxSize);
    }

    /// <summary>
    /// Estimates memory usage for streaming operations to avoid out-of-memory scenarios.
    /// </summary>
    /// <param name="itemCount">Number of items to process</param>
    /// <param name="averageItemSizeBytes">Average size per item in bytes</param>
    /// <param name="concurrencyLevel">Number of concurrent operations</param>
    /// <returns>Estimated memory usage information</returns>
    public static MemoryUsageEstimate EstimateMemoryUsage(
        long itemCount,
        double averageItemSizeBytes,
        int concurrencyLevel = 1)
    {
        var baseMemoryPerItem = averageItemSizeBytes;
        var concurrencyOverhead = concurrencyLevel * 1024 * 1024; // 1MB per concurrent operation
        var bufferMemory = concurrencyLevel * baseMemoryPerItem * 100; // Buffer for 100 items per thread
        
        var totalEstimatedMemory = (long)(baseMemoryPerItem * itemCount + concurrencyOverhead + bufferMemory);
        var peakMemoryUsage = (long)(concurrencyLevel * baseMemoryPerItem * 1000 + concurrencyOverhead); // Peak usage
        
        return new MemoryUsageEstimate
        {
            TotalEstimatedBytes = totalEstimatedMemory,
            PeakMemoryUsageBytes = peakMemoryUsage,
            RecommendedBatchSize = CalculateOptimalBatchSize(averageItemSizeBytes, concurrencyLevel),
            MemoryPressureLevel = DetermineMemoryPressureLevel(peakMemoryUsage)
        };
    }
    
    private static int CalculateOptimalBatchSize(double averageItemSizeBytes, int concurrencyLevel)
    {
        // Target 4MB batches, adjusted for item size and concurrency
        const long targetBatchMemory = 4 * 1024 * 1024; // 4MB
        var itemsPerBatch = (int)(targetBatchMemory / Math.Max(averageItemSizeBytes, 1));
        
        // Ensure reasonable bounds
        return Math.Max(100, Math.Min(10_000, itemsPerBatch / concurrencyLevel));
    }
    
    private static MemoryPressureLevel DetermineMemoryPressureLevel(long peakMemoryUsage)
    {
        var availableMemory = GC.GetTotalMemory(false);
        var totalMemory = Environment.WorkingSet;
        
        var memoryRatio = (double)peakMemoryUsage / totalMemory;
        
        return memoryRatio switch
        {
            < 0.1 => MemoryPressureLevel.Low,
            < 0.3 => MemoryPressureLevel.Medium,
            < 0.6 => MemoryPressureLevel.High,
            _ => MemoryPressureLevel.Critical
        };
    }
}

/// <summary>
/// High-performance object pool for reducing allocations.
/// </summary>
/// <typeparam name="T">Type of pooled objects</typeparam>
public sealed class ObjectPool<T> : IDisposable where T : class
{
    private readonly ConcurrentQueue<T> _objects = new();
    private readonly Func<T> _factory;
    private readonly Action<T>? _resetAction;
    private readonly int _maxSize;
    private int _currentSize;

    internal ObjectPool(Func<T> factory, Action<T>? resetAction, int maxSize)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _resetAction = resetAction;
        _maxSize = maxSize;
    }

    /// <summary>
    /// Gets an object from the pool or creates a new one.
    /// </summary>
    /// <returns>Pooled object</returns>
    public T Get()
    {
        if (_objects.TryDequeue(out var obj))
        {
            Interlocked.Decrement(ref _currentSize);
            return obj;
        }
        
        return _factory();
    }

    /// <summary>
    /// Returns an object to the pool.
    /// </summary>
    /// <param name="obj">Object to return</param>
    public void Return(T obj)
    {
        if (obj == null) return;
        
        if (_currentSize < _maxSize)
        {
            _resetAction?.Invoke(obj);
            _objects.Enqueue(obj);
            Interlocked.Increment(ref _currentSize);
        }
    }

    /// <summary>
    /// Gets a pooled object and ensures it's returned when disposed.
    /// </summary>
    /// <returns>Disposable pooled object</returns>
    public PooledObject<T> Rent()
    {
        return new PooledObject<T>(this, Get());
    }

    public void Dispose()
    {
        while (_objects.TryDequeue(out var obj))
        {
            if (obj is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}

/// <summary>
/// Disposable wrapper for pooled objects.
/// </summary>
/// <typeparam name="T">Type of pooled object</typeparam>
public readonly struct PooledObject<T> : IDisposable where T : class
{
    private readonly ObjectPool<T> _pool;
    public T Value { get; }

    internal PooledObject(ObjectPool<T> pool, T value)
    {
        _pool = pool;
        Value = value;
    }

    public void Dispose()
    {
        _pool.Return(Value);
    }
}

/// <summary>
/// Memory usage estimation results.
/// </summary>
public sealed record MemoryUsageEstimate
{
    /// <summary>Total estimated memory usage for the entire operation.</summary>
    public long TotalEstimatedBytes { get; init; }
    
    /// <summary>Peak memory usage during processing.</summary>
    public long PeakMemoryUsageBytes { get; init; }
    
    /// <summary>Recommended batch size for optimal performance.</summary>
    public int RecommendedBatchSize { get; init; }
    
    /// <summary>Memory pressure level assessment.</summary>
    public MemoryPressureLevel MemoryPressureLevel { get; init; }
}

/// <summary>
/// Memory pressure levels for performance tuning.
/// </summary>
public enum MemoryPressureLevel
{
    /// <summary>Low memory pressure - can use aggressive caching and batching.</summary>
    Low,
    
    /// <summary>Medium memory pressure - use moderate batching.</summary>
    Medium,
    
    /// <summary>High memory pressure - use small batches and frequent GC.</summary>
    High,
    
    /// <summary>Critical memory pressure - use minimal batching and streaming.</summary>
    Critical
}