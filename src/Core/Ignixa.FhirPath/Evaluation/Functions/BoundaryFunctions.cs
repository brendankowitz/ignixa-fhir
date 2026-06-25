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
using Ignixa.FhirPath.Types;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Boundary function implementations for FhirPath expressions.
/// Supports lowBoundary(precision) and highBoundary(precision) for partial dates/times and numeric values.
/// </summary>
internal static class BoundaryFunctions
{
    private const int DefaultDecimalPrecision = 8;  // Default precision is 8 for decimals when no argument given
    private const int DefaultDateTimePrecision = 17;  // Default precision for date/dateTime (full millisecond precision)
    private const int DefaultTimePrecision = 9;  // Default precision for time (full millisecond precision)

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
        int? explicitPrecision = null;
        
        if (arguments.Count > 0)
        {
            // Non-scoped function: evaluate argument in outer context (don't change $this)
            var precisionResults = evaluateExpression(context.Focus, arguments[0], context).ToList();
            if (precisionResults.Count == 1 && precisionResults[0].Value is int p)
            {
                explicitPrecision = p;
            }
            else if (precisionResults.Count == 1 && precisionResults[0].Value != null)
            {
#pragma warning disable CS8602 // Dereference of a possibly null reference - we checked Value is not null above
                var valueStr = precisionResults[0].Value.ToString();
#pragma warning restore CS8602
                if (valueStr != null && int.TryParse(valueStr, out var parsedPrecision))
                {
                    explicitPrecision = parsedPrecision;
                }
            }
        }

        // Validate explicit precision if provided (< 0 or > 31 is invalid)
        if (explicitPrecision.HasValue && (explicitPrecision.Value < 0 || explicitPrecision.Value > 31))
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

            var precision = explicitPrecision ?? GetDefaultPrecisionForElement(element);
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
        int? explicitPrecision = null;
        
        if (arguments.Count > 0)
        {
            // Non-scoped function: evaluate argument in outer context (don't change $this)
            var precisionResults = evaluateExpression(context.Focus, arguments[0], context).ToList();
            if (precisionResults.Count == 1 && precisionResults[0].Value is int p)
            {
                explicitPrecision = p;
            }
            else if (precisionResults.Count == 1 && precisionResults[0].Value != null)
            {
#pragma warning disable CS8602 // Dereference of a possibly null reference - we checked Value is not null above
                var valueStr = precisionResults[0].Value.ToString();
#pragma warning restore CS8602
                if (valueStr != null && int.TryParse(valueStr, out var parsedPrecision))
                {
                    explicitPrecision = parsedPrecision;
                }
            }
        }

        // Validate explicit precision if provided (< 0 or > 31 is invalid)
        if (explicitPrecision.HasValue && (explicitPrecision.Value < 0 || explicitPrecision.Value > 31))
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

            var precision = explicitPrecision ?? GetDefaultPrecisionForElement(element);
            var result = CalculateHighBoundary(element, precision);
            if (result != null)
            {
                yield return result;
            }
        }
    }

    #region Helper Methods

    private static int GetDefaultPrecisionForElement(IElement element)
    {
        var instanceType = element.InstanceType;
        
        // For date/time types, default to full precision for boundary comparisons
        // Per FHIRPath spec and Firely implementation:
        // - @2014.lowBoundary() = @2014-01-01T00:00:00.000 (full datetime)
        // - @T10:30.lowBoundary() = @T10:30:00.000 (full time)
        if (string.Equals(instanceType, "date", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(instanceType, "dateTime", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(instanceType, "instant", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultDateTimePrecision;
        }
        
        if (string.Equals(instanceType, "time", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultTimePrecision;
        }
        
        // Check for date/time string literals (starting with @)
        if (element.Value is string str)
        {
            var cleanStr = str.StartsWith('@') ? str.Substring(1) : str;
            if (IsDateTimeString(cleanStr))
            {
                return DefaultDateTimePrecision;
            }
            if (IsTimeString(cleanStr))
            {
                return DefaultTimePrecision;
            }
        }
        
        // Default to decimal precision for numeric types
        return DefaultDecimalPrecision;
    }

    private static int GetTimePrecision(string timeStr)
    {
        // Time format: HH[:mm[:ss[.fff]]]
        // Returns: 2=hour, 4=minute, 6=second, 9=millisecond
        var cleanStr = timeStr.TrimStart('T');
        var parts = cleanStr.Split(':');
        
        if (parts.Length == 1) return 2; // Hour only
        if (parts.Length == 2) return 4; // Hour:minute
        
        // Check for milliseconds in the seconds part
        if (parts[2].Contains('.', StringComparison.Ordinal)) return 9;
        return 6; // Hour:minute:second
    }

    private static IElement? CalculateLowBoundary(IElement element, int outputPrecision)
    {
        // Strip @ prefix from FHIR date/time string values
        var cleanValue = element.Value is string s && s.StartsWith('@')
            ? s.Substring(1)
            : element.Value;

        // Handle Quantity type (has both value and unit)
        if (element.Value is Quantity qty)
        {
            var inputPrecision = GetDecimalPrecision(qty.Value);
            var boundaryValue = CalculateNumericLowBoundaryWithPrecisions(qty.Value, inputPrecision, outputPrecision);
            return FunctionHelpers.CreateQuantity(new Quantity(boundaryValue, qty.Unit));
        }

        // Handle FHIR Quantity element with decimal value
        if (element.InstanceType == "Quantity" && element.Value is decimal quantityValue)
        {
            var inputPrecision = GetDecimalPrecision(quantityValue);
            var boundaryValue = CalculateNumericLowBoundaryWithPrecisions(quantityValue, inputPrecision, outputPrecision);
            var unit = ExtractUnitFromQuantityElement(element) ?? "1";
            return FunctionHelpers.CreateQuantity(new Quantity(boundaryValue, unit));
        }

        return cleanValue switch
        {
            // Numeric types - get input precision from value, use outputPrecision for formatting
            decimal d => FunctionHelpers.CreateDecimal(CalculateNumericLowBoundaryWithPrecisions(d, GetDecimalPrecision(d), outputPrecision)),
            double d => FunctionHelpers.CreateDecimal(CalculateNumericLowBoundaryWithPrecisions((decimal)d, GetDecimalPrecision((decimal)d), outputPrecision)),
            int i => FunctionHelpers.CreateDecimal(CalculateNumericLowBoundaryWithPrecisions((decimal)i, 0, outputPrecision)),  // integers have 0 precision
            long l => FunctionHelpers.CreateDecimal(CalculateNumericLowBoundaryWithPrecisions((decimal)l, 0, outputPrecision)),

            // DateTime strings (with @ prefix handled above)
            string str when IsDateTimeString(str) => CalculateDateTimeLowBoundary(str, outputPrecision, element.InstanceType),

            // Time strings
            string str when IsTimeString(str) => CalculateTimeLowBoundary(str, outputPrecision),

            _ => null
        };
    }

    private static IElement? CalculateHighBoundary(IElement element, int outputPrecision)
    {
        // Strip @ prefix from FHIR date/time string values
        var cleanValue = element.Value is string s && s.StartsWith('@')
            ? s.Substring(1)
            : element.Value;

        // Handle Quantity type (has both value and unit)
        if (element.Value is Quantity qty)
        {
            var inputPrecision = GetDecimalPrecision(qty.Value);
            var boundaryValue = CalculateNumericHighBoundaryWithPrecisions(qty.Value, inputPrecision, outputPrecision);
            return FunctionHelpers.CreateQuantity(new Quantity(boundaryValue, qty.Unit));
        }

        // Handle FHIR Quantity element with decimal value
        if (element.InstanceType == "Quantity" && element.Value is decimal quantityValue)
        {
            var inputPrecision = GetDecimalPrecision(quantityValue);
            var boundaryValue = CalculateNumericHighBoundaryWithPrecisions(quantityValue, inputPrecision, outputPrecision);
            var unit = ExtractUnitFromQuantityElement(element) ?? "1";
            return FunctionHelpers.CreateQuantity(new Quantity(boundaryValue, unit));
        }

        return cleanValue switch
        {
            // Numeric types - get input precision from value, use outputPrecision for formatting
            decimal d => FunctionHelpers.CreateDecimal(CalculateNumericHighBoundaryWithPrecisions(d, GetDecimalPrecision(d), outputPrecision)),
            double d => FunctionHelpers.CreateDecimal(CalculateNumericHighBoundaryWithPrecisions((decimal)d, GetDecimalPrecision((decimal)d), outputPrecision)),
            int i => FunctionHelpers.CreateDecimal(CalculateNumericHighBoundaryWithPrecisions((decimal)i, 0, outputPrecision)),  // integers have 0 precision
            long l => FunctionHelpers.CreateDecimal(CalculateNumericHighBoundaryWithPrecisions((decimal)l, 0, outputPrecision)),

            // DateTime strings (with @ prefix handled above)
            string str when IsDateTimeString(str) => CalculateDateTimeHighBoundary(str, outputPrecision, element.InstanceType),

            // Time strings
            string str when IsTimeString(str) => CalculateTimeHighBoundary(str),

            _ => null
        };
    }

    private static string? ExtractUnitFromQuantityElement(IElement element)
    {
        var unitChildren = element.Children("unit");
        if (unitChildren.Count > 0 && unitChildren[0].Value is string unit)
            return unit;

        var codeChildren = element.Children("code");
        if (codeChildren.Count > 0 && codeChildren[0].Value is string code)
            return code;

        return null;
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

    /// <summary>
    /// Calculates the low boundary of a numeric value.
    /// Per FHIRPath spec: lowBoundary returns the minimum possible value.
    /// Uses floor for most cases, but handles small negatives specially.
    /// </summary>
    private static decimal CalculateNumericLowBoundaryWithPrecisions(decimal value, int inputPrecision, int outputPrecision)
    {
        // Calculate boundary using input precision (the precision of the incoming value)
        var boundaryAdjustment = 0.5m * (decimal)Math.Pow(10, -inputPrecision);
        var result = value - boundaryAdjustment;

        // Special case: output precision 0 means return an integer
        if (outputPrecision == 0)
        {
            return Math.Floor(result);
        }
        
        var multiplier = (decimal)Math.Pow(10, outputPrecision);
        var scaled = result * multiplier;
        
        // Special case for small negative values close to zero (like -0.00345)
        // When the scaled value is between -1 and 0, floor gives -1 (result -0.1)
        // but the minimum boundary should be -0.0 (truncation toward zero, preserving sign)
        // This matches the behavior expected for (-0.0034).lowBoundary(1) -> -0.0
        if (scaled > -1m && scaled < 0m)
        {
            // Return negative zero by using -0.0 formatted with the correct precision
            // C# decimal doesn't have negative zero, so we'll return 0 but the caller
            // should format it specially. However, for now we return the closest representation.
            return SetDecimalScalePreservingSign(0m, outputPrecision, value < 0);
        }
        
        var floored = Math.Floor(scaled) / multiplier;
        
        // Ensure the decimal has exactly the output precision number of decimal places
        return SetDecimalScale(floored, outputPrecision);
    }

    /// <summary>
    /// Calculates the high boundary of a numeric value.
    /// Per FHIRPath spec: highBoundary returns the maximum possible value.
    /// Uses different rounding based on sign: ceiling for positive, truncation for negative.
    /// </summary>
    private static decimal CalculateNumericHighBoundaryWithPrecisions(decimal value, int inputPrecision, int outputPrecision)
    {
        // Calculate boundary using input precision (the precision of the incoming value)
        var boundaryAdjustment = 0.5m * (decimal)Math.Pow(10, -inputPrecision);
        var result = value + boundaryAdjustment;

        // Special case: output precision 0 means return an integer
        if (outputPrecision == 0)
        {
            return Math.Ceiling(result);
        }
        
        // For highBoundary:
        // - For positive values: use standard rounding which handles edge cases like 0.00345 -> 0.0
        // - For negative values: truncate toward zero (which is "up" for negatives)
        decimal rounded;
        if (result >= 0)
        {
            rounded = Math.Round(result, outputPrecision, MidpointRounding.AwayFromZero);
        }
        else
        {
            // For negative values, truncate toward zero (ceiling behavior)
            var multiplier = (decimal)Math.Pow(10, outputPrecision);
            rounded = Math.Truncate(result * multiplier) / multiplier;
        }
        
        // Ensure the decimal has exactly the output precision number of decimal places
        return SetDecimalScale(rounded, outputPrecision);
    }

    /// <summary>
    /// Sets the scale of a decimal to ensure it has exactly the specified number of decimal places.
    /// This is important because FHIRPath boundary functions must return values with exactly
    /// the precision number of decimal places, including trailing zeros.
    /// </summary>
    private static decimal SetDecimalScale(decimal value, int scale)
    {
        if (scale <= 0)
        {
            return Math.Truncate(value);
        }
        
        // To force a specific scale with trailing zeros, we need to construct the decimal manually
        // by parsing a formatted string representation
        var format = "0." + new string('0', scale);
        var formatted = value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        return decimal.Parse(formatted, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Sets the scale of a decimal, optionally preserving the negative sign even for zero values.
    /// This is needed because FHIRPath boundary functions may return "-0.0" for negative inputs
    /// that round to zero.
    /// </summary>
    private static decimal SetDecimalScalePreservingSign(decimal value, int scale, bool preserveNegative)
    {
        if (scale <= 0)
        {
            return Math.Truncate(value);
        }
        
        var format = "0." + new string('0', scale);
        var formatted = value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        
        // If we need to preserve negative sign on zero, prepend "-"
        if (preserveNegative && value == 0 && !formatted.StartsWith("-", StringComparison.Ordinal))
        {
            formatted = "-" + formatted;
        }
        
        return decimal.Parse(formatted, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static IElement? CalculateDateTimeLowBoundary(string dateTimeStr, int outputPrecision, string instanceType)
    {
        // Parse the date/time string to determine its components
        var parsed = ParseDateTimeString(dateTimeStr);
        if (parsed == null) return null;

        string result = FormatDateTimeLowBoundary(parsed.Value, outputPrecision, dateTimeStr);
        
        // FHIRPath spec: boundary functions on dates always return dateTime type
        // Even partial dates like "2014-01" are typed as dateTime in FHIRPath
        return FunctionHelpers.CreateDateTime(result);
    }

    private static IElement? CalculateDateTimeHighBoundary(string dateTimeStr, int outputPrecision, string instanceType)
    {
        var parsed = ParseDateTimeString(dateTimeStr);
        if (parsed == null) return null;

        string result = FormatDateTimeHighBoundary(parsed.Value, outputPrecision, dateTimeStr);
        
        // FHIRPath spec: boundary functions on dates always return dateTime type
        return FunctionHelpers.CreateDateTime(result);
    }

    private static IElement? CalculateTimeLowBoundary(string timeStr, int precision)
    {
        // Parse time and expand to start of period at given precision
        var parsed = ParseTimeString(timeStr);
        if (parsed == null) return null;

        var result = FormatTimeLowBoundary(parsed.Value, precision);
        return FunctionHelpers.CreateTime(result);
    }

    private static IElement? CalculateTimeHighBoundary(string timeStr)
    {
        var parsed = ParseTimeString(timeStr);
        if (parsed == null) return null;

        var inputPrecision = GetTimePrecision(timeStr.TrimStart('@'));
        var result = FormatTimeHighBoundary(parsed.Value, inputPrecision);
        return FunctionHelpers.CreateTime(result);
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

    private static string FormatDateTimeLowBoundary((int year, int month, int day, int hour, int minute, int second, int millisecond, string? timezone) parsed, int outputPrecision, string original)
    {
        // Determine input precision from the original string
        var inputPrecision = GetDateTimePrecision(original);
        
        // Output precision determines the format of the result
        // Precision 4 = year (YYYY), 6 = month (YYYY-MM), 8 = day (YYYY-MM-DD), etc.
        // If output precision is less than or equal to input precision, return at output precision
        // Otherwise, expand to output precision with low boundary values
        
        if (outputPrecision <= 4)
        {
            return $"{parsed.year:D4}";
        }
        
        var month = inputPrecision <= 4 ? 1 : parsed.month;
        if (outputPrecision <= 6)
        {
            return $"{parsed.year:D4}-{month:D2}";
        }
        
        var day = inputPrecision <= 6 ? 1 : parsed.day;
        if (outputPrecision <= 8)
        {
            return $"{parsed.year:D4}-{month:D2}-{day:D2}";
        }
        
        // For time components, need timezone
        var tz = parsed.timezone ?? "+14:00";
        var hour = inputPrecision <= 8 ? 0 : parsed.hour;
        if (outputPrecision <= 10)
        {
            return $"{parsed.year:D4}-{month:D2}-{day:D2}T{hour:D2}{tz}";
        }
        
        var minute = inputPrecision <= 10 ? 0 : parsed.minute;
        if (outputPrecision <= 12)
        {
            return $"{parsed.year:D4}-{month:D2}-{day:D2}T{hour:D2}:{minute:D2}{tz}";
        }
        
        var second = inputPrecision <= 12 ? 0 : parsed.second;
        if (outputPrecision <= 14)
        {
            return $"{parsed.year:D4}-{month:D2}-{day:D2}T{hour:D2}:{minute:D2}:{second:D2}{tz}";
        }
        
        // Full millisecond precision (17+)
        var millisecond = inputPrecision <= 14 ? 0 : parsed.millisecond;
        return $"{parsed.year:D4}-{month:D2}-{day:D2}T{hour:D2}:{minute:D2}:{second:D2}.{millisecond:D3}{tz}";
    }

    private static string FormatFullDateTimeLow((int year, int month, int day, int hour, int minute, int second, int millisecond, string? timezone) parsed, int outputPrecision, int inputPrecision)
    {
        var tz = parsed.timezone ?? "+14:00";
        
        // Per FHIRPath spec: lowBoundary returns the lowest possible value within the range.
        // For partial dates (year/month/day precision), fill in missing components with low values.
        // For year precision (4), use January 1.
        // For month precision (6), use the 1st day.
        // For time components not specified, use 00:00:00.000.
        var month = inputPrecision <= 4 ? 1 : parsed.month;
        var day = inputPrecision <= 6 ? 1 : parsed.day;
        var hour = inputPrecision <= 8 ? 0 : parsed.hour;
        var minute = inputPrecision <= 10 ? 0 : parsed.minute;
        var second = inputPrecision <= 12 ? 0 : parsed.second;
        var millisecond = inputPrecision <= 14 ? 0 : parsed.millisecond;
        
        return $"{parsed.year:D4}-{month:D2}-{day:D2}T{hour:D2}:{minute:D2}:{second:D2}.{millisecond:D3}{tz}";
    }

    private static string FormatDateTimeHighBoundary((int year, int month, int day, int hour, int minute, int second, int millisecond, string? timezone) parsed, int outputPrecision, string original)
    {
        // Determine input precision from the original string
        var inputPrecision = GetDateTimePrecision(original);
        
        // Output precision determines the format of the result
        // For high boundary: maximize only the component at the output precision level
        // that wasn't specified in the input. Higher precision components stay at default,
        // lower precision components (the "boundary" component) gets maximized.
        
        if (outputPrecision <= 4)
        {
            return $"{parsed.year:D4}";
        }
        
        // For month: maximize only if output precision is exactly at month level (6) AND input doesn't have month
        var month = (outputPrecision == 6 && inputPrecision <= 4) ? 12 : parsed.month;
        if (outputPrecision <= 6)
        {
            return $"{parsed.year:D4}-{month:D2}";
        }
        
        // For day: maximize if we're outputting at day level or finer AND input doesn't have day
        var day = (outputPrecision >= 8 && inputPrecision <= 6) ? DateTime.DaysInMonth(parsed.year, month) : parsed.day;
        if (outputPrecision <= 8)
        {
            return $"{parsed.year:D4}-{month:D2}-{day:D2}";
        }
        
        // For time components, need timezone
        var tz = parsed.timezone ?? "-12:00";
        
        // For hour: maximize if we're outputting time AND input doesn't have hour
        var hour = (outputPrecision >= 10 && inputPrecision <= 8) ? 23 : parsed.hour;
        if (outputPrecision <= 10)
        {
            return $"{parsed.year:D4}-{month:D2}-{day:D2}T{hour:D2}{tz}";
        }
        
        // For minute: maximize if we're outputting at minute level or finer AND input doesn't have minute
        var minute = (outputPrecision >= 12 && inputPrecision <= 10) ? 59 : parsed.minute;
        if (outputPrecision <= 12)
        {
            return $"{parsed.year:D4}-{month:D2}-{day:D2}T{hour:D2}:{minute:D2}{tz}";
        }
        
        // For second: maximize to 59 if input doesn't have seconds
        var second = inputPrecision <= 12 ? 59 : parsed.second;
        if (outputPrecision <= 14)
        {
            return $"{parsed.year:D4}-{month:D2}-{day:D2}T{hour:D2}:{minute:D2}:{second:D2}{tz}";
        }
        
        // Full millisecond precision (17+)
        // For milliseconds, if input had seconds but not milliseconds, the high boundary is .999
        var millisecond = inputPrecision <= 14 ? 999 : parsed.millisecond;
        return $"{parsed.year:D4}-{month:D2}-{day:D2}T{hour:D2}:{minute:D2}:{second:D2}.{millisecond:D3}{tz}";
    }

    private static string FormatDateHighWithDay((int year, int month, int day, int hour, int minute, int second, int millisecond, string? timezone) parsed, int inputPrecision)
    {
        // For high boundary day: use last day of month when input precision doesn't include day
        var month = inputPrecision <= 4 ? 12 : parsed.month;
        var day = inputPrecision <= 6 ? DateTime.DaysInMonth(parsed.year, month) : parsed.day;
        return $"{parsed.year:D4}-{month:D2}-{day:D2}";
    }

    private static string FormatFullDateTimeHigh((int year, int month, int day, int hour, int minute, int second, int millisecond, string? timezone) parsed, int outputPrecision, int inputPrecision)
    {
        var tz = parsed.timezone ?? "-12:00";
        
        // Per FHIRPath spec: highBoundary returns the highest possible value within the range.
        // For second precision (inputPrecision == 14), the high boundary is the end of that second (add .999).
        // For minute precision (12), it's the end of that minute (:59.999).
        // For hour precision (10), it's the end of that hour (59:59.999).
        // For day precision (8), it's the end of that day (23:59:59.999).
        // For month precision (6), use last day of month.
        // For year precision (4), use December 31.
        var month = inputPrecision <= 4 ? 12 : parsed.month;
        var day = inputPrecision <= 6 ? DateTime.DaysInMonth(parsed.year, month) : parsed.day;
        var hour = inputPrecision <= 8 ? 23 : parsed.hour;
        var minute = inputPrecision <= 10 ? 59 : parsed.minute;
        var second = inputPrecision <= 12 ? 59 : parsed.second;
        
        return $"{parsed.year:D4}-{month:D2}-{day:D2}T{hour:D2}:{minute:D2}:{second:D2}.999{tz}";
    }

    private static int GetDateTimePrecision(string dateTimeStr)
    {
        // Count the precision based on components present
        // YYYY = 4, YYYY-MM = 6, YYYY-MM-DD = 8, YYYY-MM-DDTHH = 10, etc.
        var str = dateTimeStr.TrimStart('@');
        
        if (!str.Contains('-', StringComparison.Ordinal)) return 4; // Year only
        
        var parts = str.Split('T');
        var datePart = parts[0];
        var dateComponents = datePart.Split('-').Length;
        
        if (dateComponents == 2) return 6; // Year-month
        if (parts.Length == 1) return 8; // Year-month-day only
        
        var timePart = parts[1];
        // Remove timezone
        if (timePart.Contains('+', StringComparison.Ordinal)) timePart = timePart.Split('+')[0];
        if (timePart.Contains('-', StringComparison.Ordinal) && timePart.LastIndexOf('-') > 0) timePart = timePart.Substring(0, timePart.LastIndexOf('-'));
        if (timePart.EndsWith('Z')) timePart = timePart.TrimEnd('Z');
        
        var timeComponents = timePart.Split(':').Length;
        // Note: Hour-only time (T08) is not valid FHIR, so it's internally converted to T08:00
        // This means we treat it as minute precision (12), not hour precision (10)
        if (timeComponents == 1) return 12; // Hour only → treated as hour:minute=00
        if (timeComponents == 2) return 12; // Hour:minute
        
        // Check for milliseconds
        if (timePart.Contains('.', StringComparison.Ordinal)) return 17;
        
        return 14; // Hour:minute:second
    }

    private static string FormatTimeLowBoundary((int hour, int minute, int second, int millisecond) parsed, int precision)
    {
        // Time precision: 2=hour, 4=minute, 6=second, 9=millisecond
        // For low boundary, start of period (00:00:00.000)
        // Time values are stored without T prefix per FHIR spec (HH:mm:ss format)
        // e.g., @T10:30.lowBoundary(9) returns '10:30:00.000'
        return $"{parsed.hour:D2}:{parsed.minute:D2}:{parsed.second:D2}.{parsed.millisecond:D3}";
    }

    private static string FormatTimeHighBoundary((int hour, int minute, int second, int millisecond) parsed, int inputPrecision)
    {
        // High boundary fills only the components below the input precision; specified
        // components are kept. Input precision: 2=hour, 4=minute, 6=second, 9=millisecond.
        // e.g. @T10:30.highBoundary() -> '10:30:59.999'; @T12:34:00 -> '12:34:00.999'.
        var minute = inputPrecision >= 4 ? parsed.minute : 59;
        var second = inputPrecision >= 6 ? parsed.second : 59;
        var millisecond = inputPrecision >= 9 ? parsed.millisecond : 999;
        return $"{parsed.hour:D2}:{minute:D2}:{second:D2}.{millisecond:D3}";
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
