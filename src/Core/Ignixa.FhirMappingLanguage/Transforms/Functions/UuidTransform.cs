/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// uuid() - Generates a new UUID.
/// </summary>
internal class UuidTransform : ITransformFunction
{
    public string Name => "uuid";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        return Guid.NewGuid().ToString();
    }
}
