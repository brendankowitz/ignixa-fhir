/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Types;

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// cast(source, type) - Casts source to the specified type.
/// </summary>
internal class CastTransform : ITransformFunction
{
    private static readonly Regex DatePattern = new(
        @"\A[0-9]{4}(?:-[0-9]{2}(?:-[0-9]{2})?)?\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex DateTimePattern = new(
        @"\A[0-9]{4}(?:-[0-9]{2}(?:-[0-9]{2}(?:T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]+)?(?:Z|[+-][0-9]{2}:[0-9]{2}))?)?)?\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex TimePattern = new(
        @"\A[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]+)?\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public string Name => "cast";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count != 2)
            throw new ArgumentException("cast() requires source and type arguments");

        ArgumentNullException.ThrowIfNull(arguments[0]);
        var typeName = TransformValue.String(arguments[1]);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        var targetType = GetFhirTypeName(typeName);
        if (targetType == "Quantity")
            return CastQuantity(arguments[0], typeName.StartsWith("System.", StringComparison.OrdinalIgnoreCase));

        if (targetType is null)
        {
            var complexType = typeName.StartsWith("FHIR.", StringComparison.OrdinalIgnoreCase) ? typeName[5..] : typeName;
            if (arguments[0] is IElement element && !element.HasPrimitiveValue &&
                element.InstanceType.Equals(complexType, StringComparison.Ordinal))
                return element;

            throw new NotSupportedException($"cast() does not support conversion to '{typeName}'.");
        }

        var source = TransformValue.Scalar(arguments[0])
            ?? throw new ArgumentException("cast() requires a source with a value.");
        return targetType switch
        {
            "string" or "uri" or "url" or "canonical" or "code" or "oid" or "id" or "uuid" or "markdown" =>
                TransformValue.String(source),
            "base64Binary" => source is byte[] bytes ? Convert.ToBase64String(bytes) : TransformValue.String(source),
            "integer" => checked((int)ToInteger64(source)),
            "unsignedInt" => ConstrainedInteger(source, 0),
            "positiveInt" => ConstrainedInteger(source, 1),
            "integer64" => ToInteger64(source),
            "decimal" => Convert.ToDecimal(source, CultureInfo.InvariantCulture),
            "boolean" => Convert.ToBoolean(source, CultureInfo.InvariantCulture),
            "date" or "dateTime" or "instant" or "time" => CastTemporal(arguments[0], targetType),
            _ => throw new NotSupportedException($"cast() does not support conversion to '{typeName}'.")
        };
    }

    internal static string? GetFhirTypeName(string typeName)
    {
        var name = typeName.ToLowerInvariant();
        if (name.StartsWith("system.", StringComparison.Ordinal))
        {
            return name[7..] switch
            {
                "boolean" => "boolean",
                "string" => "string",
                "integer" => "integer",
                "long" => "integer64",
                "decimal" => "decimal",
                "date" => "date",
                "datetime" => "dateTime",
                "time" => "time",
                "quantity" => "Quantity",
                _ => null
            };
        }
        if (name.StartsWith("fhir.", StringComparison.Ordinal))
            name = name[5..];

        return name switch
        {
            "boolean" or "string" or "uri" or "url" or "canonical" or "code" or "oid" or "id" or "uuid" or
                "markdown" or "integer" or "decimal" or "date" or "instant" or "time" or "integer64" => name,
            "long" => "integer64",
            "base64binary" => "base64Binary",
            "unsignedint" => "unsignedInt",
            "positiveint" => "positiveInt",
            "datetime" => "dateTime",
            "quantity" => "Quantity",
            _ => null
        };
    }

    private static long ToInteger64(object source)
    {
        if (source is decimal or double or float)
        {
            var number = Convert.ToDecimal(source, CultureInfo.InvariantCulture);
            if (number != decimal.Truncate(number))
                throw new FormatException("cast() cannot convert a fractional number to an integer.");
        }
        return Convert.ToInt64(source, CultureInfo.InvariantCulture);
    }

    private static int ConstrainedInteger(object source, int minimum)
    {
        var value = checked((int)ToInteger64(source));
        if (value < minimum)
            throw new OverflowException($"cast() requires an integer between {minimum} and {int.MaxValue}.");
        return value;
    }

    private static FhirTemporal CastTemporal(object source, string typeName)
    {
        var scalar = TransformValue.Scalar(source);
        var literal = scalar switch
        {
            FhirTemporal temporal => temporal.Literal,
            DateTimeOffset offset => offset.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture),
            _ => TransformValue.String(scalar)
        };
        if (typeName == "date" && (scalar is FhirTemporal or DateTime or DateTimeOffset ||
            source is IElement { InstanceType: "dateTime" or "instant" or "System.DateTime" }))
            literal = literal.Split('T')[0];
        if (typeName == "time" && scalar is DateTime or DateTimeOffset)
            literal = scalar is DateTimeOffset offset
                ? offset.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture)
                : ((DateTime)scalar).ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);

        var pattern = typeName switch
        {
            "date" => DatePattern,
            "time" => TimePattern,
            _ => DateTimePattern
        };
        var kind = FhirPrimitiveExtensions.FromTypeString(typeName);
        if (!pattern.IsMatch(literal) ||
            !FhirTemporal.TryParse(literal, kind, out var result) ||
            (kind == FhirPrimitive.Instant && !result.HasTimezone))
            throw new FormatException($"cast() source is not a valid {typeName} value.");

        return result;
    }

    private static object CastQuantity(object source, bool systemQuantity)
    {
        if (source is FhirQuantity quantity)
            return systemQuantity ? quantity : ToFhirQuantity(quantity);
        if (source is IElement { Value: FhirQuantity value })
            return systemQuantity ? value : ToFhirQuantity(value);
        if (source is not IElement { InstanceType: "Quantity" } element)
            throw new ArgumentException("cast() to Quantity requires a FHIR Quantity or a FHIRPath quantity value.");
        if (!systemQuantity)
            return element;

        var system = element.Children("system").SingleOrDefault()?.Value as string;
        var code = element.Children("code").SingleOrDefault()?.Value as string;
        var amount = element.Children("value").SingleOrDefault()?.Value;
        if (system != "http://unitsofmeasure.org" || string.IsNullOrEmpty(code) || amount is null ||
            element.Children("comparator").Count != 0)
            throw new InvalidOperationException("cast() to System.Quantity requires an exact value and a UCUM system/code.");

        var unit = code is "a" or "mo" or "d" or "h" or "min" or "s"
            ? CalendarDuration.GetKeywordFromUcum(code)!
            : code;
        return new FhirQuantity(Convert.ToDecimal(amount, CultureInfo.InvariantCulture), unit);
    }

    internal static JsonObject ToFhirQuantity(FhirQuantity quantity) => new()
    {
        ["value"] = quantity.Value,
        ["unit"] = quantity.Unit,
        ["system"] = "http://unitsofmeasure.org",
        ["code"] = CalendarDuration.NormalizeToUcum(quantity.Unit)
    };
}
