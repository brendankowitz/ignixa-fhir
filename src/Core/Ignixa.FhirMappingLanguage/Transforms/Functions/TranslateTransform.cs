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
        if (arguments.Count != 3)
        {
            throw new ArgumentException("translate() requires source, map_uri, and output arguments");
        }

        var source = arguments[0];
        var conceptMapUrl = TransformValue.String(arguments[1]);
        var output = TransformValue.String(arguments[2]);
        if (output is not ("code" or "system" or "display" or "Coding" or "CodeableConcept"))
        {
            throw new ArgumentException($"Unsupported translate() output '{output}'.");
        }

        if (context.TranslationResolver is { } resolver)
        {
            return resolver(source, conceptMapUrl, output)!;
        }

        if (output != "code")
        {
            throw new NotSupportedException($"translate() output '{output}' requires a TranslationResolver.");
        }

        if (context.ConceptMapResolver is not { } legacyResolver)
        {
            throw new InvalidOperationException("TranslationResolver or ConceptMapResolver must be configured in context.");
        }

        if (TransformValue.Scalar(source) is not string sourceCode)
        {
            throw new NotSupportedException("ConceptMapResolver requires a primitive source code; structured sources require a TranslationResolver.");
        }

        return legacyResolver(conceptMapUrl, sourceCode, string.Empty)!;
    }
}
