using DB2XL.Core.Models;
using DB2XL.Data.Schema;
using DB2XL.Transform.Interfaces;
using DB2XL.Transform.BuiltIns;
using Xunit;
using System.Text;

namespace DB2XL.Integration.Tests.Transformers;

public class BinaryJsonDecodeTransformerTests
{
    [Fact]
    public void BinaryJsonDecodeTransformer_ShouldDecodeBase64Json()
    {
        // Arrange
        var transformer = new BinaryJsonDecodeTransformer(new Dictionary<string, string>
        {
            ["encoding"] = "base64",
            ["format"] = "compact"
        });
        var context = new CellContext("data", "json_blob", 0, SqliteAffinity.Blob);
        
        // Create base64 encoded JSON
        var originalJson = @"{""name"":""John"",""age"":30}";
        var base64Json = Convert.ToBase64String(Encoding.UTF8.GetBytes(originalJson));

        // Act
        var result = transformer.Transform(context, base64Json);

        // Assert
        Assert.Equal(@"{""name"":""John"",""age"":30}", result);
    }

    [Fact]
    public void BinaryJsonDecodeTransformer_ShouldDecodeHexJson()
    {
        // Arrange
        var transformer = new BinaryJsonDecodeTransformer(new Dictionary<string, string>
        {
            ["encoding"] = "hex"
        });
        var context = new CellContext("data", "json_blob", 0, SqliteAffinity.Blob);
        
        // Create hex encoded JSON
        var originalJson = @"{""test"":true}";
        var hexJson = Convert.ToHexString(Encoding.UTF8.GetBytes(originalJson));

        // Act
        var result = transformer.Transform(context, hexJson);

        // Assert
        Assert.Contains("\"test\":true", result);
    }

    [Fact]
    public void BinaryJsonDecodeTransformer_ShouldAutoDetectBase64()
    {
        // Arrange
        var transformer = new BinaryJsonDecodeTransformer(new Dictionary<string, string>
        {
            ["encoding"] = "auto"
        });
        var context = new CellContext("data", "json_payload", 0, SqliteAffinity.Blob);
        
        var originalJson = @"{""auto"":""detected""}";
        var base64Json = Convert.ToBase64String(Encoding.UTF8.GetBytes(originalJson));

        // Act
        var result = transformer.Transform(context, base64Json);

        // Assert
        Assert.Contains("auto", result);
        Assert.Contains("detected", result);
    }

    [Fact]
    public void BinaryJsonDecodeTransformer_ShouldAutoDetectHex()
    {
        // Arrange
        var transformer = new BinaryJsonDecodeTransformer(new Dictionary<string, string>
        {
            ["encoding"] = "auto"
        });
        var context = new CellContext("data", "bson_data", 0, SqliteAffinity.Blob);
        
        var originalJson = @"[1,2,3]";
        var hexJson = Convert.ToHexString(Encoding.UTF8.GetBytes(originalJson)).ToLowerInvariant();

        // Act
        var result = transformer.Transform(context, hexJson);

        // Assert
        Assert.Equal("[1,2,3]", result);
    }

    [Theory]
    [InlineData("data", "json_blob", true)]
    [InlineData("data", "bson_data", true)]
    [InlineData("data", "msgpack_payload", true)]
    [InlineData("data", "content", true)]
    [InlineData("data", "payload", true)]
    [InlineData("data", "name", false)]
    public void BinaryJsonDecodeTransformer_CanApply_ShouldDetectBinaryJsonColumns(string table, string column, bool expected)
    {
        // Arrange
        var transformer = new BinaryJsonDecodeTransformer(new Dictionary<string, string>());
        var context = new CellContext(table, column, 0, SqliteAffinity.Blob);

        // Act
        var result = transformer.CanApply(context);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BinaryJsonDecodeTransformer_ShouldHandleGzipCompression()
    {
        // Arrange
        var transformer = new BinaryJsonDecodeTransformer(new Dictionary<string, string>
        {
            ["encoding"] = "base64",
            ["compression"] = "gzip"
        });
        var context = new CellContext("data", "json_blob", 0, SqliteAffinity.Blob);
        
        // Create gzip compressed JSON
        var originalJson = @"{""compressed"":true,""method"":""gzip""}";
        var jsonBytes = Encoding.UTF8.GetBytes(originalJson);
        
        using var output = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
        {
            gzip.Write(jsonBytes);
        }
        
        var compressedBase64 = Convert.ToBase64String(output.ToArray());

        // Act
        var result = transformer.Transform(context, compressedBase64);

        // Assert
        Assert.Contains("compressed", result);
        Assert.Contains("gzip", result);
    }

    [Fact]
    public void BinaryJsonDecodeTransformer_ShouldHandleInvalidData()
    {
        // Arrange
        var transformer = new BinaryJsonDecodeTransformer(new Dictionary<string, string>());
        var context = new CellContext("data", "json_blob", 0, SqliteAffinity.Blob);

        // Act & Assert
        Assert.Equal("invalid-base64!", transformer.Transform(context, "invalid-base64!"));
        Assert.Equal("", transformer.Transform(context, ""));
        Assert.Null(transformer.Transform(context, null));
    }
}

public class BinaryJsonEncodeTransformerTests
{
    [Fact]
    public void BinaryJsonEncodeTransformer_ShouldEncodeToBase64()
    {
        // Arrange
        var transformer = new BinaryJsonEncodeTransformer(new Dictionary<string, string>
        {
            ["encoding"] = "base64",
            ["compact"] = "true"
        });
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);
        var input = @"{""name"": ""John"", ""age"": 30}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.NotNull(result);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(result));
        Assert.Equal(@"{""name"":""John"",""age"":30}", decoded);
    }

    [Fact]
    public void BinaryJsonEncodeTransformer_ShouldEncodeToHex()
    {
        // Arrange
        var transformer = new BinaryJsonEncodeTransformer(new Dictionary<string, string>
        {
            ["encoding"] = "hex",
            ["compact"] = "true"
        });
        var context = new CellContext("users", "config_json", 0, SqliteAffinity.Text);
        var input = @"{""enabled"":true}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.NotNull(result);
        var decoded = Encoding.UTF8.GetString(Convert.FromHexString(result.ToUpperInvariant()));
        Assert.Equal(@"{""enabled"":true}", decoded);
    }

    [Fact]
    public void BinaryJsonEncodeTransformer_ShouldCompressWithGzip()
    {
        // Arrange
        var transformer = new BinaryJsonEncodeTransformer(new Dictionary<string, string>
        {
            ["encoding"] = "base64",
            ["compression"] = "gzip",
            ["compact"] = "true"
        });
        var context = new CellContext("logs", "data_json", 0, SqliteAffinity.Text);
        
        // Large JSON that will benefit from compression
        var largeJson = @"{""message"":""" + new string('A', 1000) + @""",""repeated"":true}";

        // Act
        var result = transformer.Transform(context, largeJson);

        // Assert
        Assert.NotNull(result);
        // Compressed should be significantly smaller than original base64
        var uncompressedBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(largeJson));
        Assert.True(result.Length < uncompressedBase64.Length);
    }

    [Theory]
    [InlineData("users", "profile_json", true)]
    [InlineData("config", "settings_data", true)]
    [InlineData("logs", "json_config", true)]
    [InlineData("users", "name", false)]
    [InlineData("users", "id", false)]
    public void BinaryJsonEncodeTransformer_CanApply_ShouldDetectJsonColumns(string table, string column, bool expected)
    {
        // Arrange
        var transformer = new BinaryJsonEncodeTransformer(new Dictionary<string, string>());
        var context = new CellContext(table, column, 0, SqliteAffinity.Text);

        // Act
        var result = transformer.CanApply(context);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BinaryJsonEncodeTransformer_ShouldHandleInvalidJson()
    {
        // Arrange
        var transformer = new BinaryJsonEncodeTransformer(new Dictionary<string, string>
        {
            ["encoding"] = "base64"
        });
        var context = new CellContext("users", "data_json", 0, SqliteAffinity.Text);

        // Act & Assert - Should still encode invalid JSON as text
        var result = transformer.Transform(context, "not-json");
        Assert.NotNull(result);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(result));
        Assert.Equal("not-json", decoded);
    }
}

public class BinaryJsonIntegrationTests
{
    [Fact]
    public void BinaryJsonTransformers_ShouldRoundTrip()
    {
        // Arrange
        var registry = new TransformerRegistry();
        registry.Register("binary-json-encode", config => new BinaryJsonEncodeTransformer(config));
        registry.Register("binary-json-decode", config => new BinaryJsonDecodeTransformer(config));

        var originalJson = @"{""user"":{""name"":""John"",""preferences"":{""theme"":""dark"",""notifications"":true}}}";
        
        var encodeConfig = new Dictionary<string, string>
        {
            ["encoding"] = "base64",
            ["compression"] = "gzip",
            ["compact"] = "true"
        };
        
        var decodeConfig = new Dictionary<string, string>
        {
            ["encoding"] = "base64",
            ["compression"] = "gzip",
            ["format"] = "pretty"
        };

        var encodeContext = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);
        var decodeContext = new CellContext("users", "json_blob", 0, SqliteAffinity.Blob);

        // Act
        var encoder = registry.CreateCell("binary-json-encode", encodeConfig);
        var encoded = encoder.Transform(encodeContext, originalJson);
        
        var decoder = registry.CreateCell("binary-json-decode", decodeConfig);
        var decoded = decoder.Transform(decodeContext, encoded);

        // Assert
        Assert.NotEqual(originalJson, encoded); // Should be different (encoded)
        Assert.Contains("user", decoded); // Should contain original data
        Assert.Contains("preferences", decoded);
        Assert.Contains("notifications", decoded);
    }

    [Fact]
    public void BinaryJsonTransformers_ShouldBeRegisterable()
    {
        // Arrange
        var registry = new TransformerRegistry();

        // Act
        registry.Register("binary-json-decode", config => new BinaryJsonDecodeTransformer(config));
        registry.Register("binary-json-encode", config => new BinaryJsonEncodeTransformer(config));

        // Assert
        Assert.True(registry.IsRegistered("binary-json-decode"));
        Assert.True(registry.IsRegistered("binary-json-encode"));
    }

    [Fact]
    public void BinaryJsonDecoder_ShouldHandleRealWorldData()
    {
        // Arrange - Simulate real-world base64 encoded JSON from a database
        var realWorldJson = @"{
  ""userId"": 12345,
  ""sessionData"": {
    ""loginTime"": ""2023-08-19T10:30:00Z"",
    ""ipAddress"": ""192.168.1.100"",
    ""userAgent"": ""Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"",
    ""preferences"": {
      ""language"": ""en-US"",
      ""timezone"": ""America/New_York"",
      ""features"": [""darkMode"", ""notifications"", ""analytics""]
    }
  },
  ""metadata"": {
    ""version"": ""1.2.3"",
    ""environment"": ""production""
  }
}";

        var transformer = new BinaryJsonDecodeTransformer(new Dictionary<string, string>
        {
            ["encoding"] = "base64",
            ["format"] = "compact"
        });
        
        var context = new CellContext("sessions", "session_data", 0, SqliteAffinity.Blob);
        var encodedData = Convert.ToBase64String(Encoding.UTF8.GetBytes(realWorldJson));

        // Act
        var result = transformer.Transform(context, encodedData);

        // Assert
        Assert.Contains("userId", result);
        Assert.Contains("sessionData", result);
        Assert.Contains("preferences", result);
        Assert.Contains("darkMode", result);
        Assert.Contains("production", result);
        
        // Should be valid JSON
        var exception = Record.Exception(() => System.Text.Json.JsonDocument.Parse(result));
        Assert.Null(exception);
    }
}