namespace DB2XL.Transform.Interfaces;

/// <summary>
/// Registry for creating and managing transformer instances
/// </summary>
public interface ITransformerRegistry
{
    /// <summary>
    /// Registers a transformer factory with the given name
    /// </summary>
    /// <param name="name">Transformer type name</param>
    /// <param name="factory">Factory function that creates transformer from configuration</param>
    void Register(string name, Func<IDictionary<string, string>, ICellTransformer> factory);

    /// <summary>
    /// Registers a row transformer factory with the given name
    /// </summary>
    /// <param name="name">Transformer type name</param>
    /// <param name="factory">Factory function that creates row transformer from configuration</param>
    void RegisterRow(string name, Func<IDictionary<string, string>, IRowTransformer> factory);

    /// <summary>
    /// Creates a cell transformer instance from configuration
    /// </summary>
    /// <param name="name">Transformer type name</param>
    /// <param name="args">Configuration arguments</param>
    /// <returns>Configured transformer instance</returns>
    ICellTransformer CreateCell(string name, IDictionary<string, string> args);

    /// <summary>
    /// Creates a row transformer instance from configuration
    /// </summary>
    /// <param name="name">Transformer type name</param>
    /// <param name="args">Configuration arguments</param>
    /// <returns>Configured transformer instance</returns>
    IRowTransformer CreateRow(string name, IDictionary<string, string> args);

    /// <summary>
    /// Gets all registered cell transformer names
    /// </summary>
    IReadOnlyCollection<string> GetRegisteredNames();

    /// <summary>
    /// Gets all registered row transformer names
    /// </summary>
    IReadOnlyCollection<string> GetRegisteredRowNames();

    /// <summary>
    /// Checks if a cell transformer with the given name is registered
    /// </summary>
    /// <param name="name">Transformer name to check</param>
    /// <returns>True if registered, false otherwise</returns>
    bool IsRegistered(string name);

    /// <summary>
    /// Checks if a row transformer with the given name is registered
    /// </summary>
    /// <param name="name">Transformer name to check</param>
    /// <returns>True if registered, false otherwise</returns>
    bool IsRowRegistered(string name);
}