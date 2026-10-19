/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Standard transform functions for FHIR Mapping Language.
 * Implements the built-in transforms defined in the FHIR specification.
 */

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// append(source...) - Concatenates source values in order.
/// </summary>
internal class AppendTransform : ITransformFunction
{
    public string Name => "append";

    public object Execute(IReadOnlyList<object> arguments, ITransformContext context)
    {
        return string.Concat(arguments.Select(TransformValue.String));
    }
}
