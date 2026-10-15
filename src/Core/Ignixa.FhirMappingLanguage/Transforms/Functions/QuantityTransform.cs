/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// qty(text) or qty(value, unit, [system, code]) - Creates a Quantity.
/// </summary>
internal class QuantityTransform : ITransformFunction
{
    private static readonly Regex TextPattern = new(
        @"^\s*(?<comparator><=|>=|<|>)?\s*(?<value>[+-]?(?:[0-9]+(?:\.[0-9]+)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?)\s*(?<unit>.*?)\s*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public string Name => "qty";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count is < 1 or > 4)
        {
            throw new ArgumentException("qty() requires text, or value and unit with optional system and code");
        }

        if (arguments.Count == 1)
        {
            return ParseText(TransformValue.String(arguments[0]));
        }

        var value = Convert.ToDecimal(TransformValue.Scalar(arguments[0]), CultureInfo.InvariantCulture);
        var unit = TransformValue.String(arguments[1]);

        var quantity = new JsonObject
        {
            ["value"] = value,
            ["unit"] = unit
        };

        if (arguments.Count > 2)
        {
            quantity["system"] = TransformValue.String(arguments[2]);
        }

        if (arguments.Count > 3)
        {
            quantity["code"] = TransformValue.String(arguments[3]);
        }

        return quantity;
    }

    private static JsonObject ParseText(string text)
    {
        Match match = TextPattern.Match(text);
        if (!match.Success ||
            !decimal.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value))
        {
            throw new FormatException("qty() text must contain an optional comparator, a decimal value, and an optional unit.");
        }

        var quantity = new JsonObject { ["value"] = value };
        if (match.Groups["comparator"].Success)
        {
            quantity["comparator"] = match.Groups["comparator"].Value;
        }

        if (match.Groups["unit"].Value.Length > 0)
        {
            quantity["unit"] = match.Groups["unit"].Value;
        }

        return quantity;
    }
}
