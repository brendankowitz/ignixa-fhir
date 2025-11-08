/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Runtime error collection for graceful error recovery in FHIR Mapping Language.
 */

namespace Ignixa.FhirMappingLanguage.Evaluation;

/// <summary>
/// Error mode for mapping execution.
/// </summary>
public enum ErrorMode
{
    /// <summary>
    /// Throw exceptions on errors (default behavior).
    /// </summary>
    Strict,

    /// <summary>
    /// Collect errors and continue execution where possible.
    /// </summary>
    Graceful
}

/// <summary>
/// Represents an error that occurred during mapping execution.
/// </summary>
public class ExecutionError
{
    public ExecutionError(
        string message,
        string? location = null,
        string? code = null,
        Exception? exception = null)
    {
        Message = message;
        Location = location;
        Code = code;
        Exception = exception;
    }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the location where the error occurred (e.g., "Group: Transform, Rule: MapName").
    /// </summary>
    public string? Location { get; }

    /// <summary>
    /// Gets the error code (e.g., "TRANSFORM_FAILED", "FHIRPATH_ERROR").
    /// </summary>
    public string? Code { get; }

    /// <summary>
    /// Gets the underlying exception if available.
    /// </summary>
    public Exception? Exception { get; }

    public override string ToString() =>
        Location != null
            ? $"{Location}: {Message}" + (Code != null ? $" [{Code}]" : "")
            : Message + (Code != null ? $" [{Code}]" : "");
}

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
