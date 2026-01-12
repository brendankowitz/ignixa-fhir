/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath type conversion function implementations.
 * Implements toInteger(), toDecimal(), toString(), toBoolean(), toDate(), toDateTime(), toTime(), toQuantity(),
 * and their corresponding convertsTo* validation functions.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Attributes;
using Ignixa.FhirPath.Expressions;

namespace Ignixa.FhirPath.Evaluation.Functions;

/// <summary>
/// Type conversion function implementations for FhirPath expressions.
/// </summary>
internal static class TypeConversionFunctions
{
    #region Conversion Functions

    /// <summary>
    /// toInteger() - Converts a value to an integer.
    /// </summary>
    [FhirPathFunction("toInteger",
        SupportedContexts = "any-integer",
        ReturnType = "integer",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TypeConversion",
        Description = "Converts a value to an integer")]
    public static IEnumerable<IElement> ToInteger(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        var value = list[0].Value;
        if (value is int i)
            return [FunctionHelpers.CreateInteger(i)];

        if (value is string s)
        {
            s = s.Trim();
            if (int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return [FunctionHelpers.CreateInteger(parsed)];
        }

        if (value is decimal d && d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue)
            return [FunctionHelpers.CreateInteger((int)d)];

        if (value is bool b)
            return [FunctionHelpers.CreateInteger(b ? 1 : 0)];

        return [];
    }

    /// <summary>
    /// toDecimal() - Converts a value to a decimal.
    /// </summary>
    [FhirPathFunction("toDecimal",
        SupportedContexts = "any-decimal",
        ReturnType = "decimal",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TypeConversion",
        Description = "Converts a value to a decimal")]
    public static IEnumerable<IElement> ToDecimal(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        var value = list[0].Value;
        if (value is decimal d)
            return [FunctionHelpers.CreateDecimal(d)];

        if (value is int i)
            return [FunctionHelpers.CreateDecimal(i)];

        if (value is bool b)
            return [FunctionHelpers.CreateDecimal(b ? 1 : 0)];

        if (value is string s)
        {
            s = s.Trim();
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return [FunctionHelpers.CreateDecimal(parsed)];
        }

        return [];
    }

    /// <summary>
    /// toString() - Converts a value to a string.
    /// </summary>
    [FhirPathFunction("toString",
        SupportedContexts = "any-string",
        ReturnType = "string",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TypeConversion",
        Description = "Converts a value to a string")]
    public static IEnumerable<IElement> ToString(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        var value = list[0].Value;
        if (value == null)
            return [];

        return [FunctionHelpers.CreateString(value.ToString()!)];
    }

    /// <summary>
    /// toBoolean() - Converts a value to a boolean.
    /// </summary>
    [FhirPathFunction("toBoolean",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TypeConversion",
        Description = "Converts a value to a boolean")]
    public static IEnumerable<IElement> ToBoolean(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        var value = list[0].Value;
        if (value is bool b)
            return [FunctionHelpers.CreateBoolean(b)];

        if (value is int i && (i == 0 || i == 1))
            return [FunctionHelpers.CreateBoolean(i == 1)];

        if (value is string s)
        {
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase))
                return [FunctionHelpers.CreateBoolean(true)];
            if (s.Equals("false", StringComparison.OrdinalIgnoreCase))
                return [FunctionHelpers.CreateBoolean(false)];
        }

        return [];
    }

    /// <summary>
    /// toDate() - Converts a value to a date.
    /// </summary>
    [FhirPathFunction("toDate",
        SupportedContexts = "any-any",
        ReturnType = "date",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TypeConversion",
        Description = "Converts a value to a date")]
    public static IEnumerable<IElement> ToDate(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        var value = list[0].Value;
        if (value is string s)
        {
            s = s.Trim();
            if (IsValidFhirDate(s))
                return [FunctionHelpers.CreateDate(s)];
        }

        return [];
    }

    private static bool IsValidFhirDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split('-');
        if (parts.Length < 1 || parts.Length > 3)
            return false;

        if (!int.TryParse(parts[0], out var year) || parts[0].Length != 4)
            return false;

        if (parts.Length >= 2)
        {
            if (!int.TryParse(parts[1], out var month) || month < 1 || month > 12 || parts[1].Length != 2)
                return false;
        }

        if (parts.Length == 3)
        {
            if (!int.TryParse(parts[2], out var day) || day < 1 || day > 31 || parts[2].Length != 2)
                return false;

            try
            {
                _ = new DateTime(year, int.Parse(parts[1]), day);
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// toDateTime() - Converts a value to a dateTime.
    /// </summary>
    [FhirPathFunction("toDateTime",
        SupportedContexts = "any-any",
        ReturnType = "dateTime",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TypeConversion",
        Description = "Converts a value to a dateTime")]
    public static IEnumerable<IElement> ToDateTime(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        var value = list[0].Value;
        if (value is string s)
        {
            s = s.Trim();
            if (IsValidFhirDateTime(s))
                return [FunctionHelpers.CreateDateTime(s)];
        }

        return [];
    }

    private static bool IsValidFhirDateTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value.Contains('T', StringComparison.Ordinal))
        {
            var parts = value.Split('T');
            if (parts.Length != 2)
                return false;

            if (!IsValidFhirDate(parts[0]))
                return false;

            var timePart = parts[1];
            timePart = timePart.TrimEnd('Z');

            if (timePart.Contains('+', StringComparison.Ordinal) || (timePart.LastIndexOf('-') > 0))
            {
                var tzIndex = timePart.Contains('+', StringComparison.Ordinal)
                    ? timePart.LastIndexOf('+')
                    : timePart.LastIndexOf('-');
                timePart = timePart.Substring(0, tzIndex);
            }

            var timeComponents = timePart.Split(':');
            if (timeComponents.Length < 1 || timeComponents.Length > 3)
                return false;

            if (!int.TryParse(timeComponents[0], out var hour) || hour < 0 || hour > 23 || timeComponents[0].Length != 2)
                return false;

            if (timeComponents.Length >= 2)
            {
                if (!int.TryParse(timeComponents[1], out var minute) || minute < 0 || minute > 59 || timeComponents[1].Length != 2)
                    return false;
            }

            if (timeComponents.Length == 3)
            {
                var secondPart = timeComponents[2];
                if (secondPart.Contains('.', StringComparison.Ordinal))
                {
                    if (!decimal.TryParse(secondPart, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var second))
                        return false;
                    if (second < 0 || second >= 60)
                        return false;
                }
                else
                {
                    if (!int.TryParse(secondPart, out var second) || second < 0 || second > 59 || secondPart.Length != 2)
                        return false;
                }
            }

            return true;
        }

        return IsValidFhirDate(value);
    }

    /// <summary>
    /// toTime() - Converts a value to a time.
    /// </summary>
    [FhirPathFunction("toTime",
        SupportedContexts = "any-any",
        ReturnType = "time",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TypeConversion",
        Description = "Converts a value to a time")]
    public static IEnumerable<IElement> ToTime(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        var value = list[0].Value;
        if (value is string s)
        {
            s = s.Trim();
            if (TimeSpan.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out _))
                return [FunctionHelpers.CreateTime(s)];
        }

        return [];
    }

    /// <summary>
    /// toQuantity() - Converts a value to a quantity.
    /// </summary>
    [FhirPathFunction("toQuantity",
        SupportedContexts = "any-any",
        ReturnType = "quantity",
        MinArguments = 0,
        MaxArguments = 1,
        Category = "TypeConversion",
        Description = "Converts a value to a quantity")]
    public static IEnumerable<IElement> ToQuantity(IEnumerable<IElement> focus, IReadOnlyList<Expression> arguments)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        // Simplified implementation - just pass through for now
        return list;
    }

    #endregion

    #region Type Checking Functions

    /// <summary>
    /// convertsToInteger() - Returns true if value can be converted to integer.
    /// </summary>
    [FhirPathFunction("convertsToInteger",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TypeConversion",
        Description = "Returns true if value can be converted to integer")]
    public static IEnumerable<IElement> ConvertsToInteger(IEnumerable<IElement> focus)
    {
        var result = ToInteger(focus);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    /// <summary>
    /// convertsToDecimal() - Returns true if value can be converted to decimal.
    /// </summary>
    [FhirPathFunction("convertsToDecimal",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TypeConversion",
        Description = "Returns true if value can be converted to decimal")]
    public static IEnumerable<IElement> ConvertsToDecimal(IEnumerable<IElement> focus)
    {
        var result = ToDecimal(focus);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    /// <summary>
    /// convertsToString() - Returns true if value can be converted to string.
    /// </summary>
    [FhirPathFunction("convertsToString",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TypeConversion",
        Description = "Returns true if value can be converted to string")]
    public static IEnumerable<IElement> ConvertsToString(IEnumerable<IElement> focus)
    {
        var result = ToString(focus);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    /// <summary>
    /// convertsToBoolean() - Returns true if value can be converted to boolean.
    /// </summary>
    [FhirPathFunction("convertsToBoolean",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TypeConversion",
        Description = "Returns true if value can be converted to boolean")]
    public static IEnumerable<IElement> ConvertsToBoolean(IEnumerable<IElement> focus)
    {
        var result = ToBoolean(focus);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    /// <summary>
    /// convertsToDate() - Returns true if value can be converted to date.
    /// </summary>
    [FhirPathFunction("convertsToDate",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TypeConversion",
        Description = "Returns true if value can be converted to date")]
    public static IEnumerable<IElement> ConvertsToDate(IEnumerable<IElement> focus)
    {
        var result = ToDate(focus);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    /// <summary>
    /// convertsToDateTime() - Returns true if value can be converted to dateTime.
    /// </summary>
    [FhirPathFunction("convertsToDateTime",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TypeConversion",
        Description = "Returns true if value can be converted to dateTime")]
    public static IEnumerable<IElement> ConvertsToDateTime(IEnumerable<IElement> focus)
    {
        var result = ToDateTime(focus);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    /// <summary>
    /// convertsToTime() - Returns true if value can be converted to time.
    /// </summary>
    [FhirPathFunction("convertsToTime",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TypeConversion",
        Description = "Returns true if value can be converted to time")]
    public static IEnumerable<IElement> ConvertsToTime(IEnumerable<IElement> focus)
    {
        var result = ToTime(focus);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    /// <summary>
    /// convertsToQuantity() - Returns true if value can be converted to quantity.
    /// </summary>
    [FhirPathFunction("convertsToQuantity",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        MinArguments = 0,
        MaxArguments = 1,
        Category = "TypeConversion",
        Description = "Returns true if value can be converted to quantity")]
    public static IEnumerable<IElement> ConvertsToQuantity(IEnumerable<IElement> focus, IReadOnlyList<Expression> arguments)
    {
        var result = ToQuantity(focus, arguments);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    #endregion
}
