using DB2XL.Transform.BuiltIns;
using DB2XL.Transform.Examples;

namespace DB2XL.Transform.Interfaces;

/// <summary>
/// Builder for creating and configuring transformer registries with built-in transformers
/// </summary>
public sealed class TransformerRegistryBuilder
{
    private readonly TransformerRegistry _registry = new();
    private bool _includeBuiltIns = true;

    /// <summary>
    /// Controls whether built-in transformers are automatically registered
    /// </summary>
    /// <param name="include">True to include built-in transformers (default), false to exclude</param>
    /// <returns>This builder for method chaining</returns>
    public TransformerRegistryBuilder WithBuiltIns(bool include = true)
    {
        _includeBuiltIns = include;
        return this;
    }

    /// <summary>
    /// Registers a custom cell transformer
    /// </summary>
    /// <param name="name">Transformer name</param>
    /// <param name="factory">Factory function</param>
    /// <returns>This builder for method chaining</returns>
    public TransformerRegistryBuilder Register(string name, Func<IDictionary<string, string>, ICellTransformer> factory)
    {
        _registry.Register(name, factory);
        return this;
    }

    /// <summary>
    /// Registers a custom row transformer
    /// </summary>
    /// <param name="name">Transformer name</param>
    /// <param name="factory">Factory function</param>
    /// <returns>This builder for method chaining</returns>
    public TransformerRegistryBuilder RegisterRow(string name, Func<IDictionary<string, string>, IRowTransformer> factory)
    {
        _registry.RegisterRow(name, factory);
        return this;
    }

    /// <summary>
    /// Registers a simple cell transformer that takes no configuration
    /// </summary>
    /// <typeparam name="T">Transformer type that has parameterless constructor</typeparam>
    /// <param name="name">Transformer name</param>
    /// <returns>This builder for method chaining</returns>
    public TransformerRegistryBuilder Register<T>(string name) where T : ICellTransformer, new()
    {
        _registry.Register(name, _ => new T());
        return this;
    }

    /// <summary>
    /// Registers a cell transformer that extends CellTransformerBase
    /// </summary>
    /// <typeparam name="T">Transformer type that extends CellTransformerBase</typeparam>
    /// <param name="name">Transformer name</param>
    /// <returns>This builder for method chaining</returns>
    public TransformerRegistryBuilder RegisterConfigurable<T>(string name) where T : CellTransformerBase
    {
        _registry.Register(name, config => (T)Activator.CreateInstance(typeof(T), config)!);
        return this;
    }

    /// <summary>
    /// Builds the configured transformer registry
    /// </summary>
    /// <returns>Configured transformer registry</returns>
    public ITransformerRegistry Build()
    {
        if (_includeBuiltIns)
        {
            RegisterBuiltInTransformers();
        }

        return _registry;
    }

    /// <summary>
    /// Creates a default registry with all built-in transformers
    /// </summary>
    /// <returns>Registry with built-in transformers</returns>
    public static ITransformerRegistry CreateDefault()
    {
        return new TransformerRegistryBuilder().Build();
    }

    /// <summary>
    /// Creates an empty registry with no built-in transformers
    /// </summary>
    /// <returns>Empty transformer registry</returns>
    public static ITransformerRegistry CreateEmpty()
    {
        return new TransformerRegistryBuilder()
            .WithBuiltIns(false)
            .Build();
    }

    private void RegisterBuiltInTransformers()
    {
        // Time/Date transformers - IMPLEMENTED ✅
        Register("epoch", config => new BuiltIns.EpochTransformer(config));
        Register("ticks", config => new BuiltIns.TicksTransformer(config));
        Register("julian-day", config => new BuiltIns.JulianDayTransformer(config));
        Register("date-format", config => new BuiltIns.DateFormatTransformer(config));
        Register("date-part", config => new BuiltIns.DatePartTransformer(config));
        
        // Example text transformers (replaced by comprehensive BuiltIn implementations)
        // Register("upper", config => new Examples.UpperCaseTransformer(config));
        // Register("trim", config => new Examples.TrimTransformer(config));
        
        // JSON transformers - IMPLEMENTED ✅
        Register("json-compact", config => new BuiltIns.JsonCompactTransformer(config));
        Register("json-pretty", config => new BuiltIns.JsonPrettyTransformer(config));
        Register("json-extract", config => new BuiltIns.JsonExtractTransformer(config));
        Register("json-flatten", config => new BuiltIns.JsonFlattenTransformer(config));
        Register("json-validate", config => new BuiltIns.JsonValidateTransformer(config));
        Register("json-count", config => new BuiltIns.JsonCountTransformer(config));
        
        // Binary JSON transformers - IMPLEMENTED ✅
        Register("binary-json-decode", config => new BuiltIns.BinaryJsonDecodeTransformer(config));
        Register("binary-json-encode", config => new BuiltIns.BinaryJsonEncodeTransformer(config));
        
        // Binary transformers
        // Register("base64-decode", config => new Base64DecodeTransformer(config));
        // Register("hex-encode", config => new HexEncodeTransformer(config));
        // Register("blob-hash", config => new BlobHashTransformer(config));
        
        // Text transformers - IMPLEMENTED ✅
        Register("upper", config => new BuiltIns.UpperCaseTransformer(config));
        Register("lower", config => new BuiltIns.LowerCaseTransformer(config));
        Register("title-case", config => new BuiltIns.TitleCaseTransformer(config));
        Register("trim", config => new BuiltIns.TrimTransformer(config));
        Register("truncate", config => new BuiltIns.TruncateTransformer(config));
        Register("coalesce", config => new BuiltIns.CoalesceTransformer(config));
        Register("regex-replace", config => new BuiltIns.RegexReplaceTransformer(config));
        Register("mask", config => new BuiltIns.MaskTransformer(config));
        Register("normalize-whitespace", config => new BuiltIns.NormalizeWhitespaceTransformer(config));
        Register("sanitize", config => new BuiltIns.SanitizeTransformer(config));
    }
}