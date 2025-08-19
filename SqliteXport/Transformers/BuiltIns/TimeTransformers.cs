using System.Globalization;

namespace DB2XL.Transformers.BuiltIns;

/// <summary>
/// Transforms Unix epoch timestamps to human-readable date/time strings
/// </summary>
public class EpochTransformer : CellTransformerBase
{
    public EpochTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        // Apply to INTEGER fields that might contain timestamps
        return ctx.Affinity == SqliteAffinity.Integer && 
               (ctx.Column.ToLowerInvariant().Contains("time") || 
                ctx.Column.ToLowerInvariant().Contains("date") ||
                ctx.Column.ToLowerInvariant().Contains("epoch") ||
                GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw) || !long.TryParse(raw, out var timestamp))
            return raw;

        var unit = GetConfig("unit", "s").ToLowerInvariant();
        var format = GetConfig("format", "yyyy-MM-ddTHH:mm:ssZ");
        var timezone = GetConfig("tz", "UTC");

        try
        {
            DateTimeOffset dateTime;
            
            // Convert based on unit
            switch (unit)
            {
                case "s" or "sec" or "seconds":
                    dateTime = DateTimeOffset.FromUnixTimeSeconds(timestamp);
                    break;
                case "ms" or "milliseconds":
                    dateTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
                    break;
                case "us" or "microseconds":
                    // SQLite microseconds since Unix epoch
                    dateTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp / 1000);
                    break;
                case "ns" or "nanoseconds":
                    // SQLite nanoseconds since Unix epoch
                    dateTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp / 1_000_000);
                    break;
                default:
                    return raw; // Unknown unit, return original
            }

            // Handle timezone conversion
            if (timezone.Equals("UTC", StringComparison.OrdinalIgnoreCase))
            {
                return dateTime.ToUniversalTime().ToString(format, CultureInfo.InvariantCulture);
            }
            else if (timezone.Equals("Local", StringComparison.OrdinalIgnoreCase))
            {
                return dateTime.ToLocalTime().ToString(format, CultureInfo.InvariantCulture);
            }
            else
            {
                // Try to parse as timezone offset (e.g., "+05:00", "-08:00")
                // Remove leading + for TimeSpan parsing
                var timezoneForParsing = timezone.StartsWith("+") ? timezone.Substring(1) : timezone;
                if (TimeSpan.TryParse(timezoneForParsing, out var offset))
                {
                    // If original timezone started with +, keep positive offset
                    // If started with -, TimeSpan.TryParse handles it correctly
                    var offsetDateTime = dateTime.ToOffset(offset);
                    // Use a format that shows the actual timezone offset instead of Z
                    var offsetFormat = format.Replace("Z", "zzz");
                    return offsetDateTime.ToString(offsetFormat, CultureInfo.InvariantCulture);
                }
                // Fall back to UTC if timezone parsing fails
                return dateTime.ToUniversalTime().ToString(format, CultureInfo.InvariantCulture);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Timestamp out of valid range, return original
            return raw;
        }
        catch (Exception ex)
        {
            throw new TransformerException("epoch", ctx, $"Failed to transform timestamp '{raw}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Transforms .NET ticks to human-readable date/time strings
/// </summary>
public class TicksTransformer : CellTransformerBase
{
    public TicksTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Affinity == SqliteAffinity.Integer && 
               (ctx.Column.ToLowerInvariant().Contains("tick") || 
                GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw) || !long.TryParse(raw, out var ticks))
            return raw;

        var format = GetConfig("format", "yyyy-MM-ddTHH:mm:ssZ");
        var timezone = GetConfig("tz", "UTC");

        try
        {
            var dateTime = new DateTime(ticks, DateTimeKind.Utc);
            var offset = new DateTimeOffset(dateTime);

            if (timezone.Equals("Local", StringComparison.OrdinalIgnoreCase))
            {
                return offset.ToLocalTime().ToString(format, CultureInfo.InvariantCulture);
            }
            else 
            {
                // Try to parse as timezone offset (e.g., "+05:00", "-08:00")
                var timezoneForParsing = timezone.StartsWith("+") ? timezone.Substring(1) : timezone;
                if (TimeSpan.TryParse(timezoneForParsing, out var timezoneOffset))
                {
                    return offset.ToOffset(timezoneOffset).ToString(format, CultureInfo.InvariantCulture);
                }
                else
                {
                    return offset.ToUniversalTime().ToString(format, CultureInfo.InvariantCulture);
                }
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            return raw; // Invalid ticks value
        }
        catch (Exception ex)
        {
            throw new TransformerException("ticks", ctx, $"Failed to transform ticks '{raw}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Transforms SQLite Julian Day numbers to human-readable date/time strings
/// </summary>
public class JulianDayTransformer : CellTransformerBase
{
    public JulianDayTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return (ctx.Affinity == SqliteAffinity.Real || ctx.Affinity == SqliteAffinity.Integer) && 
               (ctx.Column.ToLowerInvariant().Contains("julian") || 
                GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw) || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var julianDay))
            return raw;

        var format = GetConfig("format", "yyyy-MM-ddTHH:mm:ssZ");
        var timezone = GetConfig("tz", "UTC");

        try
        {
            // Convert Julian Day to DateTime
            // Julian Day 0 corresponds to January 1, 4713 BCE in the proleptic Julian calendar
            // For modern dates, we use the simplified conversion
            var dateTime = DateTime.FromOADate(julianDay - 2415018.5); // OLE Automation Date conversion
            var offset = new DateTimeOffset(dateTime, TimeSpan.Zero);

            if (timezone.Equals("Local", StringComparison.OrdinalIgnoreCase))
            {
                return offset.ToLocalTime().ToString(format, CultureInfo.InvariantCulture);
            }
            else 
            {
                // Try to parse as timezone offset (e.g., "+05:00", "-08:00")
                var timezoneForParsing = timezone.StartsWith("+") ? timezone.Substring(1) : timezone;
                if (TimeSpan.TryParse(timezoneForParsing, out var timezoneOffset))
                {
                    return offset.ToOffset(timezoneOffset).ToString(format, CultureInfo.InvariantCulture);
                }
                else
                {
                    return offset.ToString(format, CultureInfo.InvariantCulture);
                }
            }
        }
        catch (ArgumentException)
        {
            return raw; // Invalid Julian Day value
        }
        catch (Exception ex)
        {
            throw new TransformerException("julian-day", ctx, $"Failed to transform Julian Day '{raw}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Transforms ISO 8601 date strings to different formats or timezones
/// </summary>
public class DateFormatTransformer : CellTransformerBase
{
    public DateFormatTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return ctx.Affinity == SqliteAffinity.Text && 
               (ctx.Column.ToLowerInvariant().Contains("date") || 
                ctx.Column.ToLowerInvariant().Contains("time") ||
                GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var inputFormat = GetConfig("inputFormat", ""); // Auto-detect if empty
        var outputFormat = GetConfig("outputFormat", "yyyy-MM-dd HH:mm:ss");
        var timezone = GetConfig("tz", "UTC");

        try
        {
            DateTimeOffset dateTime;

            // Try to parse the input
            if (string.IsNullOrEmpty(inputFormat))
            {
                // Auto-detect common formats
                if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
                {
                    return raw; // Can't parse, return original
                }
            }
            else
            {
                // Use specific format with UTC assumption for dates without timezone info
                if (!DateTimeOffset.TryParseExact(raw, inputFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dateTime))
                {
                    return raw; // Can't parse, return original
                }
            }

            // Apply timezone conversion
            if (timezone.Equals("Local", StringComparison.OrdinalIgnoreCase))
            {
                return dateTime.ToLocalTime().ToString(outputFormat, CultureInfo.InvariantCulture);
            }
            else 
            {
                // Try to parse as timezone offset (e.g., "+05:00", "-08:00")
                var timezoneForParsing = timezone.StartsWith("+") ? timezone.Substring(1) : timezone;
                if (TimeSpan.TryParse(timezoneForParsing, out var timezoneOffset))
                {
                    return dateTime.ToOffset(timezoneOffset).ToString(outputFormat, CultureInfo.InvariantCulture);
                }
                else
                {
                    return dateTime.ToUniversalTime().ToString(outputFormat, CultureInfo.InvariantCulture);
                }
            }
        }
        catch (Exception ex)
        {
            throw new TransformerException("date-format", ctx, $"Failed to transform date '{raw}': {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Extracts specific date/time components (year, month, day, etc.)
/// </summary>
public class DatePartTransformer : CellTransformerBase
{
    public DatePartTransformer(IDictionary<string, string> configuration) : base(configuration) { }

    public override bool CanApply(CellContext ctx)
    {
        return (ctx.Affinity == SqliteAffinity.Text || ctx.Affinity == SqliteAffinity.Integer) && 
               (ctx.Column.ToLowerInvariant().Contains("date") || 
                ctx.Column.ToLowerInvariant().Contains("time") ||
                GetConfigBool("forceApply", false));
    }

    public override string? Transform(CellContext ctx, string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var part = GetConfig("part", "date").ToLowerInvariant();
        var inputFormat = GetConfig("inputFormat", "");

        try
        {
            DateTimeOffset dateTime;

            // Handle Unix timestamps
            if (ctx.Affinity == SqliteAffinity.Integer && long.TryParse(raw, out var timestamp))
            {
                var unit = GetConfig("unit", "s");
                dateTime = unit.ToLowerInvariant() switch
                {
                    "s" or "seconds" => DateTimeOffset.FromUnixTimeSeconds(timestamp),
                    "ms" or "milliseconds" => DateTimeOffset.FromUnixTimeMilliseconds(timestamp),
                    _ => DateTimeOffset.FromUnixTimeSeconds(timestamp)
                };
            }
            else
            {
                // Parse date string
                if (string.IsNullOrEmpty(inputFormat))
                {
                    if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
                        return raw;
                }
                else
                {
                    if (!DateTimeOffset.TryParseExact(raw, inputFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out dateTime))
                        return raw;
                }
            }

            // Extract the requested part
            return part switch
            {
                "year" => dateTime.Year.ToString(),
                "month" => dateTime.Month.ToString(),
                "day" => dateTime.Day.ToString(),
                "hour" => dateTime.Hour.ToString(),
                "minute" => dateTime.Minute.ToString(),
                "second" => dateTime.Second.ToString(),
                "dayofweek" => dateTime.DayOfWeek.ToString(),
                "dayofyear" => dateTime.DayOfYear.ToString(),
                "weekofyear" => CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(dateTime.DateTime, CalendarWeekRule.FirstDay, DayOfWeek.Monday).ToString(),
                "quarter" => ((dateTime.Month - 1) / 3 + 1).ToString(),
                "date" => dateTime.ToString("yyyy-MM-dd"),
                "time" => dateTime.ToString("HH:mm:ss"),
                "iso" => dateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                _ => dateTime.ToString("yyyy-MM-dd")
            };
        }
        catch (Exception ex)
        {
            throw new TransformerException("date-part", ctx, $"Failed to extract date part '{part}' from '{raw}': {ex.Message}", ex);
        }
    }
}