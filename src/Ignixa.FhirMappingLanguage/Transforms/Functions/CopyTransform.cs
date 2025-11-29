/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// copy(source) - Copies the source value to the target.
/// </summary>
internal class CopyTransform : ITransformFunction
{
    public string Name => "copy";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("copy() requires a source argument");

        // Direct copy of the value
        return arguments[0];
    }
}
