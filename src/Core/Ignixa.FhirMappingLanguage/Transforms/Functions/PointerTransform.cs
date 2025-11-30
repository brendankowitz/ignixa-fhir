/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

using Ignixa.Abstractions;

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// pointer(source) - Returns a JSON Pointer to the source element.
/// </summary>
internal class PointerTransform : ITransformFunction
{
    public string Name => "pointer";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("pointer() requires a source argument");

        var source = arguments[0];

        if (source is IElement element)
        {
            return string.IsNullOrEmpty(element.Location) ? "/" : element.Location;
        }

        return "/";
    }
}
