using DB2XL.Transformers;
using DB2XL.Transformers.BuiltIns;
using Xunit;
using System.Globalization;

namespace SqliteXport.Tests.Transformers;

public class JsonCompactTransformerTests
{
    [Fact]
    public void JsonCompactTransformer_ShouldCompactJson()
    {
        // Arrange
        var transformer = new JsonCompactTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);
        var input = @"{
  ""name"": ""John Doe"",
  ""age"": 30,
  ""address"": {
    ""street"": ""123 Main St"",
    ""city"": ""Anytown""
  }
}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Equal(@"{""name"":""John Doe"",""age"":30,""address"":{""street"":""123 Main St"",""city"":""Anytown""}}", result);
    }

    [Theory]
    [InlineData("users", "profile_json", true)]
    [InlineData("users", "settings_data", true)]
    [InlineData("users", "config", true)]
    [InlineData("users", "name", false)]
    public void JsonCompactTransformer_CanApply_ShouldDetectJsonColumns(string table, string column, bool expected)
    {
        // Arrange
        var transformer = new JsonCompactTransformer(new Dictionary<string, string>());
        var context = new CellContext(table, column, 0, SqliteAffinity.Text);

        // Act
        var result = transformer.CanApply(context);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void JsonCompactTransformer_ShouldHandleInvalidJson()
    {
        // Arrange
        var transformer = new JsonCompactTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);

        // Act & Assert
        Assert.Equal("not json", transformer.Transform(context, "not json"));
        Assert.Equal("{invalid}", transformer.Transform(context, "{invalid}"));
        Assert.Null(transformer.Transform(context, null));
        Assert.Equal("", transformer.Transform(context, ""));
    }

    [Fact]
    public void JsonCompactTransformer_ShouldForceApplyWhenConfigured()
    {
        // Arrange
        var config = new Dictionary<string, string> { ["forceApply"] = "true" };
        var transformer = new JsonCompactTransformer(config);
        var context = new CellContext("users", "name", 0, SqliteAffinity.Text);

        // Act
        var canApply = transformer.CanApply(context);

        // Assert
        Assert.True(canApply);
    }
}

public class JsonPrettyTransformerTests
{
    [Fact]
    public void JsonPrettyTransformer_ShouldFormatJson()
    {
        // Arrange
        var transformer = new JsonPrettyTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);
        var input = @"{""name"":""John"",""age"":30}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Contains("\"name\": \"John\"", result);
        Assert.Contains("\"age\": 30", result);
        Assert.Contains("\n", result); // Should have newlines
    }

    [Fact]
    public void JsonPrettyTransformer_ShouldHandleComplexJson()
    {
        // Arrange
        var transformer = new JsonPrettyTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);
        var input = @"{""user"":{""name"":""John"",""tags"":[""admin"",""active""]},""count"":5}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Contains("\"user\": {", result);
        Assert.Contains("\"tags\": [", result);
        Assert.Contains("\"admin\",", result);
        Assert.Contains("\"active\"", result);
    }

    [Fact]
    public void JsonPrettyTransformer_ShouldHandleInvalidJson()
    {
        // Arrange
        var transformer = new JsonPrettyTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);

        // Act & Assert
        Assert.Equal("not json", transformer.Transform(context, "not json"));
        Assert.Equal("{invalid}", transformer.Transform(context, "{invalid}"));
    }
}

public class JsonExtractTransformerTests
{
    [Fact]
    public void JsonExtractTransformer_ShouldExtractSimpleProperty()
    {
        // Arrange
        var config = new Dictionary<string, string> { ["path"] = "name" };
        var transformer = new JsonExtractTransformer(config);
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);
        var input = @"{""name"":""John Doe"",""age"":30}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Equal("\"John Doe\"", result);
    }

    [Fact]
    public void JsonExtractTransformer_ShouldExtractNestedProperty()
    {
        // Arrange
        var config = new Dictionary<string, string> { ["path"] = "address.city" };
        var transformer = new JsonExtractTransformer(config);
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);
        var input = @"{""name"":""John"",""address"":{""street"":""123 Main St"",""city"":""Anytown""}}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Equal("\"Anytown\"", result);
    }

    [Fact]
    public void JsonExtractTransformer_ShouldExtractArrayElement()
    {
        // Arrange
        var config = new Dictionary<string, string> { ["path"] = "tags[0]" };
        var transformer = new JsonExtractTransformer(config);
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);
        var input = @"{""tags"":[""admin"",""user"",""active""]}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Equal("\"admin\"", result);
    }

    [Fact]
    public void JsonExtractTransformer_ShouldExtractComplexArrayPath()
    {
        // Arrange
        var config = new Dictionary<string, string> { ["path"] = "users[1].name" };
        var transformer = new JsonExtractTransformer(config);
        var context = new CellContext("data", "users_json", 0, SqliteAffinity.Text);
        var input = @"{""users"":[{""name"":""John""},{""name"":""Jane""},{""name"":""Bob""}]}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Equal("\"Jane\"", result);
    }

    [Fact]
    public void JsonExtractTransformer_ShouldReturnDefaultForMissingPath()
    {
        // Arrange
        var config = new Dictionary<string, string> 
        { 
            ["path"] = "nonexistent", 
            ["default"] = "NOT_FOUND" 
        };
        var transformer = new JsonExtractTransformer(config);
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);
        var input = @"{""name"":""John""}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Equal("NOT_FOUND", result);
    }

    [Fact]
    public void JsonExtractTransformer_ShouldReturnOriginalWhenNoPath()
    {
        // Arrange
        var transformer = new JsonExtractTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);
        var input = @"{""name"":""John""}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Equal(input, result);
    }

    [Fact]
    public void JsonExtractTransformer_ShouldHandleInvalidJson()
    {
        // Arrange
        var config = new Dictionary<string, string> { ["path"] = "name" };
        var transformer = new JsonExtractTransformer(config);
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);

        // Act & Assert
        Assert.Equal("invalid", transformer.Transform(context, "invalid"));
        Assert.Equal("{bad}", transformer.Transform(context, "{bad}"));
    }
}

public class JsonFlattenTransformerTests
{
    [Fact]
    public void JsonFlattenTransformer_ShouldFlattenSimpleObject()
    {
        // Arrange
        var transformer = new JsonFlattenTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);
        var input = @"{""name"":""John"",""age"":30}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Contains("name=John", result);
        Assert.Contains("age=30", result);
        Assert.Contains("; ", result);
    }

    [Fact]
    public void JsonFlattenTransformer_ShouldFlattenNestedObject()
    {
        // Arrange
        var transformer = new JsonFlattenTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);
        var input = @"{""user"":{""name"":""John"",""age"":30}}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Contains("user.name=John", result);
        Assert.Contains("user.age=30", result);
    }

    [Fact]
    public void JsonFlattenTransformer_ShouldFlattenArray()
    {
        // Arrange
        var transformer = new JsonFlattenTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "tags_json", 0, SqliteAffinity.Text);
        var input = @"{""tags"":[""admin"",""user""]}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Contains("tags[0]=admin", result);
        Assert.Contains("tags[1]=user", result);
    }

    [Fact]
    public void JsonFlattenTransformer_ShouldUseCustomSeparator()
    {
        // Arrange
        var config = new Dictionary<string, string> 
        { 
            ["separator"] = "_",
            ["delimiter"] = " | "
        };
        var transformer = new JsonFlattenTransformer(config);
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);
        var input = @"{""user"":{""name"":""John""}}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Contains("user_name=John", result);
    }

    [Fact]
    public void JsonFlattenTransformer_ShouldRespectMaxDepth()
    {
        // Arrange
        var config = new Dictionary<string, string> { ["maxDepth"] = "1" };
        var transformer = new JsonFlattenTransformer(config);
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);
        var input = @"{""user"":{""details"":{""name"":""John""}}}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        // Should not include deeply nested values due to maxDepth=1
        Assert.DoesNotContain("user.details.name", result);
    }

    [Fact]
    public void JsonFlattenTransformer_ShouldHandleInvalidJson()
    {
        // Arrange
        var transformer = new JsonFlattenTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);

        // Act & Assert
        Assert.Equal("invalid", transformer.Transform(context, "invalid"));
        Assert.Equal("{bad}", transformer.Transform(context, "{bad}"));
    }
}

public class JsonValidateTransformerTests
{
    [Fact]
    public void JsonValidateTransformer_ShouldValidateValidJson()
    {
        // Arrange
        var transformer = new JsonValidateTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);
        var input = @"{""name"":""John"",""age"":30}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Equal("VALID", result);
    }

    [Fact]
    public void JsonValidateTransformer_ShouldRejectInvalidJson()
    {
        // Arrange
        var transformer = new JsonValidateTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);

        // Act & Assert
        Assert.Equal("INVALID", transformer.Transform(context, "invalid"));
        Assert.Equal("INVALID", transformer.Transform(context, "{bad}"));
        Assert.Equal("INVALID", transformer.Transform(context, "{\"missing\": quote}"));
    }

    [Fact]
    public void JsonValidateTransformer_ShouldUseCustomResults()
    {
        // Arrange
        var config = new Dictionary<string, string> 
        { 
            ["validResult"] = "OK",
            ["invalidResult"] = "ERROR",
            ["emptyResult"] = "NULL"
        };
        var transformer = new JsonValidateTransformer(config);
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);

        // Act & Assert
        Assert.Equal("OK", transformer.Transform(context, @"{""test"":true}"));
        Assert.Equal("ERROR", transformer.Transform(context, "invalid"));
        Assert.Equal("NULL", transformer.Transform(context, ""));
        Assert.Equal("NULL", transformer.Transform(context, null));
    }

    [Fact]
    public void JsonValidateTransformer_ShouldShowErrorWhenConfigured()
    {
        // Arrange
        var config = new Dictionary<string, string> 
        { 
            ["showError"] = "true",
            ["invalidResult"] = "ERROR"
        };
        var transformer = new JsonValidateTransformer(config);
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, "{\"missing\": quote}");

        // Assert
        Assert.StartsWith("ERROR:", result);
        Assert.Contains("invalid", result);
    }
}

public class JsonCountTransformerTests
{
    [Fact]
    public void JsonCountTransformer_ShouldCountObjectProperties()
    {
        // Arrange
        var transformer = new JsonCountTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);
        var input = @"{""name"":""John"",""age"":30,""email"":""john@example.com""}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Equal("3", result);
    }

    [Fact]
    public void JsonCountTransformer_ShouldCountArrayItems()
    {
        // Arrange
        var transformer = new JsonCountTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "tags_json", 0, SqliteAffinity.Text);
        var input = @"[""admin"",""user"",""active"",""verified""]";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Equal("4", result);
    }

    [Fact]
    public void JsonCountTransformer_ShouldCountPropertiesOnly()
    {
        // Arrange
        var config = new Dictionary<string, string> { ["type"] = "properties" };
        var transformer = new JsonCountTransformer(config);
        var context = new CellContext("data", "mixed_json", 0, SqliteAffinity.Text);
        var input = @"{""users"":[""John"",""Jane""],""count"":2}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Equal("2", result); // users and count properties
    }

    [Fact]
    public void JsonCountTransformer_ShouldCountItemsOnly()
    {
        // Arrange
        var config = new Dictionary<string, string> { ["type"] = "items" };
        var transformer = new JsonCountTransformer(config);
        var context = new CellContext("data", "array_json", 0, SqliteAffinity.Text);
        var input = @"{""items"":[1,2,3,4,5]}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Equal("0", result); // Root is object, not array, so 0 items
    }

    [Fact]
    public void JsonCountTransformer_ShouldCountAllElements()
    {
        // Arrange
        var config = new Dictionary<string, string> { ["type"] = "all" };
        var transformer = new JsonCountTransformer(config);
        var context = new CellContext("data", "nested_json", 0, SqliteAffinity.Text);
        var input = @"{""user"":{""name"":""John""},""tags"":[""admin""]}";

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.NotEqual("0", result);
        Assert.NotEqual("2", result); // Should be more than just top-level properties
    }

    [Fact]
    public void JsonCountTransformer_ShouldReturnZeroForInvalidJson()
    {
        // Arrange
        var transformer = new JsonCountTransformer(new Dictionary<string, string>());
        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);

        // Act & Assert
        Assert.Equal("0", transformer.Transform(context, "invalid"));
        Assert.Equal("0", transformer.Transform(context, "{bad}"));
        Assert.Equal("0", transformer.Transform(context, null));
        Assert.Equal("0", transformer.Transform(context, ""));
    }
}

public class JsonTransformersIntegrationTests
{
    [Fact]
    public void AllJsonTransformers_ShouldBeRegisterable()
    {
        // Arrange
        var registry = new TransformerRegistry();

        // Act - Register all JSON transformers
        registry.Register("json-compact", config => new JsonCompactTransformer(config));
        registry.Register("json-pretty", config => new JsonPrettyTransformer(config));
        registry.Register("json-extract", config => new JsonExtractTransformer(config));
        registry.Register("json-flatten", config => new JsonFlattenTransformer(config));
        registry.Register("json-validate", config => new JsonValidateTransformer(config));
        registry.Register("json-count", config => new JsonCountTransformer(config));

        // Assert
        Assert.True(registry.IsRegistered("json-compact"));
        Assert.True(registry.IsRegistered("json-pretty"));
        Assert.True(registry.IsRegistered("json-extract"));
        Assert.True(registry.IsRegistered("json-flatten"));
        Assert.True(registry.IsRegistered("json-validate"));
        Assert.True(registry.IsRegistered("json-count"));
        Assert.Equal(6, registry.GetRegisteredNames().Count);
    }

    [Fact]
    public void JsonTransformers_ShouldWorkInPipeline()
    {
        // Arrange
        var registry = new TransformerRegistry();
        registry.Register("json-compact", config => new JsonCompactTransformer(config));
        registry.Register("json-extract", config => new JsonExtractTransformer(config));
        registry.Register("json-validate", config => new JsonValidateTransformer(config));

        var messyJson = @"{
  ""user"": {
    ""name"": ""John Doe"",
    ""email"": ""john@example.com""
  },
  ""active"": true
}";

        var context = new CellContext("users", "profile_json", 0, SqliteAffinity.Text);

        // Act - Transform through multiple stages
        var compactTransformer = registry.CreateCell("json-compact", new Dictionary<string, string>());
        var compactedJson = compactTransformer.Transform(context, messyJson);

        var extractTransformer = registry.CreateCell("json-extract", new Dictionary<string, string> 
        { 
            ["path"] = "user.name" 
        });
        var extractedName = extractTransformer.Transform(context, compactedJson);

        var validateTransformer = registry.CreateCell("json-validate", new Dictionary<string, string>());
        var isValid = validateTransformer.Transform(context, compactedJson);

        // Assert
        Assert.DoesNotContain("\n", compactedJson); // Should be compacted
        Assert.Equal("\"John Doe\"", extractedName); // Should extract name
        Assert.Equal("VALID", isValid); // Should validate as valid JSON
    }

    [Fact]
    public void JsonTransformers_ShouldHandleComplexRealWorldData()
    {
        // Arrange
        var registry = new TransformerRegistry();
        registry.Register("json-flatten", config => new JsonFlattenTransformer(config));
        registry.Register("json-count", config => new JsonCountTransformer(config));

        // Simulate real-world JSON data from a database
        var userProfile = @"{
  ""id"": 12345,
  ""profile"": {
    ""firstName"": ""John"",
    ""lastName"": ""Doe"",
    ""email"": ""john.doe@example.com"",
    ""preferences"": {
      ""theme"": ""dark"",
      ""notifications"": {
        ""email"": true,
        ""sms"": false,
        ""push"": true
      }
    }
  },
  ""roles"": [""user"", ""admin""],
  ""metadata"": {
    ""createdAt"": ""2023-08-15T12:00:00Z"",
    ""lastLogin"": ""2023-08-19T10:30:00Z""
  }
}";

        var context = new CellContext("users", "profile_data", 0, SqliteAffinity.Text);

        // Act
        var flattenTransformer = registry.CreateCell("json-flatten", new Dictionary<string, string>());
        var flattened = flattenTransformer.Transform(context, userProfile);

        var countTransformer = registry.CreateCell("json-count", new Dictionary<string, string>());
        var propertyCount = countTransformer.Transform(context, userProfile);

        // Assert
        Assert.Contains("profile.firstName=John", flattened);
        Assert.Contains("profile.preferences.theme=dark", flattened);
        Assert.Contains("roles[0]=user", flattened);
        Assert.Contains("metadata.createdAt=", flattened);
        Assert.Equal("4", propertyCount); // id, profile, roles, metadata
    }
}