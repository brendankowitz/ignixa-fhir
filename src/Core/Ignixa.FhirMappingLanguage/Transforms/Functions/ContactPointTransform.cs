/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

using System.Text.Json.Nodes;

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// cp(value) or cp(system, value, [use]) - Creates a ContactPoint.
/// </summary>
internal class ContactPointTransform : ITransformFunction
{
    public string Name => "cp";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count is < 1 or > 3)
        {
            throw new ArgumentException("cp() requires a value, or system and value with an optional use");
        }

        var value = TransformValue.String(arguments[arguments.Count == 1 ? 0 : 1]);
        var system = arguments.Count == 1 ? InferSystem(value) : TransformValue.String(arguments[0]);
        var use = arguments.Count > 2 ? TransformValue.String(arguments[2]) : null;

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

    private static string InferSystem(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return "url";
        }

        return value.Contains('@', StringComparison.Ordinal) ? "email" : "phone";
    }
}
