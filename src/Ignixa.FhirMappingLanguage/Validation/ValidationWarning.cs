/* Copyright (c) 2025, Ignixa Contributors */

namespace Ignixa.FhirMappingLanguage.Validation;

/// <summary>
/// Represents a validation warning.
/// </summary>
public class ValidationWarning
{
    public ValidationWarning(string message, string? location = null, string? code = null)
    {
        Message = message;
        Location = location;
        Code = code;
    }

    /// <summary>
    /// Gets the warning message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the location where the warning occurred.
    /// </summary>
    public string? Location { get; }

    /// <summary>
    /// Gets the warning code.
    /// </summary>
    public string? Code { get; }

    public override string ToString() =>
        Location != null
            ? $"{Location}: {Message}" + (Code != null ? $" [{Code}]" : "")
            : Message + (Code != null ? $" [{Code}]" : "");
}
