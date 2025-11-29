/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// truncate(source, length) - Truncates a string to the specified length.
/// </summary>
internal class TruncateTransform : ITransformFunction
{
    public string Name => "truncate";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("truncate() requires source and length arguments");

        var source = arguments[0].ToString() ?? string.Empty;
        var length = Convert.ToInt32(arguments[1]);

        return source.Length <= length ? source : source.Substring(0, length);
    }
}
