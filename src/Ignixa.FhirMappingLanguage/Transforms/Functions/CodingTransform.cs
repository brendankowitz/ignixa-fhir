/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

using System.Text.Json.Nodes;

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// c(system, code, [display]) - Creates a Coding.
/// </summary>
internal class CodingTransform : ITransformFunction
{
    public string Name => "c";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("c() requires system and code arguments");

        var system = arguments[0].ToString()!;
        var code = arguments[1].ToString()!;
        var display = arguments.Count > 2 ? arguments[2].ToString() : null;

        var coding = new JsonObject
        {
            ["system"] = system,
            ["code"] = code
        };

        if (display != null)
        {
            coding["display"] = display;
        }

        return coding;
    }
}
