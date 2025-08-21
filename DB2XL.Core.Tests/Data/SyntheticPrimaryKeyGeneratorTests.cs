using DB2XL.Core.Utilities;

namespace DB2XL.Core.Tests.Data;

public class SyntheticPrimaryKeyGeneratorTests
{
    [Fact]
    public void GenerateRowHash_WithSameValues_ReturnsSameHash()
    {
        // Arrange
        var values1 = new object?[] { 1, "test", 3.14 };
        var values2 = new object?[] { 1, "test", 3.14 };

        // Act
        var hash1 = SyntheticPrimaryKeyGenerator.GenerateRowHash(values1);
        var hash2 = SyntheticPrimaryKeyGenerator.GenerateRowHash(values2);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void GenerateRowHash_WithDifferentValues_ReturnsDifferentHashes()
    {
        // Arrange
        var values1 = new object?[] { 1, "test", 3.14 };
        var values2 = new object?[] { 2, "test", 3.14 };

        // Act
        var hash1 = SyntheticPrimaryKeyGenerator.GenerateRowHash(values1);
        var hash2 = SyntheticPrimaryKeyGenerator.GenerateRowHash(values2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void GenerateRowHash_WithNullValues_HandlesCorrectly()
    {
        // Arrange
        var valuesWithNull = new object?[] { 1, null, "test" };
        var valuesWithoutNull = new object?[] { 1, "", "test" };

        // Act
        var hashWithNull = SyntheticPrimaryKeyGenerator.GenerateRowHash(valuesWithNull);
        var hashWithoutNull = SyntheticPrimaryKeyGenerator.GenerateRowHash(valuesWithoutNull);

        // Assert
        Assert.NotEqual(hashWithNull, hashWithoutNull);
        Assert.NotEmpty(hashWithNull);
        Assert.NotEmpty(hashWithoutNull);
    }

    [Fact]
    public void GenerateRowHash_WithEmptyArray_ReturnsValidHash()
    {
        // Arrange
        var emptyValues = Array.Empty<object?>();

        // Act
        var hash = SyntheticPrimaryKeyGenerator.GenerateRowHash(emptyValues);

        // Assert
        Assert.NotEmpty(hash);
        Assert.True(hash.Length > 0);
    }

    [Fact]
    public void GenerateRowHash_WithSingleValue_ReturnsValidHash()
    {
        // Arrange
        var singleValue = new object?[] { "single" };

        // Act
        var hash = SyntheticPrimaryKeyGenerator.GenerateRowHash(singleValue);

        // Assert
        Assert.NotEmpty(hash);
        Assert.True(hash.Length > 0);
    }

    [Fact]
    public void GenerateRowHash_WithMixedTypes_HandlesCorrectly()
    {
        // Arrange
        var mixedValues = new object?[] 
        { 
            42,                    // int
            3.14159,              // double  
            "string",             // string
            true,                 // bool
            DateTime.Now,         // DateTime
            null,                 // null
            new byte[] { 1, 2, 3 } // byte array
        };

        // Act
        var hash = SyntheticPrimaryKeyGenerator.GenerateRowHash(mixedValues);

        // Assert
        Assert.NotEmpty(hash);
        Assert.True(hash.Length > 0);
        
        // Verify it's uppercase hex (SHA256 is 64 hex chars)
        Assert.Equal(64, hash.Length);
        Assert.True(hash.All(c => char.IsDigit(c) || (c >= 'A' && c <= 'F')));
    }

    [Fact]
    public void GenerateRowHash_WithDifferentOrder_ReturnsDifferentHashes()
    {
        // Arrange
        var values1 = new object?[] { "first", "second" };
        var values2 = new object?[] { "second", "first" };

        // Act
        var hash1 = SyntheticPrimaryKeyGenerator.GenerateRowHash(values1);
        var hash2 = SyntheticPrimaryKeyGenerator.GenerateRowHash(values2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void GenerateRowHash_IsDeterministic_AcrossMultipleCalls()
    {
        // Arrange
        var values = new object?[] { 123, "test", 45.67, null, true };

        // Act
        var hashes = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            hashes.Add(SyntheticPrimaryKeyGenerator.GenerateRowHash(values));
        }

        // Assert
        Assert.True(hashes.All(h => h == hashes[0]), "All hashes should be identical");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("test")]
    [InlineData("unicode: 🔥 test 中文")]
    [InlineData("special chars: !@#$%^&*()")]
    public void GenerateRowHash_WithVariousStrings_ReturnsValidHashes(string testString)
    {
        // Arrange
        var values = new object?[] { testString };

        // Act
        var hash = SyntheticPrimaryKeyGenerator.GenerateRowHash(values);

        // Assert
        Assert.NotEmpty(hash);
        Assert.Equal(64, hash.Length); // SHA256 hex length
        Assert.True(hash.All(c => char.IsDigit(c) || (c >= 'A' && c <= 'F')));
    }
}