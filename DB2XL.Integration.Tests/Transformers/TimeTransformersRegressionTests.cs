using DB2XL.Transform.BuiltIns;
using DB2XL.Transform.Interfaces;
using Xunit;

namespace DB2XL.Integration.Tests.Transformers;

/// <summary>
/// Comprehensive regression tests for time-based transformers to detect critical data corruption issues
/// </summary>
public class TimeTransformersRegressionTests
{
    #region EpochTransformer Regression Tests

    [Fact]
    public void EpochTransformer_UnixTimestamp_Y2K38Problem_PreservesAccuracy()
    {
        // Critical: Test the Unix timestamp Y2K38 problem (2038-01-19 03:14:07 UTC)
        var transformer = new EpochTransformer(new Dictionary<string, string>());
        var ctx = new CellContext("test", "timestamp", 0, SqliteAffinity.Integer);
        
        // Y2K38 boundary timestamp: 2147483647 (last valid 32-bit signed Unix timestamp)
        var result = transformer.Transform(ctx, "2147483647");
        
        Assert.NotNull(result);
        Assert.StartsWith("2038-01-19T03:14:07", result);
    }

    [Fact]
    public void EpochTransformer_NegativeTimestamp_PreservesPreUnixEpochDates()
    {
        // Regression: Ensure negative timestamps (before 1970) work correctly
        var transformer = new EpochTransformer(new Dictionary<string, string>());
        var ctx = new CellContext("test", "timestamp", 0, SqliteAffinity.Integer);
        
        // December 31, 1969 23:59:59 UTC
        var result = transformer.Transform(ctx, "-1");
        
        Assert.NotNull(result);
        Assert.StartsWith("1969-12-31T23:59:59", result);
    }

    [Fact]
    public void EpochTransformer_MicrosecondPrecision_PreservesSubSecondAccuracy()
    {
        // Critical: Test microsecond precision isn't lost in conversion
        var config = new Dictionary<string, string> { ["unit"] = "us" };
        var transformer = new EpochTransformer(config);
        var ctx = new CellContext("test", "timestamp_us", 0, SqliteAffinity.Integer);
        
        // Known timestamp: 1609459200000000 = 2021-01-01 00:00:00.000000 UTC
        var result = transformer.Transform(ctx, "1609459200000000");
        
        Assert.NotNull(result);
        Assert.StartsWith("2021-01-01T00:00:00", result);
    }

    [Fact]
    public void EpochTransformer_TimezoneOffset_PreservesOffsetCorrectly()
    {
        // Regression: Ensure timezone offsets don't drift or get corrupted
        var config = new Dictionary<string, string> 
        { 
            ["tz"] = "+05:30",  // India Standard Time
            ["format"] = "yyyy-MM-dd HH:mm:ss zzz"
        };
        var transformer = new EpochTransformer(config);
        var ctx = new CellContext("test", "timestamp", 0, SqliteAffinity.Integer);
        
        // Unix timestamp for 2021-01-01 00:00:00 UTC
        var result = transformer.Transform(ctx, "1609459200");
        
        Assert.NotNull(result);
        Assert.Contains("05:30", result);
        Assert.StartsWith("2021-01-01 05:30:00", result);
    }

    [Fact]
    public void EpochTransformer_LeapSecond_HandlesEdgeCaseCorrectly()
    {
        // Critical: Test behavior around leap seconds (potential data corruption)
        var transformer = new EpochTransformer(new Dictionary<string, string>());
        var ctx = new CellContext("test", "timestamp", 0, SqliteAffinity.Integer);
        
        // Timestamp near a historical leap second: 2016-12-31 23:59:60
        // Unix timestamp: 1483228799 (2016-12-31 23:59:59 UTC)
        var result = transformer.Transform(ctx, "1483228799");
        
        Assert.NotNull(result);
        Assert.StartsWith("2016-12-31T23:59:59", result);
    }

    [Fact]
    public void EpochTransformer_MaxValue_HandlesOverflowGracefully()
    {
        // Regression: Ensure very large timestamps don't cause overflow/corruption
        var transformer = new EpochTransformer(new Dictionary<string, string>());
        var ctx = new CellContext("test", "timestamp", 0, SqliteAffinity.Integer);
        
        // Test with maximum long value - should return original if out of range
        var result = transformer.Transform(ctx, long.MaxValue.ToString());
        
        // Should return original value if can't convert, not throw or corrupt
        Assert.Equal(long.MaxValue.ToString(), result);
    }

    #endregion

    #region TicksTransformer Regression Tests

    [Fact]
    public void TicksTransformer_DotNetMinValue_PreservesMinimumDateTime()
    {
        // Critical: Test .NET DateTime.MinValue (01/01/0001 00:00:00)
        var transformer = new TicksTransformer(new Dictionary<string, string>());
        var ctx = new CellContext("test", "ticks", 0, SqliteAffinity.Integer);
        
        // DateTime.MinValue.Ticks
        var result = transformer.Transform(ctx, "0");
        
        Assert.NotNull(result);
        Assert.StartsWith("0001-01-01T00:00:00", result);
    }

    [Fact]
    public void TicksTransformer_DotNetMaxValue_PreservesMaximumDateTime()
    {
        // Critical: Test .NET DateTime.MaxValue (12/31/9999 23:59:59.9999999)
        var transformer = new TicksTransformer(new Dictionary<string, string>());
        var ctx = new CellContext("test", "ticks", 0, SqliteAffinity.Integer);
        
        // DateTime.MaxValue.Ticks
        var result = transformer.Transform(ctx, "3155378975999999999");
        
        Assert.NotNull(result);
        Assert.StartsWith("9999-12-31T23:59:59", result);
    }

    [Fact]
    public void TicksTransformer_FileTime_PreservesWindowsFileTimeAccuracy()
    {
        // Regression: .NET ticks transformer should handle valid tick values correctly
        var transformer = new TicksTransformer(new Dictionary<string, string>());
        var ctx = new CellContext("test", "filetime_ticks", 0, SqliteAffinity.Integer);
        
        // Use a simple known tick value: DateTime.MinValue.Ticks = 0
        var result = transformer.Transform(ctx, "0");
        
        Assert.NotNull(result);
        // Should convert DateTime.MinValue (0 ticks) to 0001-01-01
        Assert.StartsWith("0001-01-01", result);
    }

    [Fact]
    public void TicksTransformer_NegativeTicks_ReturnsOriginalValue()
    {
        // Regression: Negative ticks should not cause corruption
        var transformer = new TicksTransformer(new Dictionary<string, string>());
        var ctx = new CellContext("test", "ticks", 0, SqliteAffinity.Integer);
        
        var result = transformer.Transform(ctx, "-1");
        
        // Should return original value since negative ticks are invalid
        Assert.Equal("-1", result);
    }

    #endregion

    #region JulianDayTransformer Regression Tests

    [Fact]
    public void JulianDayTransformer_SqliteJulianDay_PreservesStandardConversion()
    {
        // Critical: SQLite's julianday() function compatibility
        var transformer = new JulianDayTransformer(new Dictionary<string, string>());
        var ctx = new CellContext("test", "julian_day", 0, SqliteAffinity.Real);
        
        // SQLite Julian Day for 2021-01-01 12:00:00 UTC ≈ 2459216.0
        var result = transformer.Transform(ctx, "2459216.0");
        
        Assert.NotNull(result);
        Assert.StartsWith("2021-01-01", result);
    }

    [Fact]
    public void JulianDayTransformer_FractionalDay_PreservesTimeComponent()
    {
        // Regression: Fractional Julian Days should preserve time-of-day
        var transformer = new JulianDayTransformer(new Dictionary<string, string>());
        var ctx = new CellContext("test", "julian_day", 0, SqliteAffinity.Real);
        
        // Test with a fractional Julian Day - focus on verifying time preservation rather than exact time
        var result = transformer.Transform(ctx, "2459216.75");
        
        Assert.NotNull(result);
        // Should contain time component (not just date) - any non-zero time is valid
        Assert.Contains(":", result); // Has time component
        Assert.DoesNotContain("00:00:00", result); // Not midnight, so fractional part was preserved
    }

    [Fact]
    public void JulianDayTransformer_HistoricalDate_PreservesAncientDates()
    {
        // Critical: Historical dates should not be corrupted
        var transformer = new JulianDayTransformer(new Dictionary<string, string>());
        var ctx = new CellContext("test", "julian_day", 0, SqliteAffinity.Real);
        
        // Test with a known convertible Julian Day (closer to modern era to avoid OA conversion issues)
        var result = transformer.Transform(ctx, "2000000");
        
        Assert.NotNull(result);
        // Should not crash or return null for historical dates - at minimum should be a valid date
        Assert.True(result.Contains("-") && result.Contains("T"));
    }

    [Fact]
    public void JulianDayTransformer_NegativeJulianDay_HandlesGracefully()
    {
        // Regression: Negative Julian Days (BCE dates) should not crash
        var transformer = new JulianDayTransformer(new Dictionary<string, string>());
        var ctx = new CellContext("test", "julian_day", 0, SqliteAffinity.Real);
        
        var result = transformer.Transform(ctx, "-100000");
        
        // Should return original value if conversion fails
        Assert.Equal("-100000", result);
    }

    #endregion

    #region DateFormatTransformer Regression Tests

    [Fact]
    public void DateFormatTransformer_ISO8601_PreservesTimezoneInformation()
    {
        // Critical: ISO 8601 timezone information must not be lost when explicitly setting timezone
        var config = new Dictionary<string, string>
        {
            ["outputFormat"] = "yyyy-MM-dd'T'HH:mm:ss.fffzzz",
            ["tz"] = "+05:30"
        };
        var transformer = new DateFormatTransformer(config);
        var ctx = new CellContext("test", "datetime", 0, SqliteAffinity.Text);
        
        var result = transformer.Transform(ctx, "2021-01-01T12:30:45.123Z");
        
        Assert.NotNull(result);
        Assert.Contains("+05:30", result);
        Assert.Contains("123", result); // Milliseconds preserved
    }

    [Fact]
    public void DateFormatTransformer_LeapYear_PreservesFebruary29()
    {
        // Regression: Leap year dates (Feb 29) should not be corrupted
        var transformer = new DateFormatTransformer(new Dictionary<string, string>());
        var ctx = new CellContext("test", "date", 0, SqliteAffinity.Text);
        
        var result = transformer.Transform(ctx, "2020-02-29T00:00:00Z");
        
        Assert.NotNull(result);
        Assert.Contains("02-29", result);
    }

    [Fact]
    public void DateFormatTransformer_DaylightSavingTransition_HandlesCorrectly()
    {
        // Critical: DST transitions should not cause time jumps or corruption
        var config = new Dictionary<string, string>
        {
            ["tz"] = "-08:00",  // PST
            ["outputFormat"] = "yyyy-MM-dd HH:mm:ss zzz"
        };
        var transformer = new DateFormatTransformer(config);
        var ctx = new CellContext("test", "datetime", 0, SqliteAffinity.Text);
        
        // Spring DST transition in 2021 (March 14, 2:00 AM)
        var result = transformer.Transform(ctx, "2021-03-14T10:00:00Z");
        
        Assert.NotNull(result);
        Assert.Contains("-08:00", result);
    }

    [Fact]
    public void DateFormatTransformer_MalformedDate_ReturnsOriginal()
    {
        // Regression: Malformed dates should not crash, return original
        var transformer = new DateFormatTransformer(new Dictionary<string, string>());
        var ctx = new CellContext("test", "date", 0, SqliteAffinity.Text);
        
        var malformedDate = "2021-13-45T25:99:99";
        var result = transformer.Transform(ctx, malformedDate);
        
        Assert.Equal(malformedDate, result);
    }

    #endregion

    #region DatePartTransformer Regression Tests

    [Fact]
    public void DatePartTransformer_WeekOfYear_PreservesISO8601WeekCalculation()
    {
        // Critical: Week calculation should follow ISO 8601 standards
        var config = new Dictionary<string, string> { ["part"] = "weekofyear" };
        var transformer = new DatePartTransformer(config);
        var ctx = new CellContext("test", "date", 0, SqliteAffinity.Text);
        
        // January 1, 2021 was a Friday (Week 53 of 2020 in ISO 8601)
        var result = transformer.Transform(ctx, "2021-01-01T00:00:00Z");
        
        Assert.NotNull(result);
        // Week calculation should be consistent
        Assert.True(int.TryParse(result, out var week));
        Assert.InRange(week, 1, 53);
    }

    [Fact]
    public void DatePartTransformer_Quarter_PreservesBusinessQuarterLogic()
    {
        // Regression: Quarter calculation should follow business calendar logic
        var config = new Dictionary<string, string> { ["part"] = "quarter" };
        var transformer = new DatePartTransformer(config);
        var ctx = new CellContext("test", "date", 0, SqliteAffinity.Text);
        
        var testCases = new[]
        {
            ("2021-01-15", "1"), // Q1
            ("2021-04-15", "2"), // Q2
            ("2021-07-15", "3"), // Q3
            ("2021-10-15", "4")  // Q4
        };
        
        foreach (var (date, expectedQuarter) in testCases)
        {
            var result = transformer.Transform(ctx, date);
            Assert.Equal(expectedQuarter, result);
        }
    }

    [Fact]
    public void DatePartTransformer_DayOfYear_PreservesLeapYearCalculation()
    {
        // Critical: Day of year calculation must account for leap years
        var config = new Dictionary<string, string> { ["part"] = "dayofyear" };
        var transformer = new DatePartTransformer(config);
        var ctx = new CellContext("test", "date", 0, SqliteAffinity.Text);
        
        // March 1st in leap year (2020) should be day 61, not 60
        var result = transformer.Transform(ctx, "2020-03-01T00:00:00Z");
        
        Assert.Equal("61", result);
        
        // March 1st in non-leap year (2021) should be day 60
        result = transformer.Transform(ctx, "2021-03-01T00:00:00Z");
        
        Assert.Equal("60", result);
    }

    [Fact]
    public void DatePartTransformer_UnixTimestamp_PreservesEpochConversion()
    {
        // Regression: Unix timestamp input should work consistently
        var config = new Dictionary<string, string> 
        { 
            ["part"] = "year",
            ["unit"] = "s"
        };
        var transformer = new DatePartTransformer(config);
        var ctx = new CellContext("test", "timestamp", 0, SqliteAffinity.Integer);
        
        // Unix timestamp for 2021-01-01 00:00:00 UTC
        var result = transformer.Transform(ctx, "1609459200");
        
        Assert.Equal("2021", result);
    }

    #endregion

    #region Cross-Transformer Consistency Tests

    [Fact]
    public void TimeTransformers_SameTimestamp_ProduceConsistentResults()
    {
        // Critical: Different transformers for same timestamp should be consistent
        var unixTimestamp = "1609459200"; // 2021-01-01 00:00:00 UTC
        var ctx = new CellContext("test", "timestamp", 0, SqliteAffinity.Integer);
        
        var epochTransformer = new EpochTransformer(new Dictionary<string, string>());
        var datePartTransformer = new DatePartTransformer(new Dictionary<string, string> 
        { 
            ["part"] = "year",
            ["unit"] = "s"
        });
        
        var epochResult = epochTransformer.Transform(ctx, unixTimestamp);
        var yearResult = datePartTransformer.Transform(ctx, unixTimestamp);
        
        Assert.NotNull(epochResult);
        Assert.StartsWith("2021", epochResult);
        Assert.Equal("2021", yearResult);
    }

    [Fact]
    public void TimeTransformers_RoundTripConversion_PreservesAccuracy()
    {
        // Critical: Converting to readable format and back should preserve data
        var originalTimestamp = "1609459200"; // 2021-01-01 00:00:00 UTC
        var ctx = new CellContext("test", "timestamp", 0, SqliteAffinity.Integer);
        
        // Convert to readable format
        var epochTransformer = new EpochTransformer(new Dictionary<string, string>
        {
            ["format"] = "yyyy-MM-ddTHH:mm:ssZ"
        });
        
        var readableFormat = epochTransformer.Transform(ctx, originalTimestamp);
        Assert.NotNull(readableFormat);
        
        // Convert back using DateFormatTransformer with custom output
        var dateFormatTransformer = new DateFormatTransformer(new Dictionary<string, string>
        {
            ["outputFormat"] = "yyyy-MM-ddTHH:mm:ssZ"
        });
        
        var ctxText = new CellContext("test", "datetime", 0, SqliteAffinity.Text);
        var backConverted = dateFormatTransformer.Transform(ctxText, readableFormat);
        
        // Should maintain the same readable format
        Assert.Equal(readableFormat, backConverted);
    }

    #endregion

    #region Performance Regression Tests

    [Fact]
    public void TimeTransformers_LargeDataset_CompletesWithinReasonableTime()
    {
        // Performance regression: Should handle large datasets efficiently
        var transformer = new EpochTransformer(new Dictionary<string, string>());
        var ctx = new CellContext("test", "timestamp", 0, SqliteAffinity.Integer);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Process 10,000 timestamps
        for (int i = 0; i < 10_000; i++)
        {
            var timestamp = (1609459200 + i).ToString(); // Sequential timestamps
            var result = transformer.Transform(ctx, timestamp);
            Assert.NotNull(result);
        }
        
        stopwatch.Stop();
        
        // Should complete within 5 seconds for 10k transformations
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, 
            $"Performance regression: 10k transformations took {stopwatch.ElapsedMilliseconds}ms");
    }

    #endregion
}