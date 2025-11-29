/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// append(source, suffix) - Appends suffix to source string.
/// </summary>
internal class AppendTransform : ITransformFunction
{
    public string Name => "append";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("append() requires source and suffix arguments");

        var source = arguments[0].ToString() ?? string.Empty;
        var suffix = arguments[1].ToString() ?? string.Empty;

        return source + suffix;
    }
}
