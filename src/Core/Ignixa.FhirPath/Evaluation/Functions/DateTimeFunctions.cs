/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath date/time component extraction functions (Phase 23).
 * Implements year(), month(), day(), hour(), minute(), second(), millisecond(), timezone().
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Attributes;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Date/Time component extraction functions for FhirPath expressions.
/// Handles partial precision (year-only, year-month, etc.) and timezone extraction.
/// </summary>
public static class DateTimeFunctions
{
    /// <summary>
    /// Extracts the year component from a date/datetime/time value.
    /// Returns empty if the value doesn't have year precision.
    /// </summary>
    /// <example>
    /// @2024-11-18.year() = 2024
    /// @2024-11-18T14:30:45Z.year() = 2024
    /// @2024.year() = 2024
    /// @T14:30:45.year() = empty (time has no year)
    /// </example>
    [FhirPathFunction("year",
        SupportedContexts = "any-integer",
        ReturnType = "integer",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "DateTime",
        Description = "Extracts the year component from a date/datetime value")]
    public static IEnumerable<IElement> Year(IEnumerable<IElement> focus)
    {
        foreach (var element in focus)
        {
            var parsed = ParseDateTimeValue(element);
            if (parsed.HasValue && parsed.Value.Year.HasValue)
            {
                yield return CreateInteger(parsed.Value.Year.Value);
            }
        }
    }

    /// <summary>
    /// Extracts the month component (1-12) from a date/datetime value.
    /// Returns empty if the value doesn't have month precision.
    /// </summary>
    /// <example>
    /// @2024-11-18.month() = 11
    /// @2024-01-15T10:00:00Z.month() = 1
    /// @2024.month() = empty (year-only has no month)
    /// </example>
    [FhirPathFunction("month",
        SupportedContexts = "any-integer",
        ReturnType = "integer",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "DateTime",
        Description = "Extracts the month component from a date/datetime value")]
    public static IEnumerable<IElement> Month(IEnumerable<IElement> focus)
    {
        foreach (var element in focus)
        {
            var parsed = ParseDateTimeValue(element);
            if (parsed.HasValue && parsed.Value.Month.HasValue)
            {
                yield return CreateInteger(parsed.Value.Month.Value);
            }
        }
    }

    /// <summary>
    /// Extracts the day component (1-31) from a date/datetime value.
    /// Returns empty if the value doesn't have day precision.
    /// </summary>
    /// <example>
    /// @2024-11-18.day() = 18
    /// @2024-02-29.day() = 29 (leap year)
    /// @2024-11.day() = empty (year-month has no day)
    /// </example>
    [FhirPathFunction("day",
        SupportedContexts = "any-integer",
        ReturnType = "integer",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "DateTime",
        Description = "Extracts the day component from a date/datetime value")]
    public static IEnumerable<IElement> Day(IEnumerable<IElement> focus)
    {
        foreach (var element in focus)
        {
            var parsed = ParseDateTimeValue(element);
            if (parsed.HasValue && parsed.Value.Day.HasValue)
            {
                yield return CreateInteger(parsed.Value.Day.Value);
            }
        }
    }

    /// <summary>
    /// Extracts the hour component (0-23) from a datetime/time value.
    /// Returns empty if the value doesn't have time precision.
    /// </summary>
    /// <example>
    /// @2024-11-18T14:30:45Z.hour() = 14
    /// @T14:30:45.hour() = 14
    /// @2024-11-18.hour() = empty (date has no time)
    /// </example>
    [FhirPathFunction("hour",
        SupportedContexts = "any-integer",
        ReturnType = "integer",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "DateTime",
        Description = "Extracts the hour component from a datetime/time value")]
    public static IEnumerable<IElement> Hour(IEnumerable<IElement> focus)
    {
        foreach (var element in focus)
        {
            var parsed = ParseDateTimeValue(element);
            if (parsed.HasValue && parsed.Value.Hour.HasValue)
            {
                yield return CreateInteger(parsed.Value.Hour.Value);
            }
        }
    }

    /// <summary>
    /// Extracts the minute component (0-59) from a datetime/time value.
    /// Returns empty if the value doesn't have time precision.
    /// </summary>
    /// <example>
    /// @2024-11-18T14:30:45Z.minute() = 30
    /// @T14:30:45.minute() = 30
    /// @2024-11-18T14:00:00Z.minute() = 0
    /// </example>
    [FhirPathFunction("minute",
        SupportedContexts = "any-integer",
        ReturnType = "integer",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "DateTime",
        Description = "Extracts the minute component from a datetime/time value")]
    public static IEnumerable<IElement> Minute(IEnumerable<IElement> focus)
    {
        foreach (var element in focus)
        {
            var parsed = ParseDateTimeValue(element);
            if (parsed.HasValue && parsed.Value.Minute.HasValue)
            {
                yield return CreateInteger(parsed.Value.Minute.Value);
            }
        }
    }

    /// <summary>
    /// Extracts the second component (0-59) from a datetime/time value.
    /// Returns empty if the value doesn't have second precision.
    /// </summary>
    /// <example>
    /// @2024-11-18T14:30:45Z.second() = 45
    /// @T14:30:45.second() = 45
    /// @2024-11-18T14:30:00Z.second() = 0
    /// </example>
    [FhirPathFunction("second",
        SupportedContexts = "any-integer",
        ReturnType = "integer",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "DateTime",
        Description = "Extracts the second component from a datetime/time value")]
    public static IEnumerable<IElement> Second(IEnumerable<IElement> focus)
    {
        foreach (var element in focus)
        {
            var parsed = ParseDateTimeValue(element);
            if (parsed.HasValue && parsed.Value.Second.HasValue)
            {
                yield return CreateInteger(parsed.Value.Second.Value);
            }
        }
    }

    /// <summary>
    /// Extracts the millisecond component (0-999) from a datetime/time value.
    /// Returns 0 if the value has time precision but no millisecond component.
    /// Returns empty if the value doesn't have time precision.
    /// </summary>
    /// <example>
    /// @2024-11-18T14:30:45.123Z.millisecond() = 123
    /// @T14:30:45.999.millisecond() = 999
    /// @2024-11-18T14:30:45Z.millisecond() = 0
    /// @2024-11-18.millisecond() = empty (date has no time)
    /// </example>
    [FhirPathFunction("millisecond",
        SupportedContexts = "any-integer",
        ReturnType = "integer",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "DateTime",
        Description = "Extracts the millisecond component from a datetime/time value")]
    public static IEnumerable<IElement> Millisecond(IEnumerable<IElement> focus)
    {
        foreach (var element in focus)
        {
            var parsed = ParseDateTimeValue(element);
            if (parsed.HasValue && parsed.Value.Hour.HasValue)
            {
                // If we have time precision, return millisecond or 0
                yield return CreateInteger(parsed.Value.Millisecond ?? 0);
            }
        }
    }

    /// <summary>
    /// Extracts the timezone offset from a datetime value.
    /// Returns "Z" for UTC, "+HH:MM" or "-HH:MM" for offsets, empty for local time.
    /// </summary>
    /// <example>
    /// @2024-11-18T14:30:45Z.timezone() = "Z"
    /// @2024-11-18T14:30:45+05:30.timezone() = "+05:30"
    /// @2024-11-18T14:30:45-08:00.timezone() = "-08:00"
    /// @2024-11-18T14:30:45.timezone() = empty (local time)
    /// @2024-11-18.timezone() = empty (date has no timezone)
    /// </example>
    [FhirPathFunction("timezone",
        SupportedContexts = "any-string",
        ReturnType = "string",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "DateTime",
        Description = "Extracts the timezone offset from a datetime value")]
    public static IEnumerable<IElement> Timezone(IEnumerable<IElement> focus)
    {
        foreach (var element in focus)
        {
            var parsed = ParseDateTimeValue(element);
            if (parsed.HasValue && !string.IsNullOrEmpty(parsed.Value.Timezone))
            {
                yield return CreateString(parsed.Value.Timezone);
            }
        }
    }

    #region Parsing Logic

    /// <summary>
    /// Represents a parsed date/time value with partial precision support.
    /// </summary>
    private struct ParsedDateTime
    {
        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? Day { get; set; }
        public int? Hour { get; set; }
        public int? Minute { get; set; }
        public int? Second { get; set; }
        public int? Millisecond { get; set; }
        public string? Timezone { get; set; }
    }

    /// <summary>
    /// Parses a date/time literal value from an IElement.
    /// Supports ISO 8601 formats with partial precision:
    /// - Date: @YYYY, @YYYY-MM, @YYYY-MM-DD
    /// - DateTime: @YYYY-MM-DDTHH:MM:SS.FFF(Z|±HH:MM)?
    /// - Time: @THH:MM:SS.FFF
    /// </summary>
    private static ParsedDateTime? ParseDateTimeValue(IElement element)
    {
        var value = element.Value?.ToString();
        if (string.IsNullOrEmpty(value))
            return null;

        // Remove leading @ if present (from literal syntax)
        if (value.StartsWith("@", StringComparison.Ordinal))
            value = value.Substring(1);

        var parsed = new ParsedDateTime();

        // Check for time-only literal (@T...)
        if (value.StartsWith("T", StringComparison.Ordinal))
        {
            return ParseTime(value.Substring(1));
        }

        // Parse date components (YYYY-MM-DD)
        var dateTimeParts = value.Split('T');
        var datePart = dateTimeParts[0];

        if (!ParseDate(datePart, ref parsed))
            return null;

        // Parse time components if present
        if (dateTimeParts.Length > 1)
        {
            var timePart = dateTimeParts[1];
            ParseTimeWithTimezone(timePart, ref parsed);
        }

        return parsed;
    }

    /// <summary>
    /// Parses date component (YYYY, YYYY-MM, or YYYY-MM-DD).
    /// </summary>
    private static bool ParseDate(string datePart, ref ParsedDateTime parsed)
    {
        var parts = datePart.Split('-');

        // Year (required)
        if (parts.Length >= 1 && int.TryParse(parts[0], out var year))
        {
            parsed.Year = year;
        }
        else
        {
            return false;
        }

        // Month (optional)
        if (parts.Length >= 2 && int.TryParse(parts[1], out var month))
        {
            parsed.Month = month;
        }

        // Day (optional)
        if (parts.Length >= 3 && int.TryParse(parts[2], out var day))
        {
            parsed.Day = day;
        }

        return true;
    }

    /// <summary>
    /// Parses time-only literal (THH:MM:SS.FFF).
    /// </summary>
    private static ParsedDateTime? ParseTime(string timePart)
    {
        var parsed = new ParsedDateTime();
        ParseTimeComponents(timePart, ref parsed);
        return parsed;
    }

    /// <summary>
    /// Parses time component with optional timezone (HH:MM:SS.FFF(Z|±HH:MM)?).
    /// </summary>
    private static void ParseTimeWithTimezone(string timePart, ref ParsedDateTime parsed)
    {
        // Extract timezone suffix (Z or ±HH:MM)
        string? timezone = null;
        var timeOnly = timePart;

        if (timePart.EndsWith("Z", StringComparison.Ordinal))
        {
            timezone = "Z";
            timeOnly = timePart.Substring(0, timePart.Length - 1);
        }
        else
        {
            // Check for ±HH:MM offset
            var plusIndex = timePart.LastIndexOf('+');
            var minusIndex = timePart.LastIndexOf('-');
            var offsetIndex = Math.Max(plusIndex, minusIndex);

            if (offsetIndex > 0)
            {
                timezone = timePart.Substring(offsetIndex);
                timeOnly = timePart.Substring(0, offsetIndex);
            }
        }

        ParseTimeComponents(timeOnly, ref parsed);
        parsed.Timezone = timezone;
    }

    /// <summary>
    /// Parses time components (HH:MM:SS.FFF).
    /// </summary>
    private static void ParseTimeComponents(string timePart, ref ParsedDateTime parsed)
    {
        // Split on colon and dot
        var parts = timePart.Split(':');

        // Hour (required for time)
        if (parts.Length >= 1 && int.TryParse(parts[0], out var hour))
        {
            parsed.Hour = hour;
        }

        // Minute (required for time)
        if (parts.Length >= 2 && int.TryParse(parts[1], out var minute))
        {
            parsed.Minute = minute;
        }

        // Second and millisecond (optional)
        if (parts.Length >= 3)
        {
            var secondPart = parts[2];
            var dotIndex = secondPart.IndexOf('.', StringComparison.Ordinal);

            if (dotIndex >= 0)
            {
                // Has milliseconds
                var secondStr = secondPart.Substring(0, dotIndex);
                var millisecondStr = secondPart.Substring(dotIndex + 1);

                if (int.TryParse(secondStr, out var second))
                {
                    parsed.Second = second;
                }

                // Parse milliseconds (may have variable precision)
                if (ParseMilliseconds(millisecondStr, out var millisecond))
                {
                    parsed.Millisecond = millisecond;
                }
            }
            else
            {
                // No milliseconds
                if (int.TryParse(secondPart, out var second))
                {
                    parsed.Second = second;
                }
            }
        }
    }

    /// <summary>
    /// Parses millisecond fraction (handles variable precision: .1, .12, .123, .1234, etc.).
    /// </summary>
    private static bool ParseMilliseconds(string fractionStr, out int millisecond)
    {
        millisecond = 0;

        if (string.IsNullOrEmpty(fractionStr))
            return false;

        // Pad or truncate to 3 digits
        if (fractionStr.Length < 3)
        {
            fractionStr = fractionStr.PadRight(3, '0');
        }
        else if (fractionStr.Length > 3)
        {
            fractionStr = fractionStr.Substring(0, 3);
        }

        return int.TryParse(fractionStr, out millisecond);
    }

    #endregion

    #region Helper Methods

    private static IElement CreateInteger(int value)
    {
        return new PrimitiveElement(value, "integer");
    }

    private static IElement CreateString(string value)
    {
        return new PrimitiveElement(value, "string");
    }

    /// <summary>
    /// Simple IElement implementation for primitive values.
    /// </summary>
    private class PrimitiveElement : IElement
    {
        public PrimitiveElement(object value, string instanceType)
        {
            Value = value;
            InstanceType = instanceType;
        }

        public string Name => string.Empty;
        public string InstanceType { get; }
        public object Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;

        public T? Meta<T>() where T : class => null;

        public IReadOnlyList<IElement> Children(string? name = null)
        {
            return [];
        }
    }

    /// <summary>
    /// Returns the precision (number of significant digits/characters) of a date/time or decimal value.
    /// For dates/times, counts all digits. For decimals, counts significant figures including trailing zeros.
    /// Returns empty for empty collections or non-date/decimal values.
    /// </summary>
    /// <example>
    /// 1.58700.precision() = 5
    /// @2014.precision() = 4
    /// @2014-01-05T10:30:00.000.precision() = 17
    /// @T10:30.precision() = 4
    /// {}.precision() = empty
    /// </example>
    [FhirPathFunction("precision",
        SupportedContexts = "any-integer",
        ReturnType = "integer",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "DateTime",
        Description = "Returns the precision (number of significant digits) of a date/time or decimal value")]
    public static IEnumerable<IElement> Precision(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count == 0)
            return [];

        if (list.Count != 1)
            return [];

        var element = list[0];
        var value = element.Value;

        // Handle date/time literals (stored as strings with @ prefix)
        if (value is string str && str.StartsWith("@", StringComparison.Ordinal))
        {
            // Remove @ prefix and count digits only
            var dateTimeStr = str.Substring(1);
            var digitCount = dateTimeStr.Count(c => char.IsDigit(c));
            return [CreateInteger(digitCount)];
        }

        // Handle decimal values - count significant figures
        if (value is decimal decValue)
        {
            // Convert to string to preserve trailing zeros
            var decStr = decValue.ToString(System.Globalization.CultureInfo.InvariantCulture);

            // Remove decimal point and count digits
            var digits = decStr.Replace(".", "", StringComparison.Ordinal)
                              .Replace("-", "", StringComparison.Ordinal);
            return [CreateInteger(digits.Length)];
        }

        // Handle integer values
        if (value is int intValue)
        {
            var intStr = intValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return [CreateInteger(intStr.Replace("-", "", StringComparison.Ordinal).Length)];
        }

        return [];
    }

    #endregion
}
