/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * FhirPath type conversion function implementations.
 * Implements toInteger(), toDecimal(), toString(), toBoolean(), toDate(), toDateTime(), toTime(), toQuantity(),
 * and their corresponding convertsTo* validation functions.
 */

using Ignixa.Abstractions;
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
    public static IEnumerable<ITypedElement> ToInteger(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        var value = list[0].Value;
        if (value is int i)
            return new[] { FunctionHelpers.CreateInteger(i) };

        if (value is string s && int.TryParse(s, out var parsed))
            return new[] { FunctionHelpers.CreateInteger(parsed) };

        if (value is decimal d && d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue)
            return new[] { FunctionHelpers.CreateInteger((int)d) };

        if (value is bool b)
            return new[] { FunctionHelpers.CreateInteger(b ? 1 : 0) };

        return Enumerable.Empty<ITypedElement>();
    }

    /// <summary>
    /// toDecimal() - Converts a value to a decimal.
    /// </summary>
    public static IEnumerable<ITypedElement> ToDecimal(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        var value = list[0].Value;
        if (value is decimal d)
            return new[] { FunctionHelpers.CreateDecimal(d) };

        if (value is int i)
            return new[] { FunctionHelpers.CreateDecimal(i) };

        if (value is string s && decimal.TryParse(s, out var parsed))
            return new[] { FunctionHelpers.CreateDecimal(parsed) };

        return Enumerable.Empty<ITypedElement>();
    }

    /// <summary>
    /// toString() - Converts a value to a string.
    /// </summary>
    public static IEnumerable<ITypedElement> ToString(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        var value = list[0].Value;
        if (value == null)
            return Enumerable.Empty<ITypedElement>();

        return new[] { FunctionHelpers.CreateString(value.ToString()!) };
    }

    /// <summary>
    /// toBoolean() - Converts a value to a boolean.
    /// </summary>
    public static IEnumerable<ITypedElement> ToBoolean(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        var value = list[0].Value;
        if (value is bool b)
            return new[] { FunctionHelpers.CreateBoolean(b) };

        if (value is int i && (i == 0 || i == 1))
            return new[] { FunctionHelpers.CreateBoolean(i == 1) };

        if (value is string s)
        {
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase))
                return new[] { FunctionHelpers.CreateBoolean(true) };
            if (s.Equals("false", StringComparison.OrdinalIgnoreCase))
                return new[] { FunctionHelpers.CreateBoolean(false) };
        }

        return Enumerable.Empty<ITypedElement>();
    }

    /// <summary>
    /// toDate() - Converts a value to a date.
    /// </summary>
    public static IEnumerable<ITypedElement> ToDate(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        // Simplified: Just return the value if it's a date/datetime string
        var value = list[0].Value;
        if (value is string s)
        {
            // Basic validation for FHIR date format (YYYY-MM-DD)
            if (DateTime.TryParse(s, out _))
                return new[] { FunctionHelpers.CreateDate(s) };
        }

        return Enumerable.Empty<ITypedElement>();
    }

    /// <summary>
    /// toDateTime() - Converts a value to a dateTime.
    /// </summary>
    public static IEnumerable<ITypedElement> ToDateTime(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        var value = list[0].Value;
        if (value is string s)
        {
            if (DateTime.TryParse(s, out _))
                return new[] { FunctionHelpers.CreateDateTime(s) };
        }

        return Enumerable.Empty<ITypedElement>();
    }

    /// <summary>
    /// toTime() - Converts a value to a time.
    /// </summary>
    public static IEnumerable<ITypedElement> ToTime(IEnumerable<ITypedElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        var value = list[0].Value;
        if (value is string s)
        {
            // Basic validation for time format
            if (TimeSpan.TryParse(s, out _))
                return new[] { FunctionHelpers.CreateTime(s) };
        }

        return Enumerable.Empty<ITypedElement>();
    }

    /// <summary>
    /// toQuantity() - Converts a value to a quantity.
    /// </summary>
    public static IEnumerable<ITypedElement> ToQuantity(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return Enumerable.Empty<ITypedElement>();

        // Simplified implementation - just pass through for now
        return list;
    }

    #endregion

    #region Type Checking Functions

    /// <summary>
    /// convertsToInteger() - Returns true if value can be converted to integer.
    /// </summary>
    public static IEnumerable<ITypedElement> ConvertsToInteger(IEnumerable<ITypedElement> focus)
    {
        var result = ToInteger(focus);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    /// <summary>
    /// convertsToDecimal() - Returns true if value can be converted to decimal.
    /// </summary>
    public static IEnumerable<ITypedElement> ConvertsToDecimal(IEnumerable<ITypedElement> focus)
    {
        var result = ToDecimal(focus);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    /// <summary>
    /// convertsToString() - Returns true if value can be converted to string.
    /// </summary>
    public static IEnumerable<ITypedElement> ConvertsToString(IEnumerable<ITypedElement> focus)
    {
        var result = ToString(focus);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    /// <summary>
    /// convertsToBoolean() - Returns true if value can be converted to boolean.
    /// </summary>
    public static IEnumerable<ITypedElement> ConvertsToBoolean(IEnumerable<ITypedElement> focus)
    {
        var result = ToBoolean(focus);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    /// <summary>
    /// convertsToDate() - Returns true if value can be converted to date.
    /// </summary>
    public static IEnumerable<ITypedElement> ConvertsToDate(IEnumerable<ITypedElement> focus)
    {
        var result = ToDate(focus);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    /// <summary>
    /// convertsToDateTime() - Returns true if value can be converted to dateTime.
    /// </summary>
    public static IEnumerable<ITypedElement> ConvertsToDateTime(IEnumerable<ITypedElement> focus)
    {
        var result = ToDateTime(focus);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    /// <summary>
    /// convertsToTime() - Returns true if value can be converted to time.
    /// </summary>
    public static IEnumerable<ITypedElement> ConvertsToTime(IEnumerable<ITypedElement> focus)
    {
        var result = ToTime(focus);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    /// <summary>
    /// convertsToQuantity() - Returns true if value can be converted to quantity.
    /// </summary>
    public static IEnumerable<ITypedElement> ConvertsToQuantity(IEnumerable<ITypedElement> focus, IReadOnlyList<Expression> arguments)
    {
        var result = ToQuantity(focus, arguments);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    #endregion
}
