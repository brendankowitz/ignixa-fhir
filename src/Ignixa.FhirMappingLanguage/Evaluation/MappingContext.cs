/*
 * Copyright (c) 2025, Sparky Contributors
 *
 * Evaluation context for FHIR Mapping Language.
 */

using Ignixa.FhirMappingLanguage.Transforms;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.FhirMappingLanguage.Evaluation;

/// <summary>
/// Context for evaluating FHIR mapping expressions.
/// Holds variables, source/target resources, and configuration.
/// </summary>
public class MappingContext : ITransformContext
{
    private readonly Dictionary<string, object> _variables = new();
    private readonly Dictionary<string, ITypedElement> _sources = new();
    private readonly Dictionary<string, ITypedElement> _targets = new();

    /// <summary>
    /// Gets or sets a variable in the context.
    /// </summary>
    public object? GetVariable(string name) =>
        _variables.TryGetValue(name, out var value) ? value : null;

    public void SetVariable(string name, object value) =>
        _variables[name] = value;

    public void RemoveVariable(string name) =>
        _variables.Remove(name);

    /// <summary>
    /// Gets or sets a source element in the context.
    /// </summary>
    public ITypedElement? GetSource(string name) =>
        _sources.TryGetValue(name, out var value) ? value : null;

    public void SetSource(string name, ITypedElement element) =>
        _sources[name] = element;

    /// <summary>
    /// Gets or sets a target element in the context.
    /// </summary>
    public ITypedElement? GetTarget(string name) =>
        _targets.TryGetValue(name, out var value) ? value : null;

    public void SetTarget(string name, ITypedElement element) =>
        _targets[name] = element;

    /// <summary>
    /// FHIRPath evaluator for evaluating embedded FHIRPath expressions.
    /// </summary>
    public Func<string, ITypedElement, IEnumerable<ITypedElement>>? FhirPathEvaluator { get; set; }

    /// <summary>
    /// Transform function resolver.
    /// </summary>
    public Func<string, IEnumerable<object>, object>? TransformResolver { get; set; }

    /// <summary>
    /// Resource creator for creating new FHIR resources.
    /// </summary>
    public Func<string, ITypedElement>? ResourceCreator { get; set; }

    /// <summary>
    /// ConceptMap resolver for terminology translation.
    /// </summary>
    public Func<string, string, string, string?>? ConceptMapResolver { get; set; }
}
