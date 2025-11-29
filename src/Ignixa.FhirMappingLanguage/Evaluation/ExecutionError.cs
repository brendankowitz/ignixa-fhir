/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Represents an error that occurred during mapping execution.
 */

namespace Ignixa.FhirMappingLanguage.Evaluation;

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
