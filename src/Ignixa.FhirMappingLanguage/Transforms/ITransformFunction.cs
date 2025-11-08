/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Transform function interface for FHIR Mapping Language.
 */

using Ignixa.Abstractions;

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

/// <summary>
/// Context for transform function execution.
/// Provides access to sources, targets, variables, and services.
/// </summary>
public interface ITransformContext
{
    /// <summary>
    /// Gets a source element by name.
    /// </summary>
    ITypedElement? GetSource(string name);

    /// <summary>
    /// Gets a target element by name.
    /// </summary>
    ITypedElement? GetTarget(string name);

    /// <summary>
    /// Gets a variable value by name.
    /// </summary>
    object? GetVariable(string name);

    /// <summary>
    /// Sets a variable value.
    /// </summary>
    void SetVariable(string name, object value);

    /// <summary>
    /// Resource factory for creating new FHIR resources.
    /// </summary>
    Func<string, ITypedElement>? ResourceCreator { get; }

    /// <summary>
    /// FHIRPath evaluator for evaluating expressions.
    /// </summary>
    Func<string, ITypedElement, IEnumerable<ITypedElement>>? FhirPathEvaluator { get; }

    /// <summary>
    /// ConceptMap resolver for terminology translation.
    /// </summary>
    Func<string, string, string, string?>? ConceptMapResolver { get; }
}
