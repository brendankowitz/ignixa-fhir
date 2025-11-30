/* Copyright (c) 2025, Ignixa Contributors */

using Ignixa.Abstractions;

namespace Ignixa.FhirMappingLanguage.Transforms;

/// <summary>
/// Context for transform function execution.
/// Provides access to sources, targets, variables, and services.
/// </summary>
public interface ITransformContext
{
    /// <summary>
    /// Gets a source element by name.
    /// </summary>
    IElement? GetSource(string name);

    /// <summary>
    /// Gets a target element by name.
    /// </summary>
    IElement? GetTarget(string name);

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
    Func<string, IElement>? ResourceCreator { get; }

    /// <summary>
    /// FHIRPath evaluator for evaluating expressions.
    /// </summary>
    Func<string, IElement, IEnumerable<IElement>>? FhirPathEvaluator { get; }

    /// <summary>
    /// ConceptMap resolver for terminology translation.
    /// </summary>
    Func<string, string, string, string?>? ConceptMapResolver { get; }
}
