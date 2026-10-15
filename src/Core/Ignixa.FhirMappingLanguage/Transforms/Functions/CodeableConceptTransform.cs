/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

using System.Text.Json.Nodes;

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// cc(text) or cc(system, code, [display]) - Creates a CodeableConcept.
/// </summary>
internal class CodeableConceptTransform : ITransformFunction
{
    public string Name => "cc";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count is < 1 or > 3)
        {
            throw new ArgumentException("cc() requires text, or system and code with an optional display");
        }

        if (arguments.Count == 1)
        {
            return new JsonObject { ["text"] = TransformValue.String(arguments[0]) };
        }

        var coding = (JsonObject)new CodingTransform().Execute(arguments, context);
        return new JsonObject { ["coding"] = new JsonArray(coding) };
    }
}
