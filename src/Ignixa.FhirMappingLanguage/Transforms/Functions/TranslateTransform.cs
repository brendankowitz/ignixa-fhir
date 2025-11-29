/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// translate(source, map_uri, output) - Translates a code using a ConceptMap.
/// </summary>
internal class TranslateTransform : ITransformFunction
{
    public string Name => "translate";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 3)
            throw new ArgumentException("translate() requires conceptMap, sourceSystem, and sourceCode arguments");

        if (context.ConceptMapResolver == null)
            throw new InvalidOperationException("ConceptMapResolver not configured in context");

        var conceptMapUrl = arguments[0].ToString()!;
        var sourceSystem = arguments[1].ToString()!;
        var sourceCode = arguments[2].ToString()!;

        var translated = context.ConceptMapResolver(conceptMapUrl, sourceSystem, sourceCode);

        return translated!; // Return null if translation fails (let caller handle)
    }
}
