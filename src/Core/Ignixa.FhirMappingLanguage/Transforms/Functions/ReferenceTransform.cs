/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

using System.Text.Json.Nodes;
using Ignixa.Abstractions;

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// reference(source) - Creates a Reference to the source.
/// </summary>
internal class ReferenceTransform : ITransformFunction
{
    public string Name => "reference";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("reference() requires a source argument");

        var source = arguments[0];

        // If source is an IElement with an id, create a reference
        if (source is IElement element)
        {
            var resourceType = element.InstanceType;

            // Try to get id from children first, then from element value
            var idChildren = element.Children("id");
            var id = idChildren.Count > 0 ? idChildren[0].Value?.ToString() : element.Value?.ToString();

            if (resourceType != null && id != null)
            {
                var reference = new JsonObject
                {
                    ["reference"] = $"{resourceType}/{id}"
                };
                return reference;
            }
        }

        // Otherwise, treat as a reference string
        var referenceString = source.ToString()!;
        return new JsonObject
        {
            ["reference"] = referenceString
        };
    }
}
