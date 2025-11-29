/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Validation;

/// <summary>
/// Represents a validation error.
/// </summary>
public class ValidationError
{
    public ValidationError(string message, string? location = null, string? code = null)
    {
        Message = message;
        Location = location;
        Code = code;
    }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the location where the error occurred (e.g., "Group: Transform, Rule: 1").
    /// </summary>
    public string? Location { get; }

    /// <summary>
    /// Gets the error code (e.g., "TYPE_MISMATCH", "MISSING_SOURCE").
    /// </summary>
    public string? Code { get; }

    public override string ToString() =>
        Location != null
            ? $"{Location}: {Message}" + (Code != null ? $" [{Code}]" : "")
            : Message + (Code != null ? $" [{Code}]" : "");
}
