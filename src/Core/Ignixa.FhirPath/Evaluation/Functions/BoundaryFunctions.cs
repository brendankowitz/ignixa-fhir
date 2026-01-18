/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath boundary function implementations.
 * Implements lowBoundary() and highBoundary() for dates, times, and numeric values
 * per FHIRPath specification.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Attributes;
using Ignixa.FhirPath.Expressions;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Boundary function implementations for FhirPath expressions.
/// Supports lowBoundary(precision) and highBoundary(precision) for partial dates/times and numeric values.
/// </summary>
internal static class BoundaryFunctions
{
    private const int DefaultPrecision = -1;  // -1 means derive from input value

    /// <summary>
    /// lowBoundary(precision) - Returns the lower boundary of a value at the specified precision.
    /// For decimals: Returns value - 0.5 * 10^(-precision), truncated to precision decimal places.
    /// For dates/times: Returns the start of the period at the given precision with UTC+14:00 offset.
    /// </summary>
    [FhirPathFunction("lowBoundary",
        SupportedContexts = "any-any",
        ReturnType = "any",
        MinArguments = 0,
        MaxArguments = 1,
        TakesExpressionArguments = true,
        Category = "Boundary",
        Description = "Returns the lower boundary of a value at the specified precision")]
    public static IEnumerable<IElement> LowBoundary(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        int precision = DefaultPrecision;
        
        if (arguments.Count > 0)
        {
            var precisionResults = evaluateExpression(focus, arguments[0], context).ToList();
            if (precisionResults.Count == 1 && precisionResults[0].Value is int p)
            {
                precision = p;
            }
            else if (precisionResults.Count == 1 && precisionResults[0].Value != null)
            {
#pragma warning disable CS8602 // Dereference of a possibly null reference - we checked Value is not null above
                var valueStr = precisionResults[0].Value.ToString();
#pragma warning restore CS8602
                if (valueStr != null && int.TryParse(valueStr, out var parsedPrecision))
                {
                    precision = parsedPrecision;
                }
            }
        }

        // Validate explicit precision if provided
        if (precision != DefaultPrecision && (precision < 0 || precision > 31))
        {
            // Invalid precision: return empty
            yield break;
        }

        foreach (var element in focus)
        {
            if (element.Value == null)
            {
                continue;
            }

            var result = CalculateLowBoundary(element, precision);
            if (result != null)
            {
                yield return result;
            }
        }
    }

    /// <summary>
    /// highBoundary(precision) - Returns the upper boundary of a value at the specified precision.
    /// For decimals: Returns value + 0.5 * 10^(-precision), truncated to precision decimal places.
    /// For dates/times: Returns the end of the period at the given precision with UTC-12:00 offset.
    /// </summary>
    [FhirPathFunction("highBoundary",
        SupportedContexts = "any-any",
        ReturnType = "any",
        MinArguments = 0,
        MaxArguments = 1,
        TakesExpressionArguments = true,
        Category = "Boundary",
        Description = "Returns the upper boundary of a value at the specified precision")]
    public static IEnumerable<IElement> HighBoundary(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        int precision = DefaultPrecision;
        
        if (arguments.Count > 0)
        {
            var precisionResults = evaluateExpression(focus, arguments[0], context).ToList();
            if (precisionResults.Count == 1 && precisionResults[0].Value is int p)
            {
                precision = p;
            }
            else if (precisionResults.Count == 1 && precisionResults[0].Value != null)
            {
#pragma warning disable CS8602 // Dereference of a possibly null reference - we checked Value is not null above
                var valueStr = precisionResults[0].Value.ToString();
#pragma warning restore CS8602
                if (valueStr != null && int.TryParse(valueStr, out var parsedPrecision))
                {
                    precision = parsedPrecision;
                }
            }
        }

        // Validate explicit precision if provided
        if (precision != DefaultPrecision && (precision < 0 || precision > 31))
        {
            // Invalid precision: return empty
            yield break;
        }

        foreach (var element in focus)
        {
            if (element.Value == null)
            {
                continue;
            }

            var result = CalculateHighBoundary(element, precision);
            if (result != null)
            {
                yield return result;
            }
        }
    }

    #region Helper Methods

    private static IElement? CalculateLowBoundary(IElement element, int precision)
    {
        // Strip @ prefix from FHIR date/time string values
        var cleanValue = element.Value is string s && s.StartsWith('@')
            ? s.Substring(1)
            : element.Value;

        // Handle Quantity type (has both value and unit)
        if (element.InstanceType == "Quantity" && element.Value is decimal quantityValue)
        {
            var effectivePrecision = precision == DefaultPrecision ? GetDecimalPrecision(quantityValue) : precision;
            var boundaryValue = CalculateNumericLowBoundary(quantityValue, effectivePrecision);
            // For Quantity, we need to preserve the unit - this would typically be done by the type system
            return FunctionHelpers.CreateDecimal(boundaryValue);
        }

        return cleanValue switch
        {
            // Numeric types - derive precision from value if not explicitly provided
            decimal d => FunctionHelpers.CreateDecimal(CalculateNumericLowBoundary(d, precision == DefaultPrecision ? GetDecimalPrecision(d) : precision)),
            double d => FunctionHelpers.CreateDecimal(CalculateNumericLowBoundary((decimal)d, precision == DefaultPrecision ? GetDecimalPrecision((decimal)d) : precision)),
            int i => FunctionHelpers.CreateDecimal(CalculateNumericLowBoundary((decimal)i, precision == DefaultPrecision ? 0 : precision)),
            long l => FunctionHelpers.CreateDecimal(CalculateNumericLowBoundary((decimal)l, precision == DefaultPrecision ? 0 : precision)),

            // DateTime strings (with @ prefix handled above)
            string str when IsDateTimeString(str) => CalculateDateTimeLowBoundary(str, precision, element.InstanceType),

            // Time strings
            string str when IsTimeString(str) => CalculateTimeLowBoundary(str, precision),

            _ => null
        };
    }

    private static IElement? CalculateHighBoundary(IElement element, int precision)
    {
        // Strip @ prefix from FHIR date/time string values
        var cleanValue = element.Value is string s && s.StartsWith('@')
            ? s.Substring(1)
            : element.Value;

        // Handle Quantity type (has both value and unit)
        if (element.InstanceType == "Quantity" && element.Value is decimal quantityValue)
        {
            var effectivePrecision = precision == DefaultPrecision ? GetDecimalPrecision(quantityValue) : precision;
            var boundaryValue = CalculateNumericHighBoundary(quantityValue, effectivePrecision);
            return FunctionHelpers.CreateDecimal(boundaryValue);
        }

        return cleanValue switch
        {
            // Numeric types - derive precision from value if not explicitly provided
            decimal d => FunctionHelpers.CreateDecimal(CalculateNumericHighBoundary(d, precision == DefaultPrecision ? GetDecimalPrecision(d) : precision)),
            double d => FunctionHelpers.CreateDecimal(CalculateNumericHighBoundary((decimal)d, precision == DefaultPrecision ? GetDecimalPrecision((decimal)d) : precision)),
            int i => FunctionHelpers.CreateDecimal(CalculateNumericHighBoundary((decimal)i, precision == DefaultPrecision ? 0 : precision)),
            long l => FunctionHelpers.CreateDecimal(CalculateNumericHighBoundary((decimal)l, precision == DefaultPrecision ? 0 : precision)),

            // DateTime strings (with @ prefix handled above)
            string str when IsDateTimeString(str) => CalculateDateTimeHighBoundary(str, precision, element.InstanceType),

            // Time strings
            string str when IsTimeString(str) => CalculateTimeHighBoundary(str, precision),

            _ => null
        };
    }

    /// <summary>
    /// Gets the implied precision of a decimal value based on its internal scale.
    /// For example, 1.0 has precision 1, 1.00 has precision 2, 1 has precision 0.
    /// Uses the internal scale of the decimal which is preserved when parsing from strings.
    /// </summary>
    private static int GetDecimalPrecision(decimal value)
    {
        // Extract the scale from the decimal's internal representation
        // The scale is stored in bits 16-23 of the fourth 32-bit element
        var bits = decimal.GetBits(value);
        return (bits[3] >> 16) & 0xFF;
    }

    private static decimal CalculateNumericLowBoundary(decimal value, int precision)
    {
        // lowBoundary subtracts 0.5 * 10^(-precision) and truncates to precision decimal places
        // Special case: precision 0 means round down to integer
        if (precision == 0)
        {
            return Math.Floor(value);
        }

        var adjustment = 0.5m * (decimal)Math.Pow(10, -precision);
        var result = value - adjustment;
        
        // Truncate to precision decimal places
        var multiplier = (decimal)Math.Pow(10, precision);
        return Math.Truncate(result * multiplier) / multiplier;
    }

    private static decimal CalculateNumericHighBoundary(decimal value, int precision)
    {
        // highBoundary adds 0.5 * 10^(-precision) and truncates to precision decimal places
        // Special case: precision 0 means round up to integer
        if (precision == 0)
        {
            return Math.Ceiling(value);
        }

        var adjustment = 0.5m * (decimal)Math.Pow(10, -precision);
        var result = value + adjustment;
        
        // Truncate to precision decimal places  
        var multiplier = (decimal)Math.Pow(10, precision);
        return Math.Truncate(result * multiplier) / multiplier;
    }

    private static IElement? CalculateDateTimeLowBoundary(string dateTimeStr, int precision, string instanceType)
    {
        // Parse the date/time string to determine its components
        var parsed = ParseDateTimeString(dateTimeStr);
        if (parsed == null) return null;

        string result;
        // For dateTime type, always return full datetime with timezone
        // For date type, return date only
        if (instanceType == "date")
        {
            // Date precision - return date only, derive precision from input
            result = FormatDateLowBoundary(parsed.Value, dateTimeStr);
        }
        else
        {
            // DateTime precision - return datetime with timezone
            result = FormatDateTimeLowBoundary(parsed.Value, precision, dateTimeStr);
        }

        return FunctionHelpers.CreateString(result);
    }

    private static IElement? CalculateDateTimeHighBoundary(string dateTimeStr, int precision, string instanceType)
    {
        var parsed = ParseDateTimeString(dateTimeStr);
        if (parsed == null) return null;

        string result;
        // For dateTime type, always return full datetime with timezone
        // For date type, return date only
        if (instanceType == "date")
        {
            // Date precision - return date only, derive precision from input
            result = FormatDateHighBoundary(parsed.Value, dateTimeStr);
        }
        else
        {
            // DateTime precision - return datetime with timezone
            result = FormatDateTimeHighBoundary(parsed.Value, precision, dateTimeStr);
        }

        return FunctionHelpers.CreateString(result);
    }

    private static IElement? CalculateTimeLowBoundary(string timeStr, int precision)
    {
        // Parse time and expand to start of period at given precision
        var parsed = ParseTimeString(timeStr);
        if (parsed == null) return null;

        var result = FormatTimeLowBoundary(parsed.Value, precision);
        return FunctionHelpers.CreateString(result);
    }

    private static IElement? CalculateTimeHighBoundary(string timeStr, int precision)
    {
        var parsed = ParseTimeString(timeStr);
        if (parsed == null) return null;

        var result = FormatTimeHighBoundary(parsed.Value, precision);
        return FunctionHelpers.CreateString(result);
    }

    private static (int year, int month, int day, int hour, int minute, int second, int millisecond, string? timezone)? ParseDateTimeString(string dateTimeStr)
    {
        // Parse FHIR date/dateTime format: YYYY[-MM[-DD[Thh[:mm[:ss[.fff]]]][+/-hh:mm]]]]
        var match = Regex.Match(dateTimeStr, @"^(\d{4})(?:-(\d{2}))?(?:-(\d{2}))?(?:[T ](\d{2})(?::(\d{2}))?(?::(\d{2}))?(?:\.(\d+))?)?([+-]\d{2}:\d{2}|Z)?$");
        
        if (!match.Success) return null;

        int year = int.Parse(match.Groups[1].Value);
        int month = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1;
        int day = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 1;
        int hour = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0;
        int minute = match.Groups[5].Success ? int.Parse(match.Groups[5].Value) : 0;
        int second = match.Groups[6].Success ? int.Parse(match.Groups[6].Value) : 0;
        int millisecond = 0;
        
        if (match.Groups[7].Success)
        {
            var msStr = match.Groups[7].Value.PadRight(3, '0').Substring(0, 3);
            millisecond = int.Parse(msStr);
        }

        string? timezone = match.Groups[8].Success ? match.Groups[8].Value : null;

        return (year, month, day, hour, minute, second, millisecond, timezone);
    }

    private static (int hour, int minute, int second, int millisecond)? ParseTimeString(string timeStr)
    {
        // Parse FHIR time format: hh[:mm[:ss[.fff]]]
        // Remove @T prefix if present
        timeStr = timeStr.TrimStart('@').TrimStart('T');

        var match = Regex.Match(timeStr, @"^(\d{2})(?::(\d{2}))?(?::(\d{2}))?(?:\.(\d+))?$");
        
        if (!match.Success) return null;

        int hour = int.Parse(match.Groups[1].Value);
        int minute = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        int second = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        int millisecond = 0;
        
        if (match.Groups[4].Success)
        {
            var msStr = match.Groups[4].Value.PadRight(3, '0').Substring(0, 3);
            millisecond = int.Parse(msStr);
        }

        return (hour, minute, second, millisecond);
    }

    private static string FormatDateLowBoundary((int year, int month, int day, int hour, int minute, int second, int millisecond, string? timezone) parsed, string original)
    {
        // Determine the granularity of the input date from the original string
        // YYYY -> start of year: YYYY-01-01
        // YYYY-MM -> start of month: YYYY-MM-01
        // YYYY-MM-DD -> same date (no expansion needed): YYYY-MM-DD
        
        var components = original.Split('-');
        if (components.Length == 1)
        {
            // Year only: YYYY -> first day of year
            return $"{parsed.year:D4}-01-01";
        }
        else if (components.Length == 2)
        {
            // Year-month: YYYY-MM -> first day of month
            return $"{parsed.year:D4}-{parsed.month:D2}-01";
        }
        else
        {
            // Full date: YYYY-MM-DD -> return as-is
            return $"{parsed.year:D4}-{parsed.month:D2}-{parsed.day:D2}";
        }
    }

    private static string FormatDateHighBoundary((int year, int month, int day, int hour, int minute, int second, int millisecond, string? timezone) parsed, string original)
    {
        // Determine the granularity of the input date from the original string
        // YYYY -> end of year: YYYY-12-31
        // YYYY-MM -> end of month: YYYY-MM-{lastDay}
        // YYYY-MM-DD -> same date (no expansion needed): YYYY-MM-DD
        
        var components = original.Split('-');
        if (components.Length == 1)
        {
            // Year only: YYYY -> last day of year
            return $"{parsed.year:D4}-12-31";
        }
        else if (components.Length == 2)
        {
            // Year-month: YYYY-MM -> last day of month
            var daysInMonth = DateTime.DaysInMonth(parsed.year, parsed.month);
            return $"{parsed.year:D4}-{parsed.month:D2}-{daysInMonth:D2}";
        }
        else
        {
            // Full date: YYYY-MM-DD -> return as-is
            return $"{parsed.year:D4}-{parsed.month:D2}-{parsed.day:D2}";
        }
    }

    private static string FormatDateTimeLowBoundary((int year, int month, int day, int hour, int minute, int second, int millisecond, string? timezone) parsed, int precision, string original)
    {
        // precision 17 = millisecond level
        // For low boundary, use UTC+14:00 timezone (easternmost timezone)
        var existingTimezone = parsed.timezone;
        
        if (existingTimezone != null)
        {
            // Preserve existing timezone
            return $"{parsed.year:D4}-{parsed.month:D2}-{parsed.day:D2}T{parsed.hour:D2}:{parsed.minute:D2}:{parsed.second:D2}.{parsed.millisecond:D3}{existingTimezone}";
        }
        else
        {
            // Add UTC+14:00 for low boundary
            return $"{parsed.year:D4}-{parsed.month:D2}-{parsed.day:D2}T{parsed.hour:D2}:{parsed.minute:D2}:{parsed.second:D2}.{parsed.millisecond:D3}+14:00";
        }
    }

    private static string FormatDateTimeHighBoundary((int year, int month, int day, int hour, int minute, int second, int millisecond, string? timezone) parsed, int precision, string original)
    {
        // precision 17 = millisecond level
        // For high boundary, use UTC-12:00 timezone (westernmost timezone) and end of period
        var existingTimezone = parsed.timezone;
        
        if (existingTimezone != null)
        {
            // Preserve existing timezone, but set to end of minute
            return $"{parsed.year:D4}-{parsed.month:D2}-{parsed.day:D2}T{parsed.hour:D2}:{parsed.minute:D2}:59.999{existingTimezone}";
        }
        else
        {
            // Add UTC-12:00 for high boundary and set to end of day (23:59:59.999)
            return $"{parsed.year:D4}-{parsed.month:D2}-{parsed.day:D2}T23:59:59.999-12:00";
        }
    }

    private static string FormatTimeLowBoundary((int hour, int minute, int second, int millisecond) parsed, int precision)
    {
        // precision 9 = millisecond level - return without the @T prefix for raw time value
        return $"{parsed.hour:D2}:{parsed.minute:D2}:{parsed.second:D2}.{parsed.millisecond:D3}";
    }

    private static string FormatTimeHighBoundary((int hour, int minute, int second, int millisecond) parsed, int precision)
    {
        // precision 9 = millisecond level - set to end of minute
        return $"{parsed.hour:D2}:{parsed.minute:D2}:59.999";
    }

    private static bool IsDateTimeString(string value)
    {
        // Check if looks like a date or datetime (YYYY or YYYY-MM or YYYY-MM-DD[T...])
        return Regex.IsMatch(value, @"^\d{4}(-\d{2})?(-\d{2})?([T ]\d{2})?");
    }

    private static bool IsTimeString(string value)
    {
        // Check if looks like a time (hh:mm or @Thh:mm)
        value = value.TrimStart('@').TrimStart('T');
        return Regex.IsMatch(value, @"^\d{2}(:\d{2})?");
    }

    #endregion
}
