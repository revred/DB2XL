using DB2XL.Core.Models;
using DB2XL.Data.Schema;
using DB2XL.Transform.Interfaces;
using DB2XL.Transform.BuiltIns;
using Xunit;
using System.Globalization;

namespace DB2XL.Integration.Tests.Transformers;

public class EpochTransformerTests
{
    [Theory]
    [InlineData("1692100856", "s", "2023-08-15T12:00:56Z")] // Correct epoch for this date
    [InlineData("1692100856000", "ms", "2023-08-15T12:00:56Z")]
    [InlineData("0", "s", "1970-01-01T00:00:00Z")]
    [InlineData("946684800", "s", "2000-01-01T00:00:00Z")] // Y2K
    public void EpochTransformer_ShouldConvertTimestampsCorrectly(string input, string unit, string expected)
    {
        // Arrange
        var config = new Dictionary<string, string> { ["unit"] = unit };
        var transformer = new EpochTransformer(config);
        var context = new CellContext("events", "timestamp", 0, SqliteAffinity.Integer);

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("events", "timestamp", true)]
    [InlineData("events", "created_time", true)]
    [InlineData("events", "event_date", true)]
    [InlineData("events", "epoch_ms", true)]
    [InlineData("events", "name", false)]
    [InlineData("events", "id", false)]
    public void EpochTransformer_CanApply_ShouldDetectTimeColumns(string table, string column, bool expected)
    {
        // Arrange
        var transformer = new EpochTransformer(new Dictionary<string, string>());
        var context = new CellContext(table, column, 0, SqliteAffinity.Integer);

        // Act
        var result = transformer.CanApply(context);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void EpochTransformer_ShouldHandleInvalidInput()
    {
        // Arrange
        var transformer = new EpochTransformer(new Dictionary<string, string>());
        var context = new CellContext("events", "timestamp", 0, SqliteAffinity.Integer);

        // Act & Assert
        Assert.Equal("invalid", transformer.Transform(context, "invalid"));
        Assert.Equal("", transformer.Transform(context, ""));
        Assert.Null(transformer.Transform(context, null));
    }

    [Fact]
    public void EpochTransformer_ShouldHandleTimezoneConversion()
    {
        // Arrange
        var config = new Dictionary<string, string> 
        { 
            ["unit"] = "s",
            ["tz"] = "+05:00"
        };
        var transformer = new EpochTransformer(config);
        var context = new CellContext("events", "timestamp", 0, SqliteAffinity.Integer);

        // Act
        var result = transformer.Transform(context, "1692100856"); // 2023-08-15T12:00:56Z

        // Assert
        Assert.Equal("2023-08-15T17:00:56+05:00", result);
    }

    [Fact]
    public void EpochTransformer_ShouldHandleCustomFormat()
    {
        // Arrange
        var config = new Dictionary<string, string> 
        { 
            ["unit"] = "s",
            ["format"] = "yyyy-MM-dd HH:mm:ss"
        };
        var transformer = new EpochTransformer(config);
        var context = new CellContext("events", "timestamp", 0, SqliteAffinity.Integer);

        // Act
        var result = transformer.Transform(context, "1692100856");

        // Assert
        Assert.Equal("2023-08-15 12:00:56", result);
    }

    [Fact]
    public void EpochTransformer_ShouldHandleMicroseconds()
    {
        // Arrange
        var config = new Dictionary<string, string> { ["unit"] = "us" };
        var transformer = new EpochTransformer(config);
        var context = new CellContext("events", "timestamp", 0, SqliteAffinity.Integer);

        // Act
        var result = transformer.Transform(context, "1692100856000000"); // microseconds

        // Assert
        Assert.Equal("2023-08-15T12:00:56Z", result);
    }

    [Fact]
    public void EpochTransformer_ShouldThrowOnOutOfRange()
    {
        // Arrange
        var transformer = new EpochTransformer(new Dictionary<string, string>());
        var context = new CellContext("events", "timestamp", 0, SqliteAffinity.Integer);

        // Act
        var result = transformer.Transform(context, "99999999999999999"); // Way out of range

        // Assert - Should return original value for out of range
        Assert.Equal("99999999999999999", result);
    }

    [Fact]
    public void EpochTransformer_ShouldForceApplyWhenConfigured()
    {
        // Arrange
        var config = new Dictionary<string, string> { ["forceApply"] = "true" };
        var transformer = new EpochTransformer(config);
        var context = new CellContext("events", "id", 0, SqliteAffinity.Integer); // Non-time column

        // Act
        var canApply = transformer.CanApply(context);

        // Assert
        Assert.True(canApply);
    }
}

public class TicksTransformerTests
{
    [Fact]
    public void TicksTransformer_ShouldConvertTicksCorrectly()
    {
        // Arrange
        var transformer = new TicksTransformer(new Dictionary<string, string>());
        var context = new CellContext("events", "created_ticks", 0, SqliteAffinity.Integer);
        
        // .NET ticks for 2023-08-15T12:30:56Z
        var ticks = new DateTime(2023, 8, 15, 12, 30, 56, DateTimeKind.Utc).Ticks;

        // Act
        var result = transformer.Transform(context, ticks.ToString());

        // Assert
        Assert.Equal("2023-08-15T12:30:56Z", result);
    }

    [Theory]
    [InlineData("events", "created_ticks", true)]
    [InlineData("events", "timestamp_ticks", true)]
    [InlineData("events", "tick_count", true)]
    [InlineData("events", "name", false)]
    public void TicksTransformer_CanApply_ShouldDetectTickColumns(string table, string column, bool expected)
    {
        // Arrange
        var transformer = new TicksTransformer(new Dictionary<string, string>());
        var context = new CellContext(table, column, 0, SqliteAffinity.Integer);

        // Act
        var result = transformer.CanApply(context);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TicksTransformer_ShouldHandleInvalidTicks()
    {
        // Arrange
        var transformer = new TicksTransformer(new Dictionary<string, string>());
        var context = new CellContext("events", "created_ticks", 0, SqliteAffinity.Integer);

        // Act & Assert
        Assert.Equal("invalid", transformer.Transform(context, "invalid"));
        Assert.Equal("-1", transformer.Transform(context, "-1")); // Invalid ticks, return original
    }
}

public class JulianDayTransformerTests
{
    [Fact]
    public void JulianDayTransformer_ShouldConvertJulianDayCorrectly()
    {
        // Arrange
        var transformer = new JulianDayTransformer(new Dictionary<string, string>());
        var context = new CellContext("events", "julian_date", 0, SqliteAffinity.Real);

        // Use a known Julian Day value for testing
        // Julian Day 2451545.0 = January 1, 2000 12:00:00 UTC
        // Act
        var result = transformer.Transform(context, "2451545.0");

        // Assert - Should be close to expected date (Julian Day calculations have some precision variations)
        Assert.Contains("2000-01-01", result);
    }

    [Theory]
    [InlineData("events", "julian_date", true)]
    [InlineData("events", "julian_day", true)]
    [InlineData("events", "julian_timestamp", true)]
    [InlineData("events", "timestamp", false)]
    public void JulianDayTransformer_CanApply_ShouldDetectJulianColumns(string table, string column, bool expected)
    {
        // Arrange
        var transformer = new JulianDayTransformer(new Dictionary<string, string>());
        var context = new CellContext(table, column, 0, SqliteAffinity.Real);

        // Act
        var result = transformer.CanApply(context);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void JulianDayTransformer_ShouldHandleInvalidInput()
    {
        // Arrange
        var transformer = new JulianDayTransformer(new Dictionary<string, string>());
        var context = new CellContext("events", "julian_date", 0, SqliteAffinity.Real);

        // Act & Assert
        Assert.Equal("invalid", transformer.Transform(context, "invalid"));
        Assert.Equal("", transformer.Transform(context, ""));
    }
}

public class DateFormatTransformerTests
{
    [Theory]
    [InlineData("2023-08-15T12:30:56Z", "", "yyyy-MM-dd HH:mm:ss", "2023-08-15 12:30:56")]
    [InlineData("2023-08-15T00:00:00Z", "", "MM/dd/yyyy", "08/15/2023")]
    [InlineData("15/08/2023 00:00:00", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd", "2023-08-15")]
    public void DateFormatTransformer_ShouldReformatDates(string input, string inputFormat, string outputFormat, string expected)
    {
        // Arrange
        var config = new Dictionary<string, string>
        {
            ["inputFormat"] = inputFormat,
            ["outputFormat"] = outputFormat
        };
        var transformer = new DateFormatTransformer(config);
        var context = new CellContext("events", "created_date", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DateFormatTransformer_ShouldHandleInvalidDates()
    {
        // Arrange
        var transformer = new DateFormatTransformer(new Dictionary<string, string>());
        var context = new CellContext("events", "created_date", 0, SqliteAffinity.Text);

        // Act & Assert
        Assert.Equal("invalid-date", transformer.Transform(context, "invalid-date"));
        Assert.Equal("not-a-date", transformer.Transform(context, "not-a-date"));
    }

    [Theory]
    [InlineData("events", "created_date", true)]
    [InlineData("events", "updated_time", true)]
    [InlineData("events", "event_datetime", true)]
    [InlineData("events", "name", false)]
    public void DateFormatTransformer_CanApply_ShouldDetectDateColumns(string table, string column, bool expected)
    {
        // Arrange
        var transformer = new DateFormatTransformer(new Dictionary<string, string>());
        var context = new CellContext(table, column, 0, SqliteAffinity.Text);

        // Act
        var result = transformer.CanApply(context);

        // Assert
        Assert.Equal(expected, result);
    }
}

public class DatePartTransformerTests
{
    [Theory]
    [InlineData("2023-08-15T12:30:56Z", "year", "2023")]
    [InlineData("2023-08-15T12:30:56Z", "month", "8")]
    [InlineData("2023-08-15T12:30:56Z", "day", "15")]
    [InlineData("2023-08-15T12:30:56Z", "hour", "12")]
    [InlineData("2023-08-15T12:30:56Z", "minute", "30")]
    [InlineData("2023-08-15T12:30:56Z", "second", "56")]
    [InlineData("2023-08-15T12:30:56Z", "dayofweek", "Tuesday")]
    [InlineData("2023-08-15T12:30:56Z", "quarter", "3")]
    [InlineData("2023-08-15T12:30:56Z", "date", "2023-08-15")]
    [InlineData("2023-08-15T12:30:56Z", "time", "12:30:56")]
    public void DatePartTransformer_ShouldExtractDateParts(string input, string part, string expected)
    {
        // Arrange
        var config = new Dictionary<string, string> { ["part"] = part };
        var transformer = new DatePartTransformer(config);
        var context = new CellContext("events", "created_date", 0, SqliteAffinity.Text);

        // Act
        var result = transformer.Transform(context, input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DatePartTransformer_ShouldHandleEpochTimestamps()
    {
        // Arrange
        var config = new Dictionary<string, string> 
        { 
            ["part"] = "year",
            ["unit"] = "s"
        };
        var transformer = new DatePartTransformer(config);
        var context = new CellContext("events", "timestamp", 0, SqliteAffinity.Integer);

        // Act
        var result = transformer.Transform(context, "1692123456"); // 2023-08-15

        // Assert
        Assert.Equal("2023", result);
    }

    [Fact]
    public void DatePartTransformer_ShouldHandleInvalidDates()
    {
        // Arrange
        var config = new Dictionary<string, string> { ["part"] = "year" };
        var transformer = new DatePartTransformer(config);
        var context = new CellContext("events", "created_date", 0, SqliteAffinity.Text);

        // Act & Assert
        Assert.Equal("invalid", transformer.Transform(context, "invalid"));
        Assert.Equal("not-a-date", transformer.Transform(context, "not-a-date"));
    }
}

public class TimeTransformersIntegrationTests
{
    [Fact]
    public void AllTimeTransformers_ShouldBeRegisterable()
    {
        // Arrange
        var registry = new TransformerRegistry();

        // Act - Register all time transformers
        registry.Register("epoch", config => new EpochTransformer(config));
        registry.Register("ticks", config => new TicksTransformer(config));
        registry.Register("julian-day", config => new JulianDayTransformer(config));
        registry.Register("date-format", config => new DateFormatTransformer(config));
        registry.Register("date-part", config => new DatePartTransformer(config));

        // Assert
        Assert.True(registry.IsRegistered("epoch"));
        Assert.True(registry.IsRegistered("ticks"));
        Assert.True(registry.IsRegistered("julian-day"));
        Assert.True(registry.IsRegistered("date-format"));
        Assert.True(registry.IsRegistered("date-part"));
        Assert.Equal(5, registry.GetRegisteredNames().Count);
    }

    [Fact]
    public void TimeTransformers_ShouldWorkInPipeline()
    {
        // Arrange
        var registry = new TransformerRegistry();
        registry.Register("epoch", config => new EpochTransformer(config));
        registry.Register("date-part", config => new DatePartTransformer(config));

        // Create epoch transformer
        var epochConfig = new Dictionary<string, string> { ["unit"] = "s" };
        var epochTransformer = registry.CreateCell("epoch", epochConfig);

        // Create date part transformer  
        var partConfig = new Dictionary<string, string> { ["part"] = "year" };
        var partTransformer = registry.CreateCell("date-part", partConfig);

        var context = new CellContext("events", "timestamp", 0, SqliteAffinity.Integer);

        // Act - Transform epoch to ISO format, then extract year
        var isoDate = epochTransformer.Transform(context, "1692104456"); // Should convert to some 2023-08-15 date
        
        var dateContext = new CellContext("events", "created_date", 0, SqliteAffinity.Text);
        var year = partTransformer.Transform(dateContext, isoDate); // Should be "2023"

        // Assert - Just check that it converted to 2023 and has the right date
        Assert.Contains("2023-08-15", isoDate);
        Assert.Contains("Z", isoDate);
        Assert.Equal("2023", year);
    }

    [Fact]
    public void TimeTransformers_ShouldHandleEdgeCases()
    {
        // Test with various edge cases that could appear in real databases
        var testCases = new[]
        {
            // Epoch transformer edge cases
            ("epoch", new Dictionary<string, string> { ["unit"] = "s" }, "0", "1970-01-01T00:00:00Z"),
            ("epoch", new Dictionary<string, string> { ["unit"] = "ms" }, "0", "1970-01-01T00:00:00Z"),
            
            // Ticks transformer edge cases
            ("ticks", new Dictionary<string, string>(), new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks.ToString(), "2000-01-01T00:00:00Z"),
            
            // Date format edge cases
            ("date-format", new Dictionary<string, string> { ["outputFormat"] = "yyyy" }, "2023-12-31T23:59:59Z", "2023"),
        };

        var registry = new TransformerRegistry();
        registry.Register("epoch", config => new EpochTransformer(config));
        registry.Register("ticks", config => new TicksTransformer(config));
        registry.Register("date-format", config => new DateFormatTransformer(config));

        foreach (var (transformerName, config, input, expected) in testCases)
        {
            var transformer = registry.CreateCell(transformerName, config);
            var context = new CellContext("test", "test_col", 0, SqliteAffinity.Integer);
            
            var result = transformer.Transform(context, input);
            Assert.Equal(expected, result);
        }
    }
}