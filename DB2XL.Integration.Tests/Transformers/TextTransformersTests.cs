using DB2XL.Core.Models;
using DB2XL.Data.Schema;
using DB2XL.Transform.Interfaces;
using DB2XL.Transform.BuiltIns;
using Xunit;
using System.Globalization;

namespace DB2XL.Integration.Tests.Transformers;

public class UpperCaseTransformerTests
{
    [Fact]
    public void UpperCaseTransformer_ShouldConvertToUpperCase()
    {
        // Arrange
        var transformer = new UpperCaseTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "full_name", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "john doe");

        // Assert
        Assert.Equal("JOHN DOE", result);
    }

    [Fact]
    public void UpperCaseTransformer_ShouldHandleTurkishCulture()
    {
        // Arrange
        var transformer = new UpperCaseTransformer(new Dictionary<string, string>
        {
            ["culture"] = "turkish"
        });
        var context = new CellContext("users", "name", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "istanbul");

        // Assert
        Assert.Equal("İSTANBUL", result); // Turkish I becomes İ
    }

    [Theory]
    [InlineData("users", "full_name", true)]
    [InlineData("users", "title", true)]
    [InlineData("users", "description_text", true)]
    [InlineData("users", "id", false)]
    [InlineData("users", "age", false)]
    public void UpperCaseTransformer_CanApply_ShouldDetectNameColumns(string table, string column, bool expected)
    {
        // Arrange
        var transformer = new UpperCaseTransformer(new Dictionary<string, string>());
        var context = new CellContext(table, column, 0, SqliteAffinity.Text);

        // Act
        var result = transformer.CanApply(context);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void UpperCaseTransformer_ShouldHandleNullAndEmpty()
    {
        // Arrange
        var transformer = new UpperCaseTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "name", 0, SqliteAffinity.Text);

        // Act & Assert
        Assert.Null(transformer.Transform(context, null));
        Assert.Equal("", transformer.Transform(context, ""));
    }
}

public class LowerCaseTransformerTests
{
    [Fact]
    public void LowerCaseTransformer_ShouldConvertToLowerCase()
    {
        // Arrange
        var transformer = new LowerCaseTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "full_name", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "JOHN DOE");

        // Assert
        Assert.Equal("john doe", result);
    }

    [Fact]
    public void LowerCaseTransformer_ShouldHandleTurkishCulture()
    {
        // Arrange
        var transformer = new LowerCaseTransformer(new Dictionary<string, string>
        {
            ["culture"] = "turkish"
        });
        var context = new CellContext("users", "name", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "İSTANBUL");

        // Assert
        Assert.Equal("istanbul", result); // Turkish İ becomes i
    }

    [Fact]
    public void LowerCaseTransformer_ShouldForceApplyWithConfig()
    {
        // Arrange
        var transformer = new LowerCaseTransformer(new Dictionary<string, string>
        {
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "any_column", 0, SqliteAffinity.Text);

        // Act
        var canApply = transformer.CanApply(context);
        var result = transformer.Transform(context, "FORCED");

        // Assert
        Assert.True(canApply);
        Assert.Equal("forced", result);
    }
}

public class TitleCaseTransformerTests
{
    [Fact]
    public void TitleCaseTransformer_ShouldConvertToTitleCase()
    {
        // Arrange
        var transformer = new TitleCaseTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "full_name", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "john doe smith");

        // Assert
        Assert.Equal("John Doe Smith", result);
    }

    [Fact]
    public void TitleCaseTransformer_ShouldHandleMixedCase()
    {
        // Arrange
        var transformer = new TitleCaseTransformer(new Dictionary<string, string>());
        var context = new CellContext("products", "product_title", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "iPhone 12 PRO max");

        // Assert
        Assert.Equal("Iphone 12 Pro Max", result);
    }

    [Theory]
    [InlineData("users", "full_name", true)]
    [InlineData("products", "title", true)]
    [InlineData("books", "book_title", true)]
    [InlineData("users", "email", false)]
    [InlineData("orders", "id", false)]
    public void TitleCaseTransformer_CanApply_ShouldDetectTitleColumns(string table, string column, bool expected)
    {
        // Arrange
        var transformer = new TitleCaseTransformer(new Dictionary<string, string>());
        var context = new CellContext(table, column, 0, SqliteAffinity.Text);

        // Act
        var result = transformer.CanApply(context);

        // Assert
        Assert.Equal(expected, result);
    }
}

public class TrimTransformerTests
{
    [Fact]
    public void TrimTransformer_ShouldTrimWhitespace()
    {
        // Arrange
        var transformer = new TrimTransformer(new Dictionary<string, string>
        {
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "text", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "  hello world  ");

        // Assert
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void TrimTransformer_ShouldTrimStart()
    {
        // Arrange
        var transformer = new TrimTransformer(new Dictionary<string, string>
        {
            ["mode"] = "start",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "text", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "  hello world  ");

        // Assert
        Assert.Equal("hello world  ", result);
    }

    [Fact]
    public void TrimTransformer_ShouldTrimEnd()
    {
        // Arrange
        var transformer = new TrimTransformer(new Dictionary<string, string>
        {
            ["mode"] = "end",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "text", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "  hello world  ");

        // Assert
        Assert.Equal("  hello world", result);
    }

    [Fact]
    public void TrimTransformer_ShouldTrimCustomCharacters()
    {
        // Arrange
        var transformer = new TrimTransformer(new Dictionary<string, string>
        {
            ["chars"] = ".,!",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "text", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "...hello world!!!");

        // Assert
        Assert.Equal("hello world", result);
    }
}

public class TruncateTransformerTests
{
    [Fact]
    public void TruncateTransformer_ShouldTruncateAtEnd()
    {
        // Arrange
        var transformer = new TruncateTransformer(new Dictionary<string, string>
        {
            ["maxLength"] = "10",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "description", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "This is a very long text");

        // Assert
        Assert.Equal("This is...", result);
    }

    [Fact]
    public void TruncateTransformer_ShouldTruncateAtStart()
    {
        // Arrange
        var transformer = new TruncateTransformer(new Dictionary<string, string>
        {
            ["maxLength"] = "10",
            ["mode"] = "start",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "description", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "This is a very long text");

        // Assert
        Assert.Equal("...ng text", result);
    }

    [Fact]
    public void TruncateTransformer_ShouldTruncateAtMiddle()
    {
        // Arrange
        var transformer = new TruncateTransformer(new Dictionary<string, string>
        {
            ["maxLength"] = "15",
            ["mode"] = "middle",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "description", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "This is a very long text message");

        // Assert
        Assert.Equal("This i...essage", result);
    }

    [Fact]
    public void TruncateTransformer_ShouldNotTruncateShortText()
    {
        // Arrange
        var transformer = new TruncateTransformer(new Dictionary<string, string>
        {
            ["maxLength"] = "20",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "text", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "Short text");

        // Assert
        Assert.Equal("Short text", result);
    }

    [Fact]
    public void TruncateTransformer_ShouldUseCustomEllipsis()
    {
        // Arrange
        var transformer = new TruncateTransformer(new Dictionary<string, string>
        {
            ["maxLength"] = "10",
            ["ellipsis"] = " [...]",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "text", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "This is a long text");

        // Assert
        Assert.Equal("This [...]", result);
    }
}

public class CoalesceTransformerTests
{
    [Fact]
    public void CoalesceTransformer_ShouldReplaceNull()
    {
        // Arrange
        var transformer = new CoalesceTransformer(new Dictionary<string, string>
        {
            ["default"] = "N/A",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "optional_field", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, null);

        // Assert
        Assert.Equal("N/A", result);
    }

    [Fact]
    public void CoalesceTransformer_ShouldReplaceEmpty()
    {
        // Arrange
        var transformer = new CoalesceTransformer(new Dictionary<string, string>
        {
            ["default"] = "MISSING",
            ["treatEmptyAsNull"] = "true",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "field", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "");

        // Assert
        Assert.Equal("MISSING", result);
    }

    [Fact]
    public void CoalesceTransformer_ShouldNotReplaceEmptyWhenDisabled()
    {
        // Arrange
        var transformer = new CoalesceTransformer(new Dictionary<string, string>
        {
            ["default"] = "MISSING",
            ["treatEmptyAsNull"] = "false",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "field", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "");

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void CoalesceTransformer_ShouldNotReplaceValidValue()
    {
        // Arrange
        var transformer = new CoalesceTransformer(new Dictionary<string, string>
        {
            ["default"] = "DEFAULT",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "field", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "valid value");

        // Assert
        Assert.Equal("valid value", result);
    }
}

public class RegexReplaceTransformerTests
{
    [Fact]
    public void RegexReplaceTransformer_ShouldReplacePattern()
    {
        // Arrange
        var transformer = new RegexReplaceTransformer(new Dictionary<string, string>
        {
            ["pattern"] = @"\d+",
            ["replacement"] = "XXX",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "text", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "Order 123 and Order 456");

        // Assert
        Assert.Equal("Order XXX and Order XXX", result);
    }

    [Fact]
    public void RegexReplaceTransformer_ShouldLimitReplacements()
    {
        // Arrange
        var transformer = new RegexReplaceTransformer(new Dictionary<string, string>
        {
            ["pattern"] = @"\d+",
            ["replacement"] = "XXX",
            ["maxReplacements"] = "1",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "text", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "Order 123 and Order 456");

        // Assert
        Assert.Equal("Order XXX and Order 456", result);
    }

    [Fact]
    public void RegexReplaceTransformer_ShouldHandleIgnoreCase()
    {
        // Arrange
        var transformer = new RegexReplaceTransformer(new Dictionary<string, string>
        {
            ["pattern"] = "hello",
            ["replacement"] = "Hi",
            ["ignoreCase"] = "true",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "text", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "Hello World and HELLO there");

        // Assert
        Assert.Equal("Hi World and Hi there", result);
    }

    [Fact]
    public void RegexReplaceTransformer_ShouldThrowOnInvalidPattern()
    {
        // Act & Assert
        Assert.Throws<TransformerException>(() =>
            new RegexReplaceTransformer(new Dictionary<string, string>
            {
                ["pattern"] = "[invalid",
                ["forceApply"] = "true"
            }));
    }
}

public class MaskTransformerTests
{
    [Fact]
    public void MaskTransformer_ShouldMaskEmail()
    {
        // Arrange
        var transformer = new MaskTransformer(new Dictionary<string, string>
        {
            ["type"] = "email"
        });
        var context = new CellContext("users", "email", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "john.doe@example.com");

        // Assert
        Assert.Equal("jo******@example.com", result);
    }

    [Fact]
    public void MaskTransformer_ShouldMaskPhone()
    {
        // Arrange
        var transformer = new MaskTransformer(new Dictionary<string, string>
        {
            ["type"] = "phone"
        });
        var context = new CellContext("users", "phone", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "(555) 123-4567");

        // Assert
        Assert.Equal("(555) ***-*567", result);
    }

    [Fact]
    public void MaskTransformer_ShouldMaskCreditCard()
    {
        // Arrange
        var transformer = new MaskTransformer(new Dictionary<string, string>
        {
            ["type"] = "card"
        });
        var context = new CellContext("orders", "card_number", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "4532-1234-5678-9012");

        // Assert
        Assert.Equal("****-****-****-9012", result);
    }

    [Fact]
    public void MaskTransformer_ShouldMaskSSN()
    {
        // Arrange
        var transformer = new MaskTransformer(new Dictionary<string, string>
        {
            ["type"] = "ssn"
        });
        var context = new CellContext("employees", "ssn", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "123-45-6789");

        // Assert
        Assert.Equal("***-**-6789", result);
    }

    [Fact]
    public void MaskTransformer_ShouldAutoDetectFromColumnName()
    {
        // Arrange
        var transformer = new MaskTransformer(new Dictionary<string, string>
        {
            ["type"] = "auto"
        });
        var context = new CellContext("users", "email_address", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "test@example.com");

        // Assert
        Assert.Contains("*", result);
        Assert.Contains("@example.com", result);
    }

    [Fact]
    public void MaskTransformer_ShouldUseCustomMaskChar()
    {
        // Arrange
        var transformer = new MaskTransformer(new Dictionary<string, string>
        {
            ["type"] = "custom",
            ["maskChar"] = "X",
            ["keepStart"] = "2",
            ["keepEnd"] = "2"
        });
        var context = new CellContext("data", "sensitive", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "sensitive data");

        // Assert  
        Assert.Equal("seXXXXXXXXXXta", result); // "sensitive data" = 14 chars, keep 2+2, mask 10
    }

    [Theory]
    [InlineData("users", "email", true)]
    [InlineData("users", "phone_number", true)]
    [InlineData("orders", "credit_card", true)]
    [InlineData("employees", "social_security", true)]
    [InlineData("users", "password_hash", true)]
    [InlineData("users", "name", false)]
    [InlineData("orders", "id", false)]
    public void MaskTransformer_CanApply_ShouldDetectSensitiveColumns(string table, string column, bool expected)
    {
        // Arrange
        var transformer = new MaskTransformer(new Dictionary<string, string>());
        var context = new CellContext(table, column, 0, SqliteAffinity.Text);

        // Act
        var result = transformer.CanApply(context);

        // Assert
        Assert.Equal(expected, result);
    }
}

public class NormalizeWhitespaceTransformerTests
{
    [Fact]
    public void NormalizeWhitespaceTransformer_ShouldNormalizeSpaces()
    {
        // Arrange
        var transformer = new NormalizeWhitespaceTransformer(new Dictionary<string, string>
        {
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "text", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "This  has    multiple   spaces");

        // Assert
        Assert.Equal("This has multiple spaces", result);
    }

    [Fact]
    public void NormalizeWhitespaceTransformer_ShouldHandleTabsAndNewlines()
    {
        // Arrange
        var transformer = new NormalizeWhitespaceTransformer(new Dictionary<string, string>
        {
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "description", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "Line 1\t\tLine 2\n\nLine 3");

        // Assert
        Assert.Equal("Line 1 Line 2 Line 3", result);
    }

    [Fact]
    public void NormalizeWhitespaceTransformer_ShouldUseCustomReplacement()
    {
        // Arrange
        var transformer = new NormalizeWhitespaceTransformer(new Dictionary<string, string>
        {
            ["replacement"] = " | ",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "text", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "Word1   \t  Word2\n\nWord3");

        // Assert
        Assert.Equal("Word1 | Word2 | Word3", result);
    }

    [Fact]
    public void NormalizeWhitespaceTransformer_ShouldSkipTrimmingWhenConfigured()
    {
        // Arrange
        var transformer = new NormalizeWhitespaceTransformer(new Dictionary<string, string>
        {
            ["trim"] = "false",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "text", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "  multiple  spaces  ");

        // Assert
        Assert.Equal(" multiple spaces ", result);
    }

    [Theory]
    [InlineData("posts", "description", true)]
    [InlineData("articles", "content_text", true)]
    [InlineData("reviews", "comment", true)]
    [InlineData("users", "id", false)]
    [InlineData("orders", "amount", false)]
    public void NormalizeWhitespaceTransformer_CanApply_ShouldDetectTextColumns(string table, string column, bool expected)
    {
        // Arrange
        var transformer = new NormalizeWhitespaceTransformer(new Dictionary<string, string>());
        var context = new CellContext(table, column, 0, SqliteAffinity.Text);

        // Act
        var result = transformer.CanApply(context);

        // Assert
        Assert.Equal(expected, result);
    }
}

public class SanitizeTransformerTests
{
    [Fact]
    public void SanitizeTransformer_ShouldSanitizeFilename()
    {
        // Arrange
        var transformer = new SanitizeTransformer(new Dictionary<string, string>
        {
            ["mode"] = "filename",
            ["forceApply"] = "true"
        });
        var context = new CellContext("files", "filename", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "my<file>name:with|bad*chars?.txt");

        // Assert
        Assert.Equal("my-file-name-with-bad-chars-.txt", result);
    }

    [Fact]
    public void SanitizeTransformer_ShouldSanitizeUrl()
    {
        // Arrange
        var transformer = new SanitizeTransformer(new Dictionary<string, string>
        {
            ["mode"] = "url",
            ["replacement"] = "-",
            ["forceApply"] = "true"
        });
        var context = new CellContext("pages", "slug", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "Hello World < More>");

        // Assert
        Assert.Equal("Hello World - More-", result); // < and > are not URL-safe
    }

    [Fact]
    public void SanitizeTransformer_ShouldSanitizeAlphanumeric()
    {
        // Arrange
        var transformer = new SanitizeTransformer(new Dictionary<string, string>
        {
            ["mode"] = "alphanumeric",
            ["replacement"] = "",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "code", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "ABC-123_XYZ!@#");

        // Assert
        Assert.Equal("ABC123XYZ", result);
    }

    [Fact]
    public void SanitizeTransformer_ShouldRemoveAccents()
    {
        // Arrange
        var transformer = new SanitizeTransformer(new Dictionary<string, string>
        {
            ["mode"] = "filename",
            ["removeAccents"] = "true",
            ["forceApply"] = "true"
        });
        var context = new CellContext("files", "filename", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "café münich naïve.txt");

        // Assert
        Assert.Equal("cafe munich naive.txt", result);
    }

    [Fact]
    public void SanitizeTransformer_ShouldUseCustomAllowedChars()
    {
        // Arrange
        var transformer = new SanitizeTransformer(new Dictionary<string, string>
        {
            ["mode"] = "custom",
            ["allowedChars"] = "a-zA-Z0-9._-",
            ["replacement"] = "_",
            ["forceApply"] = "true"
        });
        var context = new CellContext("data", "identifier", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "user@domain.com:8080");

        // Assert
        Assert.Equal("user_domain.com_8080", result);
    }

    [Theory]
    [InlineData("files", "filename", true)]
    [InlineData("pages", "url_slug", true)]
    [InlineData("posts", "slug", true)]
    [InlineData("users", "name", false)]
    [InlineData("orders", "id", false)]
    public void SanitizeTransformer_CanApply_ShouldDetectSanitizableColumns(string table, string column, bool expected)
    {
        // Arrange
        var transformer = new SanitizeTransformer(new Dictionary<string, string>());
        var context = new CellContext(table, column, 0, SqliteAffinity.Text);

        // Act
        var result = transformer.CanApply(context);

        // Assert
        Assert.Equal(expected, result);
    }
}

public class TextTransformerIntegrationTests
{
    [Fact]
    public void TextTransformers_ShouldBeRegisterable()
    {
        // Arrange
        var registry = new TransformerRegistry();

        // Act
        registry.Register("upper", config => new UpperCaseTransformer(config));
        registry.Register("lower", config => new LowerCaseTransformer(config));
        registry.Register("title-case", config => new TitleCaseTransformer(config));
        registry.Register("trim", config => new TrimTransformer(config));
        registry.Register("truncate", config => new TruncateTransformer(config));
        registry.Register("coalesce", config => new CoalesceTransformer(config));
        registry.Register("regex-replace", config => new RegexReplaceTransformer(config));
        registry.Register("mask", config => new MaskTransformer(config));
        registry.Register("normalize-whitespace", config => new NormalizeWhitespaceTransformer(config));
        registry.Register("sanitize", config => new SanitizeTransformer(config));

        // Assert
        Assert.True(registry.IsRegistered("upper"));
        Assert.True(registry.IsRegistered("lower"));
        Assert.True(registry.IsRegistered("title-case"));
        Assert.True(registry.IsRegistered("trim"));
        Assert.True(registry.IsRegistered("truncate"));
        Assert.True(registry.IsRegistered("coalesce"));
        Assert.True(registry.IsRegistered("regex-replace"));
        Assert.True(registry.IsRegistered("mask"));
        Assert.True(registry.IsRegistered("normalize-whitespace"));
        Assert.True(registry.IsRegistered("sanitize"));
    }

    [Fact]
    public void TextTransformers_ShouldChainTogether()
    {
        // Arrange
        var registry = TransformerRegistryBuilder.CreateDefault();
        var context = new CellContext("users", "full_name", 0, SqliteAffinity.Text);
        
        var trimConfig = new Dictionary<string, string> { ["forceApply"] = "true" };
        var titleConfig = new Dictionary<string, string> { ["forceApply"] = "true" };
        var truncateConfig = new Dictionary<string, string> 
        { 
            ["maxLength"] = "15",
            ["forceApply"] = "true"
        };

        // Act
        var trimmer = registry.CreateCell("trim", trimConfig);
        var titleCase = registry.CreateCell("title-case", titleConfig);
        var truncater = registry.CreateCell("truncate", truncateConfig);

        var input = "  john doe smith johnson  ";
        var step1 = trimmer.Transform(context, input);
        var step2 = titleCase.Transform(context, step1);
        var result = truncater.Transform(context, step2);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Length <= 15);
        Assert.Contains("John", result);
    }

    [Fact]
    public void TextTransformers_ShouldHandleRealWorldData()
    {
        // Arrange
        var registry = TransformerRegistryBuilder.CreateDefault();
        
        // Simulate real-world messy user input
        var messyData = new[]
        {
            "  JOHN DOE  ",
            "jane@EXAMPLE.com",
            "(555) 123-4567",
            "This is a very long description that needs to be truncated because it exceeds our display limits",
            "",
            null,
            "Café Münich & Naïve Restaurant"
        };

        var configs = new Dictionary<string, IDictionary<string, string>>
        {
            ["name-cleanup"] = new Dictionary<string, string>
            {
                ["forceApply"] = "true"
            },
            ["email-mask"] = new Dictionary<string, string>
            {
                ["type"] = "email",
                ["forceApply"] = "true"
            },
            ["phone-mask"] = new Dictionary<string, string>
            {
                ["type"] = "phone",
                ["forceApply"] = "true"  
            },
            ["description-truncate"] = new Dictionary<string, string>
            {
                ["maxLength"] = "50",
                ["forceApply"] = "true"
            },
            ["null-coalesce"] = new Dictionary<string, string>
            {
                ["default"] = "N/A",
                ["treatEmptyAsNull"] = "true",
                ["forceApply"] = "true"
            },
            ["restaurant-sanitize"] = new Dictionary<string, string>
            {
                ["mode"] = "filename",
                ["removeAccents"] = "true",
                ["forceApply"] = "true"
            }
        };

        // Act & Assert
        var trimmer = registry.CreateCell("trim", configs["name-cleanup"]);
        var titleCase = registry.CreateCell("title-case", configs["name-cleanup"]);
        var mask = registry.CreateCell("mask", configs["email-mask"]);
        var phoneMask = registry.CreateCell("mask", configs["phone-mask"]);
        var truncater = registry.CreateCell("truncate", configs["description-truncate"]);
        var coalesce = registry.CreateCell("coalesce", configs["null-coalesce"]);
        var sanitizer = registry.CreateCell("sanitize", configs["restaurant-sanitize"]);

        var ctx = new CellContext("test", "test", 0, SqliteAffinity.Text);

        // Process name
        var cleanName = titleCase.Transform(ctx, trimmer.Transform(ctx, messyData[0]));
        Assert.Equal("John Doe", cleanName);

        // Mask email
        var maskedEmail = mask.Transform(ctx, messyData[1]);
        Assert.Contains("*", maskedEmail);
        Assert.Contains("@EXAMPLE.com", maskedEmail);

        // Mask phone
        var maskedPhone = phoneMask.Transform(ctx, messyData[2]);
        Assert.Contains("*", maskedPhone);
        Assert.Contains("555", maskedPhone);

        // Truncate description
        var truncated = truncater.Transform(ctx, messyData[3]);
        Assert.NotNull(truncated);
        Assert.True(truncated.Length <= 50);

        // Handle null/empty
        var coalescedEmpty = coalesce.Transform(ctx, messyData[4]);
        var coalescedNull = coalesce.Transform(ctx, messyData[5]);
        Assert.Equal("N/A", coalescedEmpty);
        Assert.Equal("N/A", coalescedNull);

        // Sanitize restaurant name
        var sanitized = sanitizer.Transform(ctx, messyData[6]);
        Assert.Equal("Cafe Munich & Naive Restaurant", sanitized);
    }
}