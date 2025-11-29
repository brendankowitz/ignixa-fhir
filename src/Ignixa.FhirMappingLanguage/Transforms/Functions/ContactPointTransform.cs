/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

using System.Text.Json.Nodes;

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// cp(system, value, [use]) - Creates a ContactPoint.
/// </summary>
internal class ContactPointTransform : ITransformFunction
{
    public string Name => "cp";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("cp() requires system and value arguments");

        var system = arguments[0].ToString()!;
        var value = arguments[1].ToString()!;
        var use = arguments.Count > 2 ? arguments[2].ToString() : null;

        var contactPoint = new JsonObject
        {
            ["system"] = system,
            ["value"] = value
        };

        if (use != null)
        {
            contactPoint["use"] = use;
        }

        return contactPoint;
    }
}
