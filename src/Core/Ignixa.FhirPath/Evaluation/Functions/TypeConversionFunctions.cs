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

        if (value is string s && int.TryParse(s, out var parsed))
            return [FunctionHelpers.CreateInteger(parsed)];

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

        if (value is string s && decimal.TryParse(s, out var parsed))
            return [FunctionHelpers.CreateDecimal(parsed)];

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

        // Simplified: Just return the value if it's a date/datetime string
        var value = list[0].Value;
        if (value is string s)
        {
            // Basic validation for FHIR date format (YYYY-MM-DD)
            if (DateTime.TryParse(s, out _))
                return [FunctionHelpers.CreateDate(s)];
        }

        return [];
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
            if (DateTime.TryParse(s, out _))
                return [FunctionHelpers.CreateDateTime(s)];
        }

        return [];
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
            // Basic validation for time format
            if (TimeSpan.TryParse(s, out _))
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
