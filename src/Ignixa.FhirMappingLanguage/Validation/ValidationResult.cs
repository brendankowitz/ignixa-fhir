/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Validation result for FHIR Mapping Language validation mode.
 */

namespace Ignixa.FhirMappingLanguage.Validation;

/// <summary>
/// Represents the result of validating a FHIR mapping.
/// </summary>
public class ValidationResult
{
    private readonly List<ValidationError> _errors = [];
    private readonly List<ValidationWarning> _warnings = [];

    /// <summary>
    /// Gets whether the validation passed (no errors).
    /// </summary>
    public bool IsValid => _errors.Count == 0;

    /// <summary>
    /// Gets the validation errors.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors => _errors;

    /// <summary>
    /// Gets the validation warnings.
    /// </summary>
    public IReadOnlyList<ValidationWarning> Warnings => _warnings;

    /// <summary>
    /// Adds an error to the validation result.
    /// </summary>
    public void AddError(string message, string? location = null, string? code = null)
    {
        _errors.Add(new ValidationError(message, location, code));
    }

    /// <summary>
    /// Adds a warning to the validation result.
    /// </summary>
    public void AddWarning(string message, string? location = null, string? code = null)
    {
        _warnings.Add(new ValidationWarning(message, location, code));
    }

    /// <summary>
    /// Merges another validation result into this one.
    /// </summary>
    public void Merge(ValidationResult other)
    {
        _errors.AddRange(other.Errors);
        _warnings.AddRange(other.Warnings);
    }

    /// <summary>
    /// Returns a summary of the validation result.
    /// </summary>
#pragma warning disable CA1024 // Use properties where appropriate - This method generates a formatted string
    public string GetSummary()
#pragma warning restore CA1024
    {
        if (IsValid && _warnings.Count == 0)
        {
            return "Validation passed with no errors or warnings.";
        }

        if (IsValid)
        {
            return $"Validation passed with {_warnings.Count} warning(s).";
        }

        return $"Validation failed with {_errors.Count} error(s) and {_warnings.Count} warning(s).";
    }
}
