/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath boundary function implementations (Phase 23, Week 4).
 * Implements lowBoundary() and highBoundary() for dates, times, and numeric values
 * per SQL on FHIR v2 specification.
 */

using Ignixa.Abstractions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Boundary function implementations for FhirPath expressions.
/// Supports lowBoundary() and highBoundary() for partial dates/times and numeric values.
/// </summary>
internal static class BoundaryFunctions
{
    /// <summary>
    /// lowBoundary() - Calculates the low boundary of a value.
    /// For decimals: multiplies by 0.95 (5% lower)
    /// For dates/times: returns the start of the period with UTC+14:00 offset
    /// </summary>
    public static IEnumerable<ITypedElement> LowBoundary(IEnumerable<ITypedElement> focus)
    {
        foreach (var element in focus)
        {
            if (element.Value == null)
            {
                // Null values return no result (empty collection)
                continue;
            }

            var result = element.Value switch
            {
                // Decimal boundary: 5% lower
                decimal d => FunctionHelpers.CreateDecimal(d * 0.95m),
                double d => FunctionHelpers.CreateDecimal((decimal)d * 0.95m),
                int i => FunctionHelpers.CreateDecimal(i * 0.95m),
                long l => FunctionHelpers.CreateDecimal(l * 0.95m),

                // Date/DateTime boundary: start of period with UTC+14:00 offset
                DateTime dt => FunctionHelpers.CreateString(GetDateTimeLowBoundary(dt)),
                DateTimeOffset dto => FunctionHelpers.CreateString(GetDateTimeOffsetLowBoundary(dto)),

                // String dateTime (when element type is dateTime)
                string s when IsDateLike(s) && string.Equals(element.InstanceType, "dateTime", StringComparison.OrdinalIgnoreCase) => FunctionHelpers.CreateString(GetStringDateTimeLowBoundary(s)),

                // String dates (partial dates, when element type is date)
                string s when IsDateLike(s) => FunctionHelpers.CreateString(GetStringDateLowBoundary(s)),

                // String times (partial times)
                string s when IsTimeLike(s) => FunctionHelpers.CreateString(GetStringTimeLowBoundary(s)),

                // Unsupported type: return no result
                _ => null
            };

            if (result != null)
            {
                yield return result;
            }
        }
    }

    /// <summary>
    /// highBoundary() - Calculates the high boundary of a value.
    /// For decimals: multiplies by 1.05 (5% higher)
    /// For dates/times: returns the end of the period with UTC-12:00 offset
    /// </summary>
    public static IEnumerable<ITypedElement> HighBoundary(IEnumerable<ITypedElement> focus)
    {
        foreach (var element in focus)
        {
            if (element.Value == null)
            {
                // Null values return no result (empty collection)
                continue;
            }

            var result = element.Value switch
            {
                // Decimal boundary: 5% higher
                decimal d => FunctionHelpers.CreateDecimal(d * 1.05m),
                double d => FunctionHelpers.CreateDecimal((decimal)d * 1.05m),
                int i => FunctionHelpers.CreateDecimal(i * 1.05m),
                long l => FunctionHelpers.CreateDecimal(l * 1.05m),

                // Date/DateTime boundary: end of period with UTC-12:00 offset
                DateTime dt => FunctionHelpers.CreateString(GetDateTimeHighBoundary(dt)),
                DateTimeOffset dto => FunctionHelpers.CreateString(GetDateTimeOffsetHighBoundary(dto)),

                // String dateTime (when element type is dateTime)
                string s when IsDateLike(s) && string.Equals(element.InstanceType, "dateTime", StringComparison.OrdinalIgnoreCase) => FunctionHelpers.CreateString(GetStringDateTimeHighBoundary(s)),

                // String dates (partial dates, when element type is date)
                string s when IsDateLike(s) => FunctionHelpers.CreateString(GetStringDateHighBoundary(s)),

                // String times (partial times)
                string s when IsTimeLike(s) => FunctionHelpers.CreateString(GetStringTimeHighBoundary(s)),

                // Unsupported type: return no result
                _ => null
            };

            if (result != null)
            {
                yield return result;
            }
        }
    }

    #region DateTime Boundary Helpers

    private static string GetDateTimeLowBoundary(DateTime dt)
    {
        // For a partial date like "1970-06", expand to start with UTC+14:00 timezone
        // If the date is incomplete (day/time missing), use the first possible value
        // Example: "1970-06" becomes "1970-06-01T00:00:00.000+14:00"
        if (dt.Kind == DateTimeKind.Unspecified)
        {
            // Assume it's already the start of the period, just add UTC+14:00 offset
            return dt.ToString("yyyy-MM-ddTHH:mm:ss.fff+14:00");
        }

        // For fully specified DateTime, add UTC+14:00 offset
        return dt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff+14:00");
    }

    private static string GetDateTimeHighBoundary(DateTime dt)
    {
        // For a partial date like "1970-06", expand to end with UTC-12:00 timezone
        // If the date is incomplete (day/time missing), use the last possible value
        // Example: "1970-06" becomes "1970-06-30T23:59:59.999-12:00"
        if (dt.Kind == DateTimeKind.Unspecified)
        {
            // For unspecified datetime, shift to end of period
            var endDate = new DateTime(dt.Year, dt.Month, DateTime.DaysInMonth(dt.Year, dt.Month), 23, 59, 59, 999);
            return endDate.ToString("yyyy-MM-ddTHH:mm:ss.fff-12:00");
        }

        // For fully specified DateTime, convert to end of period and add UTC-12:00 offset
        var utcDt = dt.ToUniversalTime();
        var endUtc = new DateTime(utcDt.Year, utcDt.Month, DateTime.DaysInMonth(utcDt.Year, utcDt.Month), 23, 59, 59, 999, DateTimeKind.Utc);
        return endUtc.ToString("yyyy-MM-ddTHH:mm:ss.fff-12:00");
    }

    private static string GetDateTimeOffsetLowBoundary(DateTimeOffset dto)
    {
        // Similar to DateTime low boundary, but accounts for the offset
        return dto.DateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff+14:00");
    }

    private static string GetDateTimeOffsetHighBoundary(DateTimeOffset dto)
    {
        // Similar to DateTime high boundary, but accounts for the offset
        var endDate = new DateTime(dto.Year, dto.Month, DateTime.DaysInMonth(dto.Year, dto.Month), 23, 59, 59, 999, DateTimeKind.Unspecified);
        return endDate.ToString("yyyy-MM-ddTHH:mm:ss.fff-12:00");
    }

    #endregion

    #region String Date Boundary Helpers

    private static string GetStringDateLowBoundary(string dateString)
    {
        // Parse partial date string and expand to the start of the period
        // Returns just a date (no time component) per SQL on FHIR v2 spec
        // Examples:
        // "1970" -> "1970-01-01"
        // "1970-06" -> "1970-06-01"
        // "1970-06-15" -> "1970-06-15" (already complete)
        var parts = dateString.Split('-');

        if (parts.Length < 1)
        {
            return dateString;
        }

        int year = int.Parse(parts[0]);
        int month = parts.Length > 1 ? int.Parse(parts[1]) : 1;
        int day = parts.Length > 2 ? int.Parse(parts[2]) : 1;

        // Return date only (yyyy-MM-dd format)
        return $"{year:D4}-{month:D2}-{day:D2}";
    }

    private static string GetStringDateHighBoundary(string dateString)
    {
        // Parse partial date string and expand to the end of the period
        // Returns just a date (no time component) per SQL on FHIR v2 spec
        // Examples:
        // "1970" -> "1970-12-31"
        // "1970-06" -> "1970-06-30"
        // "1970-06-15" -> "1970-06-15" (already complete)
        var parts = dateString.Split('-');

        if (parts.Length < 1)
        {
            return dateString;
        }

        int year = int.Parse(parts[0]);
        int month = parts.Length > 1 ? int.Parse(parts[1]) : 12;
        int day = parts.Length > 2 ? int.Parse(parts[2]) : DateTime.DaysInMonth(year, month);

        // Return date only (yyyy-MM-dd format)
        return $"{year:D4}-{month:D2}-{day:D2}";
    }

    private static string GetStringDateTimeLowBoundary(string dateString)
    {
        // Parse partial date string and expand to the start of the period with UTC+14:00 timezone
        // Returns dateTime format (with time and timezone) per SQL on FHIR v2 spec
        // Examples:
        // "2010-10-10" -> "2010-10-10T00:00:00.000+14:00"
        // "2010-10" -> "2010-10-01T00:00:00.000+14:00"
        // "2010" -> "2010-01-01T00:00:00.000+14:00"
        var parts = dateString.Split('-');

        if (parts.Length < 1)
        {
            return dateString;
        }

        int year = int.Parse(parts[0]);
        int month = parts.Length > 1 ? int.Parse(parts[1]) : 1;
        int day = parts.Length > 2 ? int.Parse(parts[2]) : 1;

        // Return dateTime with UTC+14:00 timezone (yyyy-MM-ddTHH:mm:ss.fff+14:00 format)
        return $"{year:D4}-{month:D2}-{day:D2}T00:00:00.000+14:00";
    }

    private static string GetStringDateTimeHighBoundary(string dateString)
    {
        // Parse partial date string and expand to the end of the period with UTC-12:00 timezone
        // Returns dateTime format (with time and timezone) per SQL on FHIR v2 spec
        // Examples:
        // "2010-10-10" -> "2010-10-10T23:59:59.999-12:00"
        // "2010-10" -> "2010-10-31T23:59:59.999-12:00"
        // "2010" -> "2010-12-31T23:59:59.999-12:00"
        var parts = dateString.Split('-');

        if (parts.Length < 1)
        {
            return dateString;
        }

        int year = int.Parse(parts[0]);
        int month = parts.Length > 1 ? int.Parse(parts[1]) : 12;
        int day = parts.Length > 2 ? int.Parse(parts[2]) : DateTime.DaysInMonth(year, month);

        // Return dateTime with UTC-12:00 timezone (yyyy-MM-ddTHH:mm:ss.fff-12:00 format)
        return $"{year:D4}-{month:D2}-{day:D2}T23:59:59.999-12:00";
    }

    #endregion

    #region String Time Boundary Helpers

    private static string GetStringTimeLowBoundary(string timeString)
    {
        // Parse partial time string and expand to the start of the period
        // Examples:
        // "12" -> "12:00:00.000"
        // "12:34" -> "12:34:00.000"
        // "12:34:56" -> "12:34:56.000"
        // "12:34:56.789" -> "12:34:56.789" (already complete)
        var parts = timeString.Split(':', '.');

        if (parts.Length < 1)
        {
            return timeString;
        }

        int hour = int.Parse(parts[0]);
        int minute = parts.Length > 1 ? int.Parse(parts[1]) : 0;
        int second = parts.Length > 2 ? int.Parse(parts[2]) : 0;
        int millisecond = parts.Length > 3 ? int.Parse(parts[3].PadRight(3, '0').Substring(0, 3)) : 0;

        // Return time with milliseconds (HH:mm:ss.fff format)
        return $"{hour:D2}:{minute:D2}:{second:D2}.{millisecond:D3}";
    }

    private static string GetStringTimeHighBoundary(string timeString)
    {
        // Parse partial time string and expand to the end of the period
        // Examples:
        // "12" -> "12:59:59.999"
        // "12:34" -> "12:34:59.999"
        // "12:34:56" -> "12:34:56.999"
        // "12:34:56.789" -> "12:34:56.789" (already complete)
        var parts = timeString.Split(':', '.');

        if (parts.Length < 1)
        {
            return timeString;
        }

        int hour = int.Parse(parts[0]);
        int minute = parts.Length > 1 ? int.Parse(parts[1]) : 59;
        int second = parts.Length > 2 ? int.Parse(parts[2]) : 59;
        int millisecond = parts.Length > 3 ? int.Parse(parts[3].PadRight(3, '0').Substring(0, 3)) : 999;

        // Return time with milliseconds (HH:mm:ss.fff format)
        return $"{hour:D2}:{minute:D2}:{second:D2}.{millisecond:D3}";
    }

    #endregion

    #region Type Detection Helpers

    private static bool IsTimeLike(string value)
    {
        // Check if string looks like a time (HH or HH:mm or HH:mm:ss or HH:mm:ss.fff)
        // Time format uses colons and optional dot for milliseconds
        if (value.Contains('-', StringComparison.Ordinal))
        {
            return false; // Has dashes, likely a date
        }

        var parts = value.Split(':', '.');
        if (parts.Length < 1 || parts.Length > 4)
        {
            return false;
        }

        // Check if all parts are numeric
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDateLike(string value)
    {
        // Check if string looks like a date (YYYY or YYYY-MM or YYYY-MM-DD)
        var parts = value.Split('-');
        if (parts.Length < 1 || parts.Length > 3)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out _))
            {
                return false;
            }
        }

        return true;
    }

    #endregion
}
