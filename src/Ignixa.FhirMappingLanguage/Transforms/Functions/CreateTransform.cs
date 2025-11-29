/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// create(type) - Creates a new FHIR resource or element of the specified type.
/// </summary>
internal class CreateTransform : ITransformFunction
{
    public string Name => "create";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count == 0)
            throw new ArgumentException("create() requires a type argument");

        var typeName = arguments[0].ToString()!;

        if (context.ResourceCreator == null)
            throw new InvalidOperationException("ResourceCreator not configured in context");

        return context.ResourceCreator(typeName);
    }
}
