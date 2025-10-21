// <copyright file="ITerminologyService.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Service for validating codes and codings against FHIR ValueSets.
/// Implementations may use in-memory ValueSets, external terminology servers, or other sources.
/// </summary>
public interface ITerminologyService
{
    /// <summary>
    /// Validates a code against a ValueSet binding.
    /// </summary>
    /// <param name="system">The code system URL (e.g., "http://hl7.org/fhir/administrative-gender").</param>
    /// <param name="code">The code to validate (e.g., "male").</param>
    /// <param name="display">The display text for the code (optional, may be null).</param>
    /// <param name="valueSetUrl">The canonical URL of the ValueSet to validate against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating whether the code is valid and any issues found.</returns>
    Task<TerminologyValidationResult> ValidateCodeAsync(
        string? system,
        string? code,
        string? display,
        string? valueSetUrl,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of a terminology validation operation.
/// </summary>
/// <param name="IsValid">True if the code is valid or validation is unavailable (warnings don't fail validation).</param>
/// <param name="Severity">Severity of any issues found (Warning for unavailable, Error for invalid).</param>
/// <param name="Message">Human-readable message describing the result.</param>
public record TerminologyValidationResult(
    bool IsValid,
    IssueSeverity Severity,
    string? Message);
