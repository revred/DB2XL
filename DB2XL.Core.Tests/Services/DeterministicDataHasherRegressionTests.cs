using DB2XL.Core.Services;
using System.Security.Cryptography;
using System.Reflection;
using Xunit;

namespace DB2XL.Core.Tests.Services;

/// <summary>
/// Comprehensive regression tests for DeterministicDataHasher to detect hash consistency issues
/// </summary>
public class DeterministicDataHasherRegressionTests
{
    #region Helper Methods

    private static object CreateDeterministicDataHasher(HashAlgorithm algorithm)
    {
        // Access internal class via reflection for testing
        var hasherType = typeof(BundleHashCalculator).Assembly
            .GetType("DB2XL.Core.Services.DeterministicDataHasher");
        
        if (hasherType == null)
            throw new InvalidOperationException("DeterministicDataHasher type not found");
            
        return Activator.CreateInstance(hasherType, algorithm)
            ?? throw new InvalidOperationException("Failed to create DeterministicDataHasher instance");
    }

    private static void CallInitialize(object hasher, string tableName)
    {
        var method = hasher.GetType().GetMethod("Initialize", BindingFlags.Public | BindingFlags.Instance);
        method?.Invoke(hasher, new object[] { tableName });
    }

    private static void CallProcessRow(object hasher, IReadOnlyDictionary<string, object?> row)
    {
        var method = hasher.GetType().GetMethod("ProcessRow", BindingFlags.Public | BindingFlags.Instance);
        method?.Invoke(hasher, new object[] { row });
    }

    private static string CallFinalize(object hasher)
    {
        var method = hasher.GetType().GetMethod("Finalize", BindingFlags.Public | BindingFlags.Instance);
        return (string)(method?.Invoke(hasher, null) ?? throw new InvalidOperationException("Finalize failed"));
    }

    #endregion

    #region Deterministic Hash Tests

    [Fact]
    public void DeterministicDataHasher_SameData_ProducesSameHash()
    {
        // Critical: Same data must always produce the same hash
        using var sha256_1 = SHA256.Create();
        using var sha256_2 = SHA256.Create();
        
        var hasher1 = CreateDeterministicDataHasher(sha256_1);
        var hasher2 = CreateDeterministicDataHasher(sha256_2);
        
        var testData = new Dictionary<string, object?>
        {
            ["id"] = 1,
            ["name"] = "test",
            ["value"] = 42.5,
            ["active"] = true
        };
        
        // Process same data with both hashers
        CallInitialize(hasher1, "test_table");
        CallProcessRow(hasher1, testData);
        var hash1 = CallFinalize(hasher1);
        
        CallInitialize(hasher2, "test_table");
        CallProcessRow(hasher2, testData);
        var hash2 = CallFinalize(hasher2);
        
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void DeterministicDataHasher_DifferentColumnOrder_ProducesSameHash()
    {
        // Critical: Column order should not affect hash (deterministic ordering)
        using var sha256_1 = SHA256.Create();
        using var sha256_2 = SHA256.Create();
        
        var hasher1 = CreateDeterministicDataHasher(sha256_1);
        var hasher2 = CreateDeterministicDataHasher(sha256_2);
        
        var testData1 = new Dictionary<string, object?>
        {
            ["id"] = 1,
            ["name"] = "test",
            ["value"] = 42
        };
        
        var testData2 = new Dictionary<string, object?>
        {
            ["value"] = 42,
            ["id"] = 1,
            ["name"] = "test"
        };
        
        CallInitialize(hasher1, "test_table");
        CallProcessRow(hasher1, testData1);
        var hash1 = CallFinalize(hasher1);
        
        CallInitialize(hasher2, "test_table");
        CallProcessRow(hasher2, testData2);
        var hash2 = CallFinalize(hasher2);
        
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void DeterministicDataHasher_DifferentTableNames_ProduceDifferentHashes()
    {
        // Security: Different table names should produce different hashes
        using var sha256_1 = SHA256.Create();
        using var sha256_2 = SHA256.Create();
        
        var hasher1 = CreateDeterministicDataHasher(sha256_1);
        var hasher2 = CreateDeterministicDataHasher(sha256_2);
        
        var testData = new Dictionary<string, object?>
        {
            ["id"] = 1,
            ["name"] = "test"
        };
        
        CallInitialize(hasher1, "table1");
        CallProcessRow(hasher1, testData);
        var hash1 = CallFinalize(hasher1);
        
        CallInitialize(hasher2, "table2");
        CallProcessRow(hasher2, testData);
        var hash2 = CallFinalize(hasher2);
        
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void DeterministicDataHasher_DifferentDataValues_ProduceDifferentHashes()
    {
        // Regression: Different data values should produce different hashes
        using var sha256_1 = SHA256.Create();
        using var sha256_2 = SHA256.Create();
        
        var hasher1 = CreateDeterministicDataHasher(sha256_1);
        var hasher2 = CreateDeterministicDataHasher(sha256_2);
        
        var testData1 = new Dictionary<string, object?>
        {
            ["id"] = 1,
            ["name"] = "test1"
        };
        
        var testData2 = new Dictionary<string, object?>
        {
            ["id"] = 1,
            ["name"] = "test2"
        };
        
        CallInitialize(hasher1, "test_table");
        CallProcessRow(hasher1, testData1);
        var hash1 = CallFinalize(hasher1);
        
        CallInitialize(hasher2, "test_table");
        CallProcessRow(hasher2, testData2);
        var hash2 = CallFinalize(hasher2);
        
        Assert.NotEqual(hash1, hash2);
    }

    #endregion

    #region Null Value Tests

    [Fact]
    public void DeterministicDataHasher_NullValues_HandledConsistently()
    {
        // Critical: Null values should be handled consistently
        using var sha256_1 = SHA256.Create();
        using var sha256_2 = SHA256.Create();
        
        var hasher1 = CreateDeterministicDataHasher(sha256_1);
        var hasher2 = CreateDeterministicDataHasher(sha256_2);
        
        var testData1 = new Dictionary<string, object?>
        {
            ["id"] = 1,
            ["name"] = null,
            ["value"] = DBNull.Value
        };
        
        var testData2 = new Dictionary<string, object?>
        {
            ["id"] = 1,
            ["name"] = null,
            ["value"] = DBNull.Value
        };
        
        CallInitialize(hasher1, "test_table");
        CallProcessRow(hasher1, testData1);
        var hash1 = CallFinalize(hasher1);
        
        CallInitialize(hasher2, "test_table");
        CallProcessRow(hasher2, testData2);
        var hash2 = CallFinalize(hasher2);
        
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void DeterministicDataHasher_NullVsEmpty_ProduceDifferentHashes()
    {
        // Regression: Null and empty string should produce different hashes
        using var sha256_1 = SHA256.Create();
        using var sha256_2 = SHA256.Create();
        
        var hasher1 = CreateDeterministicDataHasher(sha256_1);
        var hasher2 = CreateDeterministicDataHasher(sha256_2);
        
        var testData1 = new Dictionary<string, object?>
        {
            ["id"] = 1,
            ["name"] = null
        };
        
        var testData2 = new Dictionary<string, object?>
        {
            ["id"] = 1,
            ["name"] = ""
        };
        
        CallInitialize(hasher1, "test_table");
        CallProcessRow(hasher1, testData1);
        var hash1 = CallFinalize(hasher1);
        
        CallInitialize(hasher2, "test_table");
        CallProcessRow(hasher2, testData2);
        var hash2 = CallFinalize(hasher2);
        
        Assert.NotEqual(hash1, hash2);
    }

    #endregion

    #region Data Type Tests

    [Fact]
    public void DeterministicDataHasher_NumericTypes_ConvertedConsistently()
    {
        // Critical: Same numeric values should produce same hash regardless of type
        using var sha256_1 = SHA256.Create();
        using var sha256_2 = SHA256.Create();
        using var sha256_3 = SHA256.Create();
        
        var hasher1 = CreateDeterministicDataHasher(sha256_1);
        var hasher2 = CreateDeterministicDataHasher(sha256_2);
        var hasher3 = CreateDeterministicDataHasher(sha256_3);
        
        var testData1 = new Dictionary<string, object?> { ["value"] = 42 };      // int
        var testData2 = new Dictionary<string, object?> { ["value"] = 42L };     // long  
        var testData3 = new Dictionary<string, object?> { ["value"] = 42.5 };    // double with decimal
        
        CallInitialize(hasher1, "test_table");
        CallProcessRow(hasher1, testData1);
        var hash1 = CallFinalize(hasher1);
        
        CallInitialize(hasher2, "test_table");
        CallProcessRow(hasher2, testData2);
        var hash2 = CallFinalize(hasher2);
        
        CallInitialize(hasher3, "test_table");
        CallProcessRow(hasher3, testData3);
        var hash3 = CallFinalize(hasher3);
        
        // Int and long with same value should produce same hash due to ToString()
        Assert.Equal(hash1, hash2);
        // Double with decimal should produce different hash
        Assert.NotEqual(hash1, hash3);
        Assert.NotEqual(hash2, hash3);
    }

    [Fact]
    public void DeterministicDataHasher_BooleanValues_ConvertedConsistently()
    {
        // Regression: Boolean values should convert consistently
        using var sha256_1 = SHA256.Create();
        using var sha256_2 = SHA256.Create();
        
        var hasher1 = CreateDeterministicDataHasher(sha256_1);
        var hasher2 = CreateDeterministicDataHasher(sha256_2);
        
        var testData1 = new Dictionary<string, object?> { ["active"] = true };
        var testData2 = new Dictionary<string, object?> { ["active"] = true };
        
        CallInitialize(hasher1, "test_table");
        CallProcessRow(hasher1, testData1);
        var hash1 = CallFinalize(hasher1);
        
        CallInitialize(hasher2, "test_table");
        CallProcessRow(hasher2, testData2);
        var hash2 = CallFinalize(hasher2);
        
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void DeterministicDataHasher_DateTimeValues_ConvertedConsistently()
    {
        // Critical: DateTime values should use invariant culture
        using var sha256_1 = SHA256.Create();
        using var sha256_2 = SHA256.Create();
        
        var hasher1 = CreateDeterministicDataHasher(sha256_1);
        var hasher2 = CreateDeterministicDataHasher(sha256_2);
        
        var dateTime = new DateTime(2021, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var testData1 = new Dictionary<string, object?> { ["created"] = dateTime };
        var testData2 = new Dictionary<string, object?> { ["created"] = dateTime };
        
        CallInitialize(hasher1, "test_table");
        CallProcessRow(hasher1, testData1);
        var hash1 = CallFinalize(hasher1);
        
        CallInitialize(hasher2, "test_table");
        CallProcessRow(hasher2, testData2);
        var hash2 = CallFinalize(hasher2);
        
        Assert.Equal(hash1, hash2);
    }

    #endregion

    #region Multiple Row Tests

    [Fact]
    public void DeterministicDataHasher_MultipleRows_ProcessedCorrectly()
    {
        // Regression: Multiple rows should be processed in order
        using var sha256_1 = SHA256.Create();
        using var sha256_2 = SHA256.Create();
        
        var hasher1 = CreateDeterministicDataHasher(sha256_1);
        var hasher2 = CreateDeterministicDataHasher(sha256_2);
        
        var row1 = new Dictionary<string, object?> { ["id"] = 1, ["name"] = "first" };
        var row2 = new Dictionary<string, object?> { ["id"] = 2, ["name"] = "second" };
        
        // Process rows in same order
        CallInitialize(hasher1, "test_table");
        CallProcessRow(hasher1, row1);
        CallProcessRow(hasher1, row2);
        var hash1 = CallFinalize(hasher1);
        
        CallInitialize(hasher2, "test_table");
        CallProcessRow(hasher2, row1);
        CallProcessRow(hasher2, row2);
        var hash2 = CallFinalize(hasher2);
        
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void DeterministicDataHasher_RowOrder_AffectsHash()
    {
        // Critical: Row order should affect the hash
        using var sha256_1 = SHA256.Create();
        using var sha256_2 = SHA256.Create();
        
        var hasher1 = CreateDeterministicDataHasher(sha256_1);
        var hasher2 = CreateDeterministicDataHasher(sha256_2);
        
        var row1 = new Dictionary<string, object?> { ["id"] = 1, ["name"] = "first" };
        var row2 = new Dictionary<string, object?> { ["id"] = 2, ["name"] = "second" };
        
        // Process rows in different order
        CallInitialize(hasher1, "test_table");
        CallProcessRow(hasher1, row1);
        CallProcessRow(hasher1, row2);
        var hash1 = CallFinalize(hasher1);
        
        CallInitialize(hasher2, "test_table");
        CallProcessRow(hasher2, row2);
        CallProcessRow(hasher2, row1);
        var hash2 = CallFinalize(hasher2);
        
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void DeterministicDataHasher_EmptyTable_ProducesValidHash()
    {
        // Edge case: Empty table should produce valid hash
        using var sha256 = SHA256.Create();
        var hasher = CreateDeterministicDataHasher(sha256);
        
        CallInitialize(hasher, "empty_table");
        var hash = CallFinalize(hasher);
        
        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
        Assert.Equal(64, hash.Length); // SHA256 produces 64 hex characters
    }

    #endregion

    #region Unicode and Special Character Tests

    [Fact]
    public void DeterministicDataHasher_UnicodeText_HandledCorrectly()
    {
        // Critical: Unicode text should be handled consistently
        using var sha256_1 = SHA256.Create();
        using var sha256_2 = SHA256.Create();
        
        var hasher1 = CreateDeterministicDataHasher(sha256_1);
        var hasher2 = CreateDeterministicDataHasher(sha256_2);
        
        var testData1 = new Dictionary<string, object?>
        {
            ["name"] = "测试数据", // Chinese characters
            ["emoji"] = "🔥🚀", // Emojis
            ["rtl"] = "مرحبا" // Arabic text
        };
        
        var testData2 = new Dictionary<string, object?>
        {
            ["name"] = "测试数据",
            ["emoji"] = "🔥🚀",
            ["rtl"] = "مرحبا"
        };
        
        CallInitialize(hasher1, "unicode_table");
        CallProcessRow(hasher1, testData1);
        var hash1 = CallFinalize(hasher1);
        
        CallInitialize(hasher2, "unicode_table");
        CallProcessRow(hasher2, testData2);
        var hash2 = CallFinalize(hasher2);
        
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void DeterministicDataHasher_SpecialCharacters_HandledCorrectly()
    {
        // Security: Special characters should not break hashing
        using var sha256 = SHA256.Create();
        var hasher = CreateDeterministicDataHasher(sha256);
        
        var testData = new Dictionary<string, object?>
        {
            ["field1"] = "value\x00with\x1Fnull", // Contains null and field separator
            ["field2"] = "value\x1Ewith\x1Erecord", // Contains record separator
            ["field3"] = "\"quoted'value`" // Quotes and special chars
        };
        
        CallInitialize(hasher, "special_table");
        CallProcessRow(hasher, testData);
        var hash = CallFinalize(hasher);
        
        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
        Assert.Equal(64, hash.Length);
    }

    #endregion

    #region Performance and Scale Tests

    [Fact]
    public void DeterministicDataHasher_LargeRows_ProcessedEfficiently()
    {
        // Performance: Large rows should be processed without issues
        using var sha256 = SHA256.Create();
        var hasher = CreateDeterministicDataHasher(sha256);
        
        var largeRow = new Dictionary<string, object?>();
        
        // Create a row with 100 columns
        for (int i = 0; i < 100; i++)
        {
            largeRow[$"column_{i:D3}"] = $"value_{i}";
        }
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        CallInitialize(hasher, "large_table");
        CallProcessRow(hasher, largeRow);
        var hash = CallFinalize(hasher);
        
        stopwatch.Stop();
        
        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length);
        Assert.True(stopwatch.ElapsedMilliseconds < 100, "Large row processing took too long");
    }

    [Fact]
    public void DeterministicDataHasher_LongStrings_ProcessedCorrectly()
    {
        // Edge case: Very long strings should be handled
        using var sha256 = SHA256.Create();
        var hasher = CreateDeterministicDataHasher(sha256);
        
        var longString = new string('A', 10000); // 10KB string
        var testData = new Dictionary<string, object?>
        {
            ["id"] = 1,
            ["long_text"] = longString
        };
        
        CallInitialize(hasher, "long_string_table");
        CallProcessRow(hasher, testData);
        var hash = CallFinalize(hasher);
        
        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void DeterministicDataHasher_ConstructorWithNullAlgorithm_ThrowsException()
    {
        // Error handling: Null algorithm should throw
        Assert.Throws<TargetInvocationException>(() => CreateDeterministicDataHasher(null!));
    }

    [Fact]
    public void DeterministicDataHasher_EmptyTableName_HandledCorrectly()
    {
        // Edge case: Empty table name should be handled
        using var sha256 = SHA256.Create();
        var hasher = CreateDeterministicDataHasher(sha256);
        
        CallInitialize(hasher, "");
        var hash = CallFinalize(hasher);
        
        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length);
    }

    #endregion

    #region Cross-Platform Consistency Tests

    [Fact]
    public void DeterministicDataHasher_InvariantCulture_ProducesConsistentResults()
    {
        // Critical: Results should be consistent across different cultures
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        
        try
        {
            // Test with different cultures
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            
            using var sha256_1 = SHA256.Create();
            var hasher1 = CreateDeterministicDataHasher(sha256_1);
            
            var testData = new Dictionary<string, object?>
            {
                ["decimal_value"] = 123.45m,
                ["double_value"] = 67.89,
                ["date_value"] = new DateTime(2021, 12, 25)
            };
            
            CallInitialize(hasher1, "culture_test");
            CallProcessRow(hasher1, testData);
            var hash1 = CallFinalize(hasher1);
            
            // Change culture
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            
            using var sha256_2 = SHA256.Create();
            var hasher2 = CreateDeterministicDataHasher(sha256_2);
            
            CallInitialize(hasher2, "culture_test");
            CallProcessRow(hasher2, testData);
            var hash2 = CallFinalize(hasher2);
            
            Assert.Equal(hash1, hash2);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        }
    }

    #endregion
}