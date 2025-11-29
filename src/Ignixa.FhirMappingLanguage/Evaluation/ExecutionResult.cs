/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Execution result with error collection for graceful error recovery.
 */

namespace Ignixa.FhirMappingLanguage.Evaluation;

/// <summary>
/// Represents the result of a mapping execution with potential errors.
/// </summary>
/// <typeparam name="T">The result type</typeparam>
public class ExecutionResult<T>
{
    private readonly List<ExecutionError> _errors = new();

    public ExecutionResult(T? result)
    {
        Result = result;
    }

    /// <summary>
    /// Gets the execution result (may be null if execution failed).
    /// </summary>
    public T? Result { get; }

    /// <summary>
    /// Gets whether the execution was successful (no errors).
    /// </summary>
    public bool IsSuccess => _errors.Count == 0;

    /// <summary>
    /// Gets the collection of execution errors.
    /// </summary>
    public IReadOnlyList<ExecutionError> Errors => _errors;

    /// <summary>
    /// Adds an error to the result.
    /// </summary>
    public void AddError(string message, string? location = null, string? code = null, Exception? exception = null)
    {
        _errors.Add(new ExecutionError(message, location, code, exception));
    }

    /// <summary>
    /// Adds multiple errors to the result.
    /// </summary>
    public void AddErrors(IEnumerable<ExecutionError> errors)
    {
        _errors.AddRange(errors);
    }

    /// <summary>
    /// Returns a summary of the execution result.
    /// </summary>
#pragma warning disable CA1024 // Use properties where appropriate - This method generates a formatted string
    public string GetSummary()
#pragma warning restore CA1024
    {
        if (IsSuccess)
        {
            return "Execution completed successfully.";
        }

        return $"Execution completed with {_errors.Count} error(s).";
    }
}
