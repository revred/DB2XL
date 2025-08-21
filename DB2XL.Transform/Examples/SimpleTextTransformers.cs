using DB2XL.Transform.Interfaces;
namespace DB2XL.Transform.Examples;

/// <summary>
/// Example transformer that converts text to uppercase
/// </summary>
public class UpperCaseTransformer : CellTransformerBase
{
    public UpperCaseTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        // Only apply to text fields, skip other types
        return ctx.Affinity == SqliteAffinity.Text;
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        return raw.ToUpperInvariant();
    }
}

/// <summary>
/// Example transformer that trims whitespace with optional characters
/// </summary>
public class TrimTransformer : CellTransformerBase
{
    public TrimTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var chars = GetConfig("chars");
        if (string.IsNullOrEmpty(chars))
        {
            return raw.Trim();
        }
        
        return raw.Trim(chars.ToCharArray());
    }
}

/// <summary>
/// Example transformer that truncates text to a maximum length
/// </summary>
public class TruncateTransformer : CellTransformerBase
{
    public TruncateTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var maxLength = GetConfigInt("maxLength", 100);
        var ellipsis = GetConfig("ellipsis", "...");
        
        if (raw.Length <= maxLength)
            return raw;

        var truncateLength = Math.Max(0, maxLength - ellipsis.Length);
        if (truncateLength == 0)
            return ellipsis.Substring(0, Math.Min(ellipsis.Length, maxLength));

        return raw.Substring(0, truncateLength) + ellipsis;
    }
}

/// <summary>
/// Example transformer that replaces null or empty strings with a default value
/// </summary>
public class CoalesceTransformer : CellTransformerBase
{
    public CoalesceTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override string? Transform(CellContext ctx, string? raw)
    {
        var defaultValue = GetConfig("default", "N/A");
        
        if (string.IsNullOrEmpty(raw))
            return defaultValue;

        return raw;
    }
}

/// <summary>
/// Example column-specific transformer that masks email addresses
/// </summary>
public class EmailMaskTransformer : CellTransformerBase, IColumnTransformer
{
    public string ColumnName { get; }

    public EmailMaskTransformer(IDictionary<string, string> configuration) : base(configuration)
    {
        ColumnName = GetConfig("column", "email");
    }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Column.Equals(ColumnName, StringComparison.OrdinalIgnoreCase) && 
               !string.IsNullOrEmpty(ctx.Column);
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw) || !raw.Contains("@"))
            return raw;

        var parts = raw.Split('@');
        if (parts.Length != 2)
            return raw;

        var localPart = parts[0];
        var domainPart = parts[1];

        // Mask: show first character + *** + domain
        var masked = localPart.Length > 0 ? localPart[0] + "***" : "***";
        return $"{masked}@{domainPart}";
    }
}

/// <summary>
/// Static class for registering example transformers
/// </summary>
public static class ExampleTransformers
{
    /// <summary>
    /// Registers all example transformers with the provided registry
    /// </summary>
    /// <param name="registry">Registry to register transformers with</param>
    public static void RegisterAll(ITransformerRegistry registry)
    {
        // Text transformers
        registry.Register("upper", config => new UpperCaseTransformer(config));
        registry.Register("trim", config => new TrimTransformer(config));
        registry.Register("truncate", config => new TruncateTransformer(config));
        registry.Register("coalesce", config => new CoalesceTransformer(config));
        
        // Column-specific transformers
        registry.Register("email-mask", config => new EmailMaskTransformer(config));
    }

    /// <summary>
    /// Creates a registry with all example transformers pre-registered
    /// </summary>
    /// <returns>Registry with example transformers</returns>
    public static ITransformerRegistry CreateRegistry()
    {
        var registry = TransformerRegistryBuilder.CreateEmpty();
        RegisterAll(registry);
        return registry;
    }
}