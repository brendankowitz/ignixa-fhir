/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

using System.Text.Json.Nodes;

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// id(value, [system]) - Creates an Identifier.
/// </summary>
internal class IdentifierTransform : ITransformFunction
{
    public string Name => "id";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("id() requires a value argument");

        var value = arguments[0].ToString()!;
        var system = arguments.Count > 1 ? arguments[1].ToString() : null;

        var identifier = new JsonObject
        {
            ["value"] = value
        };

        if (system != null)
        {
            identifier["system"] = system;
        }

        return identifier;
    }
}
