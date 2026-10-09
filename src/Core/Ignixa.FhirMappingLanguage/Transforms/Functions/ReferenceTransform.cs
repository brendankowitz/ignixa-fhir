/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

using Ignixa.Abstractions;

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// reference(source) - Returns a reference string for the source resource.
/// </summary>
internal class ReferenceTransform : ITransformFunction
{
    public string Name => "reference";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("reference() requires a source argument");

        var source = arguments[0];

        if (source is IElement element)
        {
            if (element.InstanceType == "Reference")
            {
                var references = element.Children("reference");
                return (references.Count > 0 ? references[0].Value?.ToString() : null)
                    ?? throw new InvalidOperationException("reference() requires a reference value.");
            }

            if (element.Value is string primitive &&
                !string.IsNullOrEmpty(element.InstanceType) &&
                !char.IsUpper(element.InstanceType[0]))
            {
                return primitive;
            }

            var resourceType = element.InstanceType;
            var idChildren = element.Children("id");
            var id = idChildren.Count > 0 ? idChildren[0].Value?.ToString() : element.Value?.ToString();

            if (!string.IsNullOrEmpty(resourceType) && !string.IsNullOrEmpty(id))
            {
                return $"{resourceType}/{id}";
            }

            throw new InvalidOperationException("reference() requires a resource with an id.");
        }

        return TransformValue.String(source);
    }
}
