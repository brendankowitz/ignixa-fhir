/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

using System.Text.Json.Nodes;

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// cc(system, code, [display]) - Creates a CodeableConcept.
/// </summary>
internal class CodeableConceptTransform : ITransformFunction
{
    public string Name => "cc";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("cc() requires system and code arguments");

        var system = arguments[0].ToString()!;
        var code = arguments[1].ToString()!;
        var display = arguments.Count > 2 ? arguments[2].ToString() : null;

        // Create a simple CodeableConcept structure
        var codeableConcept = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = system,
                    ["code"] = code,
                    ["display"] = display
                }
            }
        };

        return codeableConcept;
    }
}
