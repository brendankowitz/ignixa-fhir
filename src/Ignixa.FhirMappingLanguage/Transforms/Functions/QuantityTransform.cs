/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

using System.Text.Json.Nodes;

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// qty(value, unit, [system]) - Creates a Quantity.
/// </summary>
internal class QuantityTransform : ITransformFunction
{
    public string Name => "qty";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("qty() requires value and unit arguments");

        var value = Convert.ToDecimal(arguments[0]);
        var unit = arguments[1].ToString()!;
        var system = arguments.Count > 2 ? arguments[2].ToString() : "http://unitsofmeasure.org";

        var quantity = new JsonObject
        {
            ["value"] = value,
            ["unit"] = unit,
            ["system"] = system,
            ["code"] = unit
        };

        return quantity;
    }
}
