/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Evaluation context for FHIR Mapping Language.
 */

using Ignixa.FhirMappingLanguage.Transforms;
using Ignixa.Abstractions;

namespace Ignixa.FhirMappingLanguage.Evaluation;

/// <summary>
/// Context for evaluating FHIR mapping expressions.
/// Holds variables, source/target resources, and configuration.
/// </summary>
public class MappingContext : ITransformContext
{
    private readonly Dictionary<string, object> _variables = new();
    private readonly Dictionary<string, IElement> _sources = new();
    private readonly Dictionary<string, IElement> _targets = new();
    private readonly Dictionary<string, object> _targetResources = new();

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
    public IElement? GetSource(string name) =>
        _sources.TryGetValue(name, out var value) ? value : null;

    public void SetSource(string name, IElement element) =>
        _sources[name] = element;

    /// <summary>
    /// Gets or sets a target element in the context.
    /// </summary>
    public IElement? GetTarget(string name) =>
        _targets.TryGetValue(name, out var value) ? value : null;

    public void SetTarget(string name, IElement element) =>
        _targets[name] = element;

    /// <summary>
    /// Gets or sets a target resource (e.g., ResourceJsonNode) for mutation.
    /// This is stored separately from the IElement target to enable mutation via IJsonNodeMutator.
    /// </summary>
    public T? GetTargetResource<T>(string name) where T : class =>
        _targetResources.TryGetValue(name, out var value) ? value as T : null;

    public void SetTargetResource(string name, object resource) =>
        _targetResources[name] = resource;

    /// <summary>
    /// FHIRPath evaluator for evaluating embedded FHIRPath expressions.
    /// </summary>
    public Func<string, IElement, IEnumerable<IElement>>? FhirPathEvaluator { get; set; }

    /// <summary>
    /// Transform function resolver.
    /// </summary>
    public Func<string, IEnumerable<object>, object>? TransformResolver { get; set; }

    /// <summary>
    /// Resource creator for creating new FHIR resources.
    /// </summary>
    public Func<string, IElement>? ResourceCreator { get; set; }

    /// <summary>
    /// ConceptMap resolver for terminology translation.
    /// </summary>
    public Func<string, string, string, string?>? ConceptMapResolver { get; set; }

    /// <summary>
    /// Logger for log statements in mapping execution.
    /// Receives the log message from the FHIRPath expression evaluation.
    /// </summary>
    public Action<string>? Logger { get; set; }

    /// <summary>
    /// Error mode for mapping execution.
    /// Strict mode throws exceptions on errors (default).
    /// Lenient mode collects errors and continues execution where possible.
    /// </summary>
    public ErrorMode ErrorMode { get; set; } = ErrorMode.Strict;

    private readonly List<ExecutionError> _errors = [];

    /// <summary>
    /// Gets the collection of execution errors (populated in Lenient mode).
    /// </summary>
    public IReadOnlyList<ExecutionError> Errors => _errors;

    /// <summary>
    /// Adds an error to the execution context.
    /// In Strict mode, this will throw an exception.
    /// In Lenient mode, this will collect the error and allow execution to continue.
    /// </summary>
    public void AddError(
        string message,
        string? location = null,
        string? code = null,
        Exception? exception = null,
        string? ruleName = null,
        string? elementPath = null,
        IReadOnlyList<string>? availableElements = null,
        string? groupName = null,
        int? ruleIndex = null)
    {
        var error = new ExecutionError(
            message,
            location,
            code,
            exception,
            ruleName,
            elementPath,
            availableElements,
            groupName,
            ruleIndex);

        // Prevent duplicate errors (same message, ruleName, elementPath, groupName, ruleIndex)
        var isDuplicate = _errors.Any(e =>
            e.Message == error.Message &&
            e.RuleName == error.RuleName &&
            e.ElementPath == error.ElementPath &&
            e.GroupName == error.GroupName &&
            e.RuleIndex == error.RuleIndex);

        if (!isDuplicate)
        {
            _errors.Add(error);
        }

        if (ErrorMode == ErrorMode.Strict && !isDuplicate)
        {
            throw new MappingExecutionException(message, location, code, exception);
        }
    }

    /// <summary>
    /// Adds an error directly to the execution context.
    /// In Strict mode, this will throw an exception.
    /// In Lenient mode, this will collect the error and allow execution to continue.
    /// </summary>
    public void AddError(ExecutionError error)
    {
        _errors.Add(error);

        if (ErrorMode == ErrorMode.Strict)
        {
            throw new MappingExecutionException(error.Message, error.Location, error.Code, error.Exception);
        }
    }

    /// <summary>
    /// Clears all collected errors.
    /// </summary>
    public void ClearErrors()
    {
        _errors.Clear();
    }
}
