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
    /// toLong() - Converts a value to a 64-bit integer (Long).
    /// Per FHIRPath spec:
    /// - If input is Integer or Long, return as Long
    /// - If input is String convertible to 64-bit integer, return Long
    /// - If input is Boolean, true → 1L, false → 0L
    /// - Otherwise return empty
    /// </summary>
    [FhirPathFunction("toLong",
        SupportedContexts = "any-long",
        ReturnType = "long",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TypeConversion",
        Description = "Converts a value to a 64-bit integer (Long)")]
    public static IEnumerable<IElement> ToLong(IEnumerable<IElement> focus)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        var value = list[0].Value;
        if (value is long l)
            return [FunctionHelpers.CreateLong(l)];

        if (value is int i)
            return [FunctionHelpers.CreateLong(i)];

        if (value is string s)
        {
            s = s.Trim();
            if (long.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                return [FunctionHelpers.CreateLong(parsed)];
        }

        if (value is bool b)
            return [FunctionHelpers.CreateLong(b ? 1L : 0L)];

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

        if (value is long l)
            return [FunctionHelpers.CreateDecimal(l)];

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
    /// toQuantity([unit]) - Converts a value to a quantity, optionally converting to specified unit.
    /// When unit argument is provided, converts the quantity to the target unit using UCUM conversion rules.
    /// Returns empty if conversion is not possible.
    /// </summary>
    [FhirPathFunction("toQuantity",
        SupportedContexts = "any-any",
        ReturnType = "quantity",
        MinArguments = 0,
        MaxArguments = 1,
        Category = "TypeConversion",
        Description = "Converts a value to a quantity, optionally converting to a specified unit")]
    public static IEnumerable<IElement> ToQuantity(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        var list = focus.ToList();
        if (list.Count != 1)
            return [];

        var value = list[0].Value;
        Quantity? quantity = null;

        // If already a quantity, use it
        if (value is Quantity q)
        {
            quantity = q;
        }
        // Try to parse from string
        else if (value is string s)
        {
            quantity = TryParseQuantity(s);
        }
        // Convert numeric to dimensionless quantity with unit "1"
        else if (value is int i)
        {
            quantity = new Quantity(i, "1");
        }
        else if (value is decimal d)
        {
            quantity = new Quantity(d, "1");
        }
        // Convert boolean to dimensionless quantity (true=1, false=0)
        else if (value is bool b)
        {
            quantity = new Quantity(b ? 1 : 0, "1");
        }

        if (quantity == null)
            return [];

        // If unit argument is provided, convert to target unit
        if (arguments.Count > 0)
        {
            var unitResult = evaluateExpression(list, arguments[0], context).ToList();
            if (unitResult.Count == 1 && unitResult[0].Value is string targetUnit)
            {
                var converted = ConvertQuantityToUnit(quantity, targetUnit);
                if (converted == null)
                    return []; // Conversion not possible
                return [FunctionHelpers.CreateQuantity(converted)];
            }
            return []; // Invalid unit argument
        }

        return [FunctionHelpers.CreateQuantity(quantity)];
    }

    /// <summary>
    /// Converts a quantity to the specified target unit.
    /// Supports calendar duration conversions (year/month/day/hour/minute/second).
    /// </summary>
    private static Quantity? ConvertQuantityToUnit(Quantity source, string targetUnit)
    {
        // Normalize target unit (handle calendar keywords like 'year' → 'a')
        var normalizedTarget = CalendarDuration.GetUcumUnit(targetUnit) ?? targetUnit;
        var normalizedSource = CalendarDuration.GetUcumUnit(source.Unit) ?? source.Unit;

        // Same unit - no conversion needed
        if (string.Equals(normalizedSource, normalizedTarget, StringComparison.Ordinal))
            return new Quantity(source.Value, targetUnit);

        // Try calendar duration conversion
        var converted = TryCalendarDurationConversion(source.Value, normalizedSource, normalizedTarget);
        if (converted.HasValue)
        {
            // Use the target unit as specified (preserve keyword form if given)
            return new Quantity(converted.Value, targetUnit);
        }

        // Try UCUM conversion via the unit converter
        var converter = QuantityUnitConverter.Instance;
        var convertedValue = converter.Convert(source.Value, normalizedSource, normalizedTarget);
        if (convertedValue.HasValue)
            return new Quantity(convertedValue.Value, targetUnit);

        return null;
    }

    /// <summary>
    /// Attempts to convert between calendar duration units using FHIRPath conversion factors.
    /// Per FHIRPath spec: 1 year = 12 months = 365 days, 1 month = 30 days,
    /// 1 day = 24 hours, 1 hour = 60 minutes, 1 minute = 60 seconds.
    /// </summary>
    private static decimal? TryCalendarDurationConversion(decimal value, string fromUnit, string toUnit)
    {
        // Check if both are calendar duration units
        if (!CalendarDuration.IsCalendarDurationUnit(fromUnit) ||
            !CalendarDuration.IsCalendarDurationUnit(toUnit))
        {
            return null;
        }

        // Direct conversions per FHIRPath spec
        // Year-Month chain: 1 year = 12 months
        // Month-Day-Hour chain: 1 month = 30 days, 1 day = 24 hours, 1 hour = 60 min, 1 min = 60 sec
        // Year-Day: 1 year = 365 days (NOT 12*30=360)
        // Week: 1 week = 7 days

        // Convert to a common unit (seconds) using FHIRPath-specific factors
        // Note: year→month and month→day use different day counts (365/12 vs 30)
        
        // First, handle the special year↔month conversion directly
        if (fromUnit == "a" && toUnit == "mo")
            return value * 12m; // 1 year = 12 months
        if (fromUnit == "mo" && toUnit == "a")
            return value / 12m; // 12 months = 1 year

        // For all other conversions, use seconds as the intermediate unit
        // Year and month have different "day equivalents" depending on context:
        // - year→days: 365 days
        // - month→days: 30 days
        var toSeconds = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            { "a", 365m * 24m * 60m * 60m },      // year = 365 days in seconds
            { "mo", 30m * 24m * 60m * 60m },      // month = 30 days in seconds
            { "wk", 7m * 24m * 60m * 60m },       // week = 7 days in seconds
            { "d", 24m * 60m * 60m },             // day in seconds
            { "h", 60m * 60m },                   // hour in seconds
            { "min", 60m },                       // minute in seconds
            { "s", 1m },                          // second
            { "ms", 0.001m }                      // millisecond
        };

        if (!toSeconds.TryGetValue(fromUnit, out var fromFactor) ||
            !toSeconds.TryGetValue(toUnit, out var toFactor))
        {
            return null; // Not calendar duration units
        }

        // Convert: value * fromFactor / toFactor
        return value * fromFactor / toFactor;
    }

    /// <summary>
    /// Parses a quantity string like "5 'mg'" or "100 'kg'" or "1 day" into a Quantity.
    /// Per FHIRPath spec:
    /// - Calendar keywords (year, month, week, etc.) can be unquoted
    /// - UCUM units MUST be quoted (e.g., '5 \'kg\'' not '5 kg')
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

        // Check if this is a quoted UCUM unit (e.g., 'kg' or 'mg')
        if (unitStr.Length >= 2 && unitStr[0] == '\'' && unitStr[unitStr.Length - 1] == '\'')
        {
            // Remove quotes and use the UCUM unit directly
            var ucumUnit = unitStr.Substring(1, unitStr.Length - 2);
            if (!string.IsNullOrEmpty(ucumUnit))
                return new Quantity(value, ucumUnit);
            return null;
        }

        // Check if this is a calendar duration keyword (year, month, week, day, etc.)
        var ucumFromKeyword = CalendarDuration.GetUcumUnit(unitStr);
        if (ucumFromKeyword != null)
            return new Quantity(value, ucumFromKeyword);

        // Unquoted non-keyword unit is not valid per FHIRPath spec
        // (e.g., "1 wk" without quotes is not valid, "1 'wk'" is)
        return null;
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
        if (arguments.Count != 1)
            throw new InvalidOperationException("is() function requires exactly one argument");

        var typeArgument = arguments[0];
        string? targetTypeName = ExtractTypeName(typeArgument);

        if (string.IsNullOrEmpty(targetTypeName))
            throw new InvalidOperationException("is() function requires a type identifier as argument");

        var element = list[0];

        // Check if the element's type matches the target type
        var matches = IsTypeMatchWithNamespace(element, targetTypeName);
        return FunctionHelpers.ReturnBoolean(matches);
    }

    /// <summary>
    /// Extracts the full type name from a type expression, including namespace prefixes.
    /// Handles: System.Boolean, FHIR.Patient, Boolean, Patient, `Patient`
    /// </summary>
    private static string? ExtractTypeName(Expression expr)
    {
        return expr switch
        {
            // Simple identifier: Boolean, Patient, boolean
            Expressions.IdentifierExpression idExpr => idExpr.Name,

            // Property access: System.Boolean, FHIR.Patient
            Expressions.PropertyAccessExpression propExpr => ExtractPropertyAccessTypeName(propExpr),

            // Function call (used for backtick escaping): `Patient`
            Expressions.FunctionCallExpression funcExpr => funcExpr.FunctionName,

            // Constant (string literal type name)
            Expressions.ConstantExpression constExpr => constExpr.Value?.ToString(),

            _ => null
        };
    }

    private static string ExtractPropertyAccessTypeName(Expressions.PropertyAccessExpression propExpr)
    {
        // Build the full qualified name: System.Boolean, FHIR.Patient
        var parts = new List<string>();

        Expression? current = propExpr;
        while (current is Expressions.PropertyAccessExpression prop)
        {
            parts.Insert(0, prop.PropertyName);
            current = prop.Focus;
        }

        // Add the root identifier
        if (current is Expressions.IdentifierExpression id)
        {
            parts.Insert(0, id.Name);
        }

        return string.Join(".", parts);
    }

    // System types that are ONLY FHIRPath primitive types (not FHIR types)
    // These types exist only in FHIRPath, not as FHIR element types
    // Note: Date and Quantity exist as both System types and FHIR types, so they're NOT in this list.
    // IMPORTANT: Use case-SENSITIVE comparison because FHIRPath spec distinguishes:
    //   - Boolean (capitalized) = System type (FHIRPath literal)
    //   - boolean (lowercase) = FHIR type (element type)
    private static readonly HashSet<string> SystemOnlyTypes = new(StringComparer.Ordinal)
    {
        "Boolean", "Integer", "Decimal", "String", "DateTime", "Time"
    };

    private static bool IsTypeMatchWithNamespace(IElement element, string targetTypeName)
    {
        // Parse the target type to determine namespace and base type name
        // System types: System.Boolean, System.Integer, System.Decimal, System.String, System.Date, System.DateTime, System.Time, System.Quantity
        // FHIR types: FHIR.boolean, FHIR.Patient, FHIR.Quantity, etc.
        bool explicitSystemNamespace = false;
        bool explicitFhirNamespace = false;
        string typeName = targetTypeName;

        if (typeName.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
        {
            explicitSystemNamespace = true;
            typeName = typeName.Substring(7); // Remove "System." prefix
        }
        else if (typeName.StartsWith("FHIR.", StringComparison.OrdinalIgnoreCase))
        {
            explicitFhirNamespace = true;
            typeName = typeName.Substring(5); // Remove "FHIR." prefix
        }

        var elementType = element.InstanceType ?? string.Empty;

        // Check if element is a FHIRPath literal (System type) based on class name
        var implType = element.GetType().Name;
        bool elementIsSystemType = implType.Contains("Primitive", StringComparison.OrdinalIgnoreCase);

        // With explicit namespace, enforce strict matching
        if (explicitSystemNamespace)
        {
            // System.X requires element to be a FHIRPath literal
            if (!elementIsSystemType)
                return false;
        }
        else if (explicitFhirNamespace)
        {
            // FHIR.X requires element to NOT be a FHIRPath literal
            if (elementIsSystemType)
                return false;
        }
        else if (SystemOnlyTypes.Contains(typeName))
        {
            // Unqualified system-only types (Boolean, Integer, etc.) must match FHIRPath literals
            if (!elementIsSystemType)
                return false;
        }
        // For unqualified types that are NOT system-only (Patient, Quantity, code, boolean, etc.):
        // - Match FHIR element types directly by instance type
        // - This allows Observation.value.is(Quantity) to match FHIR Quantity elements

        // Now compare the type names (case-insensitive)
#pragma warning disable CA1308 // Normalize strings to uppercase
        typeName = typeName.ToLowerInvariant();
        elementType = elementType.ToLowerInvariant();
#pragma warning restore CA1308 // Normalize strings to uppercase

        if (elementType == typeName)
            return true;

        // Handle FHIR type inheritance:
        // URI subtypes: url, canonical, uuid, oid -> uri -> string
        // String subtypes: code, id, markdown, uri -> string
        // Integer subtypes: positiveInt, unsignedInt -> integer
        
        // URI subtypes inherit from uri
        if (typeName == "uri" && (elementType == "url" || elementType == "canonical" ||
            elementType == "uuid" || elementType == "oid"))
            return true;

        // String subtypes (including uri and its subtypes) inherit from string
        if (typeName == "string" && (elementType == "code" || elementType == "id" || 
            elementType == "markdown" || elementType == "uri" || elementType == "url" ||
            elementType == "canonical" || elementType == "uuid" || elementType == "oid"))
            return true;

        if (typeName == "integer" && (elementType == "positiveint" || elementType == "unsignedint"))
            return true;

        return false;
    }

    private static bool IsTypeMatch(IElement element, string targetTypeName)
    {
        // Legacy method for backward compatibility
        return IsTypeMatchWithNamespace(element, targetTypeName);
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
    /// convertsToLong() - Returns true if value can be converted to Long.
    /// Returns true if:
    /// - Item is Integer or Long
    /// - Item is String convertible to Long
    /// - Item is Boolean
    /// </summary>
    [FhirPathFunction("convertsToLong",
        SupportedContexts = "any-boolean",
        ReturnType = "boolean",
        MinArguments = 0,
        MaxArguments = 0,
        Category = "TypeConversion",
        Description = "Returns true if value can be converted to Long")]
    public static IEnumerable<IElement> ConvertsToLong(IEnumerable<IElement> focus)
    {
        var result = ToLong(focus);
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
    public static IEnumerable<IElement> ConvertsToQuantity(
        IEnumerable<IElement> focus,
        IReadOnlyList<Expression> arguments,
        EvaluationContext context,
        Func<IEnumerable<IElement>, Expression, EvaluationContext, IEnumerable<IElement>> evaluateExpression)
    {
        var result = ToQuantity(focus, arguments, context, evaluateExpression);
        return FunctionHelpers.ReturnBoolean(result.Any());
    }

    #endregion
}
