using DB2XL.Data.Checksum;
using Xunit;

namespace DB2XL.Data.Tests.Checksum;

/// <summary>
/// Comprehensive tests for DataChecksumCalculator to achieve >60% coverage
/// </summary>
public class DataChecksumCalculatorTests
{
    [Fact]
    public void Constructor_CreatesValidInstance()
    {
        // Act
        using var calculator = new DataChecksumCalculator();

        // Assert
        Assert.NotNull(calculator);
    }

    [Fact]
    public void AddField_WithNullValue_AddsNullMarker()
    {
        // Arrange
        using var calculator = new DataChecksumCalculator();

        // Act & Assert (should not throw)
        calculator.AddField(null);
        var checksum = calculator.GetChecksum();
        
        Assert.NotNull(checksum);
        Assert.NotEmpty(checksum);
    }

    [Fact]
    public void AddField_WithStringValue_AddsValueToBuffer()
    {
        // Arrange
        using var calculator = new DataChecksumCalculator();

        // Act
        calculator.AddField("test");
        var checksum = calculator.GetChecksum();

        // Assert
        Assert.NotNull(checksum);
        Assert.NotEmpty(checksum);
        Assert.Equal(64, checksum.Length); // SHA256 hash is 64 hex characters
    }

    [Fact]
    public void AddField_WithEmptyString_AddsEmptyValue()
    {
        // Arrange
        using var calculator = new DataChecksumCalculator();

        // Act
        calculator.AddField("");
        var checksum = calculator.GetChecksum();

        // Assert
        Assert.NotNull(checksum);
        Assert.NotEmpty(checksum);
    }

    [Fact]
    public void EndRow_MarksRowBoundary()
    {
        // Arrange
        using var calculator = new DataChecksumCalculator();

        // Act
        calculator.AddField("field1");
        calculator.AddField("field2");
        calculator.EndRow();
        var checksum = calculator.GetChecksum();

        // Assert
        Assert.NotNull(checksum);
        Assert.NotEmpty(checksum);
    }

    [Fact]
    public void GetChecksum_ProducesDeterministicResult()
    {
        // Arrange
        using var calculator1 = new DataChecksumCalculator();
        using var calculator2 = new DataChecksumCalculator();

        // Act - Same data should produce same checksum
        calculator1.AddField("test");
        calculator1.AddField("data");
        calculator1.EndRow();
        var checksum1 = calculator1.GetChecksum();

        calculator2.AddField("test");
        calculator2.AddField("data");
        calculator2.EndRow();
        var checksum2 = calculator2.GetChecksum();

        // Assert
        Assert.Equal(checksum1, checksum2);
    }

    [Fact]
    public void GetChecksum_DifferentDataProducesDifferentChecksum()
    {
        // Arrange
        using var calculator1 = new DataChecksumCalculator();
        using var calculator2 = new DataChecksumCalculator();

        // Act
        calculator1.AddField("test1");
        calculator1.EndRow();
        var checksum1 = calculator1.GetChecksum();

        calculator2.AddField("test2");
        calculator2.EndRow();
        var checksum2 = calculator2.GetChecksum();

        // Assert
        Assert.NotEqual(checksum1, checksum2);
    }

    [Fact]
    public void Reset_ClearsBuffer()
    {
        // Arrange
        using var calculator = new DataChecksumCalculator();
        calculator.AddField("test");
        calculator.EndRow();
        var checksumBefore = calculator.GetChecksum();

        // Act
        calculator.Reset();
        var checksumAfter = calculator.GetChecksum();

        // Assert
        Assert.NotEqual(checksumBefore, checksumAfter);
    }

    [Fact]
    public void Reset_AllowsReuse()
    {
        // Arrange
        using var calculator = new DataChecksumCalculator();

        // Act - First calculation
        calculator.AddField("test1");
        calculator.EndRow();
        var checksum1 = calculator.GetChecksum();

        // Reset and second calculation
        calculator.Reset();
        calculator.AddField("test2");
        calculator.EndRow();
        var checksum2 = calculator.GetChecksum();

        // Assert
        Assert.NotEqual(checksum1, checksum2);
    }

    [Fact]
    public void MultipleFields_ProducesCorrectChecksum()
    {
        // Arrange
        using var calculator = new DataChecksumCalculator();

        // Act
        calculator.AddField("field1");
        calculator.AddField("field2");
        calculator.AddField(null);
        calculator.AddField("field4");
        calculator.EndRow();
        var checksum = calculator.GetChecksum();

        // Assert
        Assert.NotNull(checksum);
        Assert.Equal(64, checksum.Length);
    }

    [Fact]
    public void MultipleRows_ProducesCorrectChecksum()
    {
        // Arrange
        using var calculator = new DataChecksumCalculator();

        // Act
        calculator.AddField("row1field1");
        calculator.AddField("row1field2");
        calculator.EndRow();
        
        calculator.AddField("row2field1");
        calculator.AddField("row2field2");
        calculator.EndRow();
        
        var checksum = calculator.GetChecksum();

        // Assert
        Assert.NotNull(checksum);
        Assert.Equal(64, checksum.Length);
    }

    [Fact]
    public void GetChecksum_CanBeCalledMultipleTimes()
    {
        // Arrange
        using var calculator = new DataChecksumCalculator();
        calculator.AddField("test");
        calculator.EndRow();

        // Act
        var checksum1 = calculator.GetChecksum();
        var checksum2 = calculator.GetChecksum();

        // Assert
        Assert.Equal(checksum1, checksum2);
    }

    [Fact]
    public void AddField_WithUnicodeText_HandlesCorrectly()
    {
        // Arrange
        using var calculator = new DataChecksumCalculator();

        // Act
        calculator.AddField("ñáéíóú中文🚀");
        calculator.EndRow();
        var checksum = calculator.GetChecksum();

        // Assert
        Assert.NotNull(checksum);
        Assert.Equal(64, checksum.Length);
    }

    [Fact]
    public void AddField_WithLongText_HandlesCorrectly()
    {
        // Arrange
        using var calculator = new DataChecksumCalculator();
        var longText = new string('a', 10000);

        // Act
        calculator.AddField(longText);
        calculator.EndRow();
        var checksum = calculator.GetChecksum();

        // Assert
        Assert.NotNull(checksum);
        Assert.Equal(64, checksum.Length);
    }

    [Fact]
    public void Dispose_ThrowsOnSubsequentOperations()
    {
        // Arrange
        var calculator = new DataChecksumCalculator();
        calculator.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => calculator.AddField("test"));
        Assert.Throws<ObjectDisposedException>(() => calculator.EndRow());
        Assert.Throws<ObjectDisposedException>(() => calculator.GetChecksum());
        Assert.Throws<ObjectDisposedException>(() => calculator.Reset());
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var calculator = new DataChecksumCalculator();

        // Act & Assert (should not throw)
        calculator.Dispose();
        calculator.Dispose();
    }

    [Fact]
    public void FieldOrder_AffectsChecksum()
    {
        // Arrange
        using var calculator1 = new DataChecksumCalculator();
        using var calculator2 = new DataChecksumCalculator();

        // Act - Same fields in different order
        calculator1.AddField("A");
        calculator1.AddField("B");
        calculator1.EndRow();
        var checksum1 = calculator1.GetChecksum();

        calculator2.AddField("B");
        calculator2.AddField("A");
        calculator2.EndRow();
        var checksum2 = calculator2.GetChecksum();

        // Assert
        Assert.NotEqual(checksum1, checksum2);
    }

    [Fact]
    public void RowOrder_AffectsChecksum()
    {
        // Arrange
        using var calculator1 = new DataChecksumCalculator();
        using var calculator2 = new DataChecksumCalculator();

        // Act - Same rows in different order
        calculator1.AddField("row1");
        calculator1.EndRow();
        calculator1.AddField("row2");
        calculator1.EndRow();
        var checksum1 = calculator1.GetChecksum();

        calculator2.AddField("row2");
        calculator2.EndRow();
        calculator2.AddField("row1");
        calculator2.EndRow();
        var checksum2 = calculator2.GetChecksum();

        // Assert
        Assert.NotEqual(checksum1, checksum2);
    }

    [Fact]
    public void EmptyCalculator_ProducesValidChecksum()
    {
        // Arrange
        using var calculator = new DataChecksumCalculator();

        // Act
        var checksum = calculator.GetChecksum();

        // Assert
        Assert.NotNull(checksum);
        Assert.Equal(64, checksum.Length);
    }

    [Fact]
    public void GetChecksum_ReturnsSha256HexString()
    {
        // Arrange
        using var calculator = new DataChecksumCalculator();
        calculator.AddField("test");
        calculator.EndRow();

        // Act
        var checksum = calculator.GetChecksum();

        // Assert
        Assert.NotNull(checksum);
        Assert.Equal(64, checksum.Length);
        Assert.True(checksum.All(c => char.IsDigit(c) || (c >= 'A' && c <= 'F')));
    }
}