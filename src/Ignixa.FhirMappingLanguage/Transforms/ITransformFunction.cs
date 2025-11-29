/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Transform function interface for FHIR Mapping Language.
 */

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// Interface for transform functions in FHIR Mapping Language.
/// Transform functions convert source data into target data.
/// </summary>
public interface ITransformFunction
{
    /// <summary>
    /// The name of the transform function.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Executes the transform function with the provided arguments.
    /// </summary>
    /// <param name="arguments">Arguments to the transform function</param>
    /// <param name="context">Execution context with sources, targets, and variables</param>
    /// <returns>The result of the transformation</returns>
    object Execute(IReadOnlyList<object> arguments, ITransformContext context);
}
