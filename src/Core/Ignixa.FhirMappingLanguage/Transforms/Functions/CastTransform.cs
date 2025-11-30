/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// cast(source, type) - Casts source to the specified type.
/// </summary>
internal class CastTransform : ITransformFunction
{
    public string Name => "cast";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("cast() requires source and type arguments");

        var source = arguments[0];
#pragma warning disable CA1308 // Normalize strings to uppercase - FHIR type names are lowercase by convention
        var targetType = arguments[1].ToString()!.ToLowerInvariant();
#pragma warning restore CA1308

        return targetType switch
        {
            "string" => source.ToString() ?? string.Empty,
            "integer" => Convert.ToInt32(source),
            "decimal" => Convert.ToDecimal(source),
            "boolean" => Convert.ToBoolean(source),
            _ => source // Pass through for complex types
        };
    }
}
