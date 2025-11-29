/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

using Ignixa.Abstractions;

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// evaluate(source, path) - Evaluates a FHIRPath expression against the source.
/// </summary>
internal class EvaluateTransform : ITransformFunction
{
    public string Name => "evaluate";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        if (arguments.Count < 2)
            throw new ArgumentException("evaluate() requires source and path arguments");

        if (context.FhirPathEvaluator == null)
            throw new InvalidOperationException("FhirPathEvaluator not configured in context");

        var source = arguments[0];
        var path = arguments[1].ToString()!;

        // Source should be an IElement
        if (source is not IElement element)
            throw new ArgumentException("evaluate() requires source to be an IElement");

        var results = context.FhirPathEvaluator(path, element);
        return results.FirstOrDefault()?.Value ?? string.Empty;
    }
}
