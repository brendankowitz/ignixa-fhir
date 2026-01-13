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
using Ignixa.FhirPath.Types;

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

        // Handle boolean: lowercase "true" or "false" per FHIRPath spec
        if (value is bool b)
            return [FunctionHelpers.CreateString(b ? "true" : "false")];

        // All other types use their standard ToString()
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

        if (value is decimal d && (d == 0 || d == 1))
            return [FunctionHelpers.CreateBoolean(d == 1)];

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

        var value = list[0].Value;

        // If already a quantity, return it
        if (value is Quantity)
            return list;

        // Try to parse from string
        if (value is string s)
        {
            var parsed = TryParseQuantity(s);
            if (parsed != null)
                return [new QuantityElement(parsed)];
        }

        // Convert numeric to dimensionless quantity with unit "1"
        if (value is int i)
            return [new QuantityElement(new Quantity(i, "1"))];

        if (value is decimal d)
            return [new QuantityElement(new Quantity(d, "1"))];

        // Convert boolean to dimensionless quantity (true=1, false=0)
        if (value is bool b)
            return [new QuantityElement(new Quantity(b ? 1 : 0, "1"))];

        return [];
    }

    /// <summary>
    /// Parses a quantity string like "5 mg" or "100 'kg'" into a Quantity.
    /// </summary>
    private static Quantity? TryParseQuantity(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var trimmed = input.Trim();

        // Split on first space to get value and unit
        var spaceIndex = trimmed.IndexOf(' ', StringComparison.Ordinal);
        if (spaceIndex < 0)
        {
            // No space - might be just a number (dimensionless)
            if (decimal.TryParse(trimmed, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var val))
                return new Quantity(val, "1");
            return null;
        }

        var valueStr = trimmed.Substring(0, spaceIndex);
        var unitStr = trimmed.Substring(spaceIndex + 1).Trim();

        if (!decimal.TryParse(valueStr, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value))
            return null;

        // Validate unit is not empty
        if (string.IsNullOrWhiteSpace(unitStr))
            return null;

        // Normalize calendar duration units (e.g. "day" -> "d")
        // This ensures '1 day'.toQuantity() works as expected
        var ucumUnit = CalendarDuration.GetUcumUnit(unitStr);
        if (ucumUnit != null)
            unitStr = ucumUnit;

        return new Quantity(value, unitStr);
    }

    /// <summary>
    /// IElement implementation for Quantity values.
    /// </summary>
    private class QuantityElement : IElement
    {
        private readonly Quantity _quantity;

        public QuantityElement(Quantity quantity)
        {
            _quantity = quantity ?? throw new ArgumentNullException(nameof(quantity));
        }

        public string Name => string.Empty;
        public string InstanceType => "Quantity";
        public object Value => _quantity;
        public string Location => string.Empty;
        public IType? Type => null;
        public IReadOnlyList<IElement> Children(string? name = null) => [];
        public T? Meta<T>() where T : class => null;
    }

    #endregion

    #region Type Checking Functions

    /// <summary>
    /// is(type) - Returns true if the value is of the specified type.
    /// </summary>
    [FhirPathFunction("is",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        MinArguments = 1,
        MaxArguments = 1,
        Category = "TypeConversion",
        Description = "Returns true if the value is of the specified type")]
    public static IEnumerable<IElement> Is(IEnumerable<IElement> focus, IReadOnlyList<Expression> arguments)
    {
        var list = focus.ToList();

        // Empty collection returns empty
        if (list.Count == 0)
            return [];

        // Multiple items signals an error
        if (list.Count > 1)
            throw new InvalidOperationException("is() function requires input collection to contain at most one item");

        // Get the type name from the argument
        // The argument should be an identifier expression representing the type
        if (arguments.Count != 1)
            throw new InvalidOperationException("is() function requires exactly one argument");

        var typeArgument = arguments[0];
        if (typeArgument is not Expressions.IdentifierExpression identifierExpr)
            throw new InvalidOperationException("is() function requires a type identifier as argument");

        var targetTypeName = identifierExpr.Name;
        var element = list[0];

        // Check if the element's type matches the target type
        var matches = IsTypeMatch(element, targetTypeName);
        return FunctionHelpers.ReturnBoolean(matches);
    }

    private static bool IsTypeMatch(IElement element, string targetTypeName)
    {
        // Normalize type names (case-insensitive comparison per FHIR spec)
        var elementTypeName = element.InstanceType;

        if (string.Equals(elementTypeName, targetTypeName, StringComparison.OrdinalIgnoreCase))
            return true;

        // Handle primitive type aliases
        // FHIRPath uses lowercase primitives (string, integer, decimal, boolean)
        // while FHIR uses capitalized types (String, Integer, Decimal, Boolean)
        var normalizedTargetType = targetTypeName.ToUpperInvariant();
        var normalizedElementType = elementTypeName.ToUpperInvariant();

        if (normalizedElementType == normalizedTargetType)
            return true;

        // Check for specific type mappings
        // FHIR has "code" which is a subtype of "string"
        if (normalizedTargetType == "STRING" && normalizedElementType == "CODE")
            return true;

        if (normalizedTargetType == "CODE" && normalizedElementType == "STRING")
        {
            // Only match if it's actually a code element
            return elementTypeName.Equals("code", StringComparison.OrdinalIgnoreCase);
        }

        // Check if element type inherits from target type
        // This would require type hierarchy information from the schema
        // For now, we'll do basic checks

        return false;
    }

    #endregion

    #region Conversion Checking Functions

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
