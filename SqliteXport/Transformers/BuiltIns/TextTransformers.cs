using System.Globalization;
using System.Text.RegularExpressions;
using System.Text;

namespace DB2XL.Transformers.BuiltIns;

/// <summary>
/// Converts text to uppercase with culture options
/// </summary>
public class UpperCaseTransformer : CellTransformerBase
{
    public UpperCaseTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Affinity == SqliteAffinity.Text && 
               (ctx.Column.ToLowerInvariant().Contains("name") || 
                ctx.Column.ToLowerInvariant().Contains("title") ||
                ctx.Column.ToLowerInvariant().Contains("text") ||
                GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var culture = GetConfig("culture", "invariant").ToLowerInvariant();
        
        try
        {
            return culture switch
            {
                "invariant" => raw.ToUpperInvariant(),
                "current" => raw.ToUpper(),
                "turkish" => raw.ToUpper(new CultureInfo("tr-TR")),
                _ => raw.ToUpperInvariant()
            };
        }
        catch (Exception ex)
        {
            throw new TransformerException("upper", ctx, $"Failed to convert text to uppercase: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Converts text to lowercase with culture options
/// </summary>
public class LowerCaseTransformer : CellTransformerBase
{
    public LowerCaseTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Affinity == SqliteAffinity.Text && 
               (ctx.Column.ToLowerInvariant().Contains("name") || 
                ctx.Column.ToLowerInvariant().Contains("title") ||
                ctx.Column.ToLowerInvariant().Contains("text") ||
                GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var culture = GetConfig("culture", "invariant").ToLowerInvariant();
        
        try
        {
            return culture switch
            {
                "invariant" => raw.ToLowerInvariant(),
                "current" => raw.ToLower(),
                "turkish" => raw.ToLower(new CultureInfo("tr-TR")),
                _ => raw.ToLowerInvariant()
            };
        }
        catch (Exception ex)
        {
            throw new TransformerException("lower", ctx, $"Failed to convert text to lowercase: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Converts text to title case (proper case)
/// </summary>
public class TitleCaseTransformer : CellTransformerBase
{
    public TitleCaseTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Affinity == SqliteAffinity.Text && 
               (ctx.Column.ToLowerInvariant().Contains("name") || 
                ctx.Column.ToLowerInvariant().Contains("title") ||
                GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var culture = GetConfig("culture", "current");
        var cultureInfo = culture.ToLowerInvariant() switch
        {
            "invariant" => CultureInfo.InvariantCulture,
            "current" => CultureInfo.CurrentCulture,
            _ => CultureInfo.CurrentCulture
        };

        try
        {
            return cultureInfo.TextInfo.ToTitleCase(raw.ToLower(cultureInfo));
        }
        catch (Exception ex)
        {
            throw new TransformerException("title-case", ctx, $"Failed to convert text to title case: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Trims whitespace or custom characters from text
/// </summary>
public class TrimTransformer : CellTransformerBase
{
    public TrimTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Affinity == SqliteAffinity.Text && 
               GetConfigBool("forceApply", false);
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (raw == null)
            return raw;

        var trimMode = GetConfig("mode", "both").ToLowerInvariant(); // both, start, end
        var trimChars = GetConfig("chars", "");
        
        try
        {
            char[]? charsToTrim = string.IsNullOrEmpty(trimChars) ? null : trimChars.ToCharArray();
            
            return trimMode switch
            {
                "start" => charsToTrim == null ? raw.TrimStart() : raw.TrimStart(charsToTrim),
                "end" => charsToTrim == null ? raw.TrimEnd() : raw.TrimEnd(charsToTrim),
                _ => charsToTrim == null ? raw.Trim() : raw.Trim(charsToTrim)
            };
        }
        catch (Exception ex)
        {
            throw new TransformerException("trim", ctx, $"Failed to trim text: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Truncates text to a maximum length with optional ellipsis
/// </summary>
public class TruncateTransformer : CellTransformerBase
{
    public TruncateTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Affinity == SqliteAffinity.Text && 
               GetConfigBool("forceApply", false);
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var maxLength = GetConfigInt("maxLength", 100);
        var ellipsis = GetConfig("ellipsis", "...");
        var mode = GetConfig("mode", "end").ToLowerInvariant(); // end, middle, start

        if (raw.Length <= maxLength)
            return raw;

        try
        {
            return mode switch
            {
                "start" => ellipsis + raw.Substring(raw.Length - maxLength + ellipsis.Length),
                "middle" => TruncateMiddle(raw, maxLength, ellipsis),
                _ => raw.Substring(0, Math.Max(0, maxLength - ellipsis.Length)) + ellipsis
            };
        }
        catch (Exception ex)
        {
            throw new TransformerException("truncate", ctx, $"Failed to truncate text: {ex.Message}", ex);
        }
    }

    private string TruncateMiddle(string text, int maxLength, string ellipsis)
    {
        if (maxLength <= ellipsis.Length)
            return ellipsis.Substring(0, maxLength);

        var availableLength = maxLength - ellipsis.Length;
        var startLength = availableLength / 2;
        var endLength = availableLength - startLength;

        return text.Substring(0, startLength) + ellipsis + text.Substring(text.Length - endLength);
    }
}

/// <summary>
/// Replaces null or empty values with a default value
/// </summary>
public class CoalesceTransformer : CellTransformerBase
{
    public CoalesceTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return GetConfigBool("forceApply", false);
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        var defaultValue = GetConfig("default", "N/A");
        var treatEmptyAsNull = GetConfigBool("treatEmptyAsNull", true);

        if (raw == null)
            return defaultValue;

        if (treatEmptyAsNull && string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        return raw;
    }
}

/// <summary>
/// Performs regex find and replace operations
/// </summary>
public class RegexReplaceTransformer : CellTransformerBase
{
    private readonly Regex? _regex;

    public RegexReplaceTransformer(IDictionary<string, string> configuration) : base(configuration) 
    {
        var pattern = GetConfig("pattern", "");
        if (!string.IsNullOrEmpty(pattern))
        {
            var options = GetRegexOptions();
            try
            {
                _regex = new Regex(pattern, options, TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                throw new TransformerException("regex-replace", new CellContext("", "", 0, SqliteAffinity.Text), 
                    $"Invalid regex pattern '{pattern}': {ex.Message}", ex);
            }
        }
    }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Affinity == SqliteAffinity.Text && 
               _regex != null &&
               GetConfigBool("forceApply", false);
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw) || _regex == null)
            return raw;

        var replacement = GetConfig("replacement", "");
        var maxReplacements = GetConfigInt("maxReplacements", -1);

        try
        {
            return maxReplacements == -1 
                ? _regex.Replace(raw, replacement)
                : _regex.Replace(raw, replacement, maxReplacements);
        }
        catch (RegexMatchTimeoutException)
        {
            return raw; // Timeout, return original
        }
        catch (Exception ex)
        {
            throw new TransformerException("regex-replace", ctx, $"Regex replacement failed: {ex.Message}", ex);
        }
    }

    private RegexOptions GetRegexOptions()
    {
        var options = RegexOptions.None;
        
        if (GetConfigBool("ignoreCase", false))
            options |= RegexOptions.IgnoreCase;
        
        if (GetConfigBool("multiline", false))
            options |= RegexOptions.Multiline;
        
        if (GetConfigBool("singleline", false))
            options |= RegexOptions.Singleline;

        return options;
    }
}

/// <summary>
/// Masks sensitive text like emails, phone numbers, or credit cards
/// </summary>
public class MaskTransformer : CellTransformerBase
{
    public MaskTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        var columnName = ctx.Column.ToLowerInvariant();
        return ctx.Affinity == SqliteAffinity.Text && 
               (columnName.Contains("email") || 
                columnName.Contains("phone") ||
                columnName.Contains("card") ||
                columnName.Contains("ssn") ||
                columnName.Contains("social") ||
                columnName.Contains("password") ||
                GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var maskType = GetConfig("type", "auto").ToLowerInvariant();
        var maskChar = GetConfig("maskChar", "*")[0];
        
        try
        {
            return maskType switch
            {
                "email" => MaskEmail(raw, maskChar),
                "phone" => MaskPhone(raw, maskChar),
                "card" => MaskCreditCard(raw, maskChar),
                "ssn" => MaskSSN(raw, maskChar),
                "custom" => MaskCustom(raw, maskChar),
                _ => AutoDetectAndMask(raw, maskChar, ctx.Column)
            };
        }
        catch (Exception ex)
        {
            throw new TransformerException("mask", ctx, $"Failed to mask text: {ex.Message}", ex);
        }
    }

    private string MaskEmail(string email, char maskChar)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0) return email;

        var username = email.Substring(0, atIndex);
        var domain = email.Substring(atIndex);
        
        var visibleChars = Math.Min(2, username.Length - 1);
        var maskedUsername = username.Substring(0, visibleChars) + new string(maskChar, Math.Max(0, username.Length - visibleChars));
        
        return maskedUsername + domain;
    }

    private string MaskPhone(string phone, char maskChar)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 7) return phone;

        // For phone numbers, keep first 3 and last 3 digits, mask the middle
        var keepStart = 3;
        var keepEnd = 3;
        var maskLength = digits.Length - keepStart - keepEnd;
        
        var masked = digits.Substring(0, keepStart) + new string(maskChar, maskLength) + digits.Substring(digits.Length - keepEnd);
        
        // Preserve original formatting by replacing each digit with corresponding masked digit
        var result = new StringBuilder(phone);
        var digitIndex = 0;
        
        for (int i = 0; i < result.Length && digitIndex < masked.Length; i++)
        {
            if (char.IsDigit(result[i]))
            {
                result[i] = masked[digitIndex++];
            }
        }
        
        return result.ToString();
    }

    private string MaskCreditCard(string card, char maskChar)
    {
        var digits = new string(card.Where(char.IsDigit).ToArray());
        if (digits.Length < 8) return card;

        var masked = new string(maskChar, digits.Length - 4) + digits.Substring(digits.Length - 4);
        
        // Preserve original formatting
        var result = card;
        var digitIndex = 0;
        for (int i = 0; i < card.Length && digitIndex < masked.Length; i++)
        {
            if (char.IsDigit(card[i]))
            {
                result = result.Remove(i, 1).Insert(i, masked[digitIndex++].ToString());
            }
        }
        return result;
    }

    private string MaskSSN(string ssn, char maskChar)
    {
        var digits = new string(ssn.Where(char.IsDigit).ToArray());
        if (digits.Length != 9) return ssn;

        var masked = new string(maskChar, 5) + digits.Substring(5);
        
        // Apply to original format
        var result = ssn;
        var digitIndex = 0;
        for (int i = 0; i < ssn.Length && digitIndex < masked.Length; i++)
        {
            if (char.IsDigit(ssn[i]))
            {
                result = result.Remove(i, 1).Insert(i, masked[digitIndex++].ToString());
            }
        }
        return result;
    }

    private string MaskCustom(string text, char maskChar)
    {
        var keepStart = GetConfigInt("keepStart", 2);
        var keepEnd = GetConfigInt("keepEnd", 2);
        
        if (text.Length <= keepStart + keepEnd)
            return text;

        return text.Substring(0, keepStart) + 
               new string(maskChar, text.Length - keepStart - keepEnd) + 
               text.Substring(text.Length - keepEnd);
    }

    private string AutoDetectAndMask(string text, char maskChar, string columnName)
    {
        var column = columnName.ToLowerInvariant();
        
        if (column.Contains("email") && text.Contains("@"))
            return MaskEmail(text, maskChar);
        
        if (column.Contains("phone") || column.Contains("tel"))
            return MaskPhone(text, maskChar);
        
        if (column.Contains("card") || column.Contains("credit"))
            return MaskCreditCard(text, maskChar);
        
        if (column.Contains("ssn") || column.Contains("social"))
            return MaskSSN(text, maskChar);

        // Default: mask middle portion
        return MaskCustom(text, maskChar);
    }
}

/// <summary>
/// Normalizes whitespace (multiple spaces, tabs, newlines to single space)
/// </summary>
public class NormalizeWhitespaceTransformer : CellTransformerBase
{
    private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

    public NormalizeWhitespaceTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Affinity == SqliteAffinity.Text && 
               (ctx.Column.ToLowerInvariant().Contains("description") || 
                ctx.Column.ToLowerInvariant().Contains("comment") ||
                ctx.Column.ToLowerInvariant().Contains("text") ||
                GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var replacement = GetConfig("replacement", " ");
        var trimResult = GetConfigBool("trim", true);

        try
        {
            var result = WhitespaceRegex.Replace(raw, replacement);
            return trimResult ? result.Trim() : result;
        }
        catch (Exception ex)
        {
            throw new TransformerException("normalize-whitespace", ctx, $"Failed to normalize whitespace: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Removes or replaces special characters
/// </summary>
public class SanitizeTransformer : CellTransformerBase
{
    public SanitizeTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Affinity == SqliteAffinity.Text && 
               (ctx.Column.ToLowerInvariant().Contains("filename") || 
                ctx.Column.ToLowerInvariant().Contains("slug") ||
                ctx.Column.ToLowerInvariant().Contains("url") ||
                GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var mode = GetConfig("mode", "filename").ToLowerInvariant();
        var replacement = GetConfig("replacement", "-");
        var removeAccents = GetConfigBool("removeAccents", false);

        try
        {
            var result = raw;
            
            if (removeAccents)
                result = RemoveAccents(result);

            return mode switch
            {
                "filename" => SanitizeFilename(result, replacement),
                "url" => SanitizeUrl(result, replacement),
                "alphanumeric" => SanitizeAlphanumeric(result, replacement),
                "custom" => SanitizeCustom(result, replacement),
                _ => SanitizeFilename(result, replacement)
            };
        }
        catch (Exception ex)
        {
            throw new TransformerException("sanitize", ctx, $"Failed to sanitize text: {ex.Message}", ex);
        }
    }

    private string RemoveAccents(string text)
    {
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }

    private string SanitizeFilename(string text, string replacement)
    {
        var invalidChars = new char[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        foreach (var c in invalidChars)
        {
            text = text.Replace(c.ToString(), replacement);
        }
        return text;
    }

    private string SanitizeUrl(string text, string replacement)
    {
        // Keep only URL-safe characters
        return Regex.Replace(text, @"[^a-zA-Z0-9\-._~:/?#[\]@!$&'()*+,;=]", replacement);
    }

    private string SanitizeAlphanumeric(string text, string replacement)
    {
        return Regex.Replace(text, @"[^a-zA-Z0-9]", replacement);
    }

    private string SanitizeCustom(string text, string replacement)
    {
        var allowedChars = GetConfig("allowedChars", "a-zA-Z0-9");
        var pattern = $"[^{allowedChars}]";
        return Regex.Replace(text, pattern, replacement);
    }
}