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
    /// Graceful mode collects errors and continues execution where possible.
    /// </summary>
    public ErrorMode ErrorMode { get; set; } = ErrorMode.Strict;

    private readonly List<ExecutionError> _errors = new();

    /// <summary>
    /// Gets the collection of execution errors (populated in Graceful mode).
    /// </summary>
    public IReadOnlyList<ExecutionError> Errors => _errors;

    /// <summary>
    /// Adds an error to the execution context.
    /// In Strict mode, this will throw an exception.
    /// In Graceful mode, this will collect the error and allow execution to continue.
    /// </summary>
    public void AddError(string message, string? location = null, string? code = null, Exception? exception = null)
    {
        var error = new ExecutionError(message, location, code, exception);
        _errors.Add(error);

        if (ErrorMode == ErrorMode.Strict)
        {
            throw new MappingExecutionException(message, location, code, exception);
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

/// <summary>
/// Exception thrown during mapping execution when in Strict error mode.
/// </summary>
public class MappingExecutionException : Exception
{
    public MappingExecutionException(string message, string? location = null, string? code = null, Exception? innerException = null)
        : base(FormatMessage(message, location, code), innerException)
    {
        Location = location;
        Code = code;
    }

    /// <summary>
    /// Gets the location where the error occurred.
    /// </summary>
    public string? Location { get; }

    /// <summary>
    /// Gets the error code.
    /// </summary>
    public string? Code { get; }

    private static string FormatMessage(string message, string? location, string? code)
    {
        var parts = new List<string>();
        if (location != null) parts.Add($"Location: {location}");
        if (code != null) parts.Add($"Code: {code}");
        parts.Add(message);
        return string.Join(" - ", parts);
    }
}
