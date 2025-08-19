using System.Collections.Concurrent;

namespace DB2XL.Transformers;

/// <summary>
/// Default implementation of transformer registry with thread-safe registration and instantiation
/// </summary>
public sealed class TransformerRegistry : ITransformerRegistry
{
    private readonly ConcurrentDictionary<string, Func<IDictionary<string, string>, ICellTransformer>> _cellFactories = new();
    private readonly ConcurrentDictionary<string, Func<IDictionary<string, string>, IRowTransformer>> _rowFactories = new();

    /// <summary>
    /// Registers a cell transformer factory with the given name
    /// </summary>
    /// <param name="name">Transformer type name (case-insensitive)</param>
    /// <param name="factory">Factory function that creates transformer from configuration</param>
    /// <exception cref="ArgumentException">Thrown when name is null/empty or factory is null</exception>
    public void Register(string name, Func<IDictionary<string, string>, ICellTransformer> factory)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Transformer name cannot be null or empty", nameof(name));
        
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        var normalizedName = name.ToLowerInvariant();
        _cellFactories.AddOrUpdate(normalizedName, factory, (_, _) => factory);
    }

    /// <summary>
    /// Registers a row transformer factory with the given name
    /// </summary>
    /// <param name="name">Transformer type name (case-insensitive)</param>
    /// <param name="factory">Factory function that creates row transformer from configuration</param>
    /// <exception cref="ArgumentException">Thrown when name is null/empty or factory is null</exception>
    public void RegisterRow(string name, Func<IDictionary<string, string>, IRowTransformer> factory)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Transformer name cannot be null or empty", nameof(name));
        
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        var normalizedName = name.ToLowerInvariant();
        _rowFactories.AddOrUpdate(normalizedName, factory, (_, _) => factory);
    }

    /// <summary>
    /// Creates a cell transformer instance from configuration
    /// </summary>
    /// <param name="name">Transformer type name (case-insensitive)</param>
    /// <param name="args">Configuration arguments</param>
    /// <returns>Configured transformer instance</returns>
    /// <exception cref="ArgumentException">Thrown when transformer name is not registered</exception>
    /// <exception cref="TransformerException">Thrown when factory fails to create transformer</exception>
    public ICellTransformer CreateCell(string name, IDictionary<string, string> args)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Transformer name cannot be null or empty", nameof(name));

        args ??= new Dictionary<string, string>();
        var normalizedName = name.ToLowerInvariant();

        if (!_cellFactories.TryGetValue(normalizedName, out var factory))
            throw new ArgumentException($"Cell transformer '{name}' is not registered", nameof(name));

        try
        {
            return factory(args);
        }
        catch (Exception ex) when (!(ex is TransformerException))
        {
            throw new TransformerException(name, $"Failed to create cell transformer '{name}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates a row transformer instance from configuration
    /// </summary>
    /// <param name="name">Transformer type name (case-insensitive)</param>
    /// <param name="args">Configuration arguments</param>
    /// <returns>Configured transformer instance</returns>
    /// <exception cref="ArgumentException">Thrown when transformer name is not registered</exception>
    /// <exception cref="TransformerException">Thrown when factory fails to create transformer</exception>
    public IRowTransformer CreateRow(string name, IDictionary<string, string> args)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Transformer name cannot be null or empty", nameof(name));

        args ??= new Dictionary<string, string>();
        var normalizedName = name.ToLowerInvariant();

        if (!_rowFactories.TryGetValue(normalizedName, out var factory))
            throw new ArgumentException($"Row transformer '{name}' is not registered", nameof(name));

        try
        {
            return factory(args);
        }
        catch (Exception ex) when (!(ex is TransformerException))
        {
            throw new TransformerException(name, $"Failed to create row transformer '{name}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets all registered cell transformer names
    /// </summary>
    /// <returns>Collection of registered cell transformer names</returns>
    public IReadOnlyCollection<string> GetRegisteredNames()
    {
        return _cellFactories.Keys.ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets all registered row transformer names
    /// </summary>
    /// <returns>Collection of registered row transformer names</returns>
    public IReadOnlyCollection<string> GetRegisteredRowNames()
    {
        return _rowFactories.Keys.ToList().AsReadOnly();
    }

    /// <summary>
    /// Checks if a cell transformer with the given name is registered
    /// </summary>
    /// <param name="name">Transformer name to check (case-insensitive)</param>
    /// <returns>True if registered, false otherwise</returns>
    public bool IsRegistered(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        
        return _cellFactories.ContainsKey(name.ToLowerInvariant());
    }

    /// <summary>
    /// Checks if a row transformer with the given name is registered
    /// </summary>
    /// <param name="name">Transformer name to check (case-insensitive)</param>
    /// <returns>True if registered, false otherwise</returns>
    public bool IsRowRegistered(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        
        return _rowFactories.ContainsKey(name.ToLowerInvariant());
    }

    /// <summary>
    /// Removes a cell transformer registration
    /// </summary>
    /// <param name="name">Transformer name to remove (case-insensitive)</param>
    /// <returns>True if removed, false if not found</returns>
    public bool Unregister(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        
        return _cellFactories.TryRemove(name.ToLowerInvariant(), out _);
    }

    /// <summary>
    /// Removes a row transformer registration
    /// </summary>
    /// <param name="name">Transformer name to remove (case-insensitive)</param>
    /// <returns>True if removed, false if not found</returns>
    public bool UnregisterRow(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        
        return _rowFactories.TryRemove(name.ToLowerInvariant(), out _);
    }

    /// <summary>
    /// Clears all registered transformers
    /// </summary>
    public void Clear()
    {
        _cellFactories.Clear();
        _rowFactories.Clear();
    }

    /// <summary>
    /// Gets the total count of registered transformers
    /// </summary>
    public int Count => _cellFactories.Count + _rowFactories.Count;
}