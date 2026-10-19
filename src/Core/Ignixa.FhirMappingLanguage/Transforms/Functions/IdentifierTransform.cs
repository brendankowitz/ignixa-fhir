/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

using System.Text.Json.Nodes;

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// id(system, value, [type]) - Creates an Identifier.
/// </summary>
internal class IdentifierTransform : ITransformFunction
{
    public string Name => "id";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count is < 1 or > 3)
        {
            throw new ArgumentException("id() requires system and value with an optional identifier type");
        }

        // Retain the legacy single-value form without reversing the standard system/value form.
        var value = TransformValue.String(arguments[arguments.Count == 1 ? 0 : 1]);
        var system = arguments.Count > 1 ? TransformValue.String(arguments[0]) : null;

        var identifier = new JsonObject
        {
            ["value"] = value
        };

        if (system != null)
        {
            identifier["system"] = system;
        }

        if (arguments.Count == 3)
        {
            identifier["type"] = new JsonObject
            {
                ["coding"] = new JsonArray(new JsonObject
                {
                    ["system"] = "http://terminology.hl7.org/CodeSystem/v2-0203",
                    ["code"] = TransformValue.String(arguments[2])
                })
            };
        }

        return identifier;
    }
}
