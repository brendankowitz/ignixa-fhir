// <copyright file="InMemoryTerminologyService.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Services;

/// <summary>
/// In-memory terminology service with version-specific ValueSets.
/// Returns warnings for unknown ValueSets to enable graceful degradation.
/// Intended for testing and prototype scenarios - production systems should use external terminology servers.
/// </summary>
public partial class InMemoryTerminologyService : ITerminologyService
{
    private readonly Dictionary<string, HashSet<string>> _valueSets = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the InMemoryTerminologyService for the specified FHIR version.
    /// </summary>
    /// <param name="fhirVersion">The FHIR version to load ValueSets for.</param>
    public InMemoryTerminologyService(FhirVersion fhirVersion)
    {
        switch (fhirVersion)
        {
            case FhirVersion.R4:
                AddFhirR4ValueSets(_valueSets);
                break;
            case FhirVersion.R4B:
                AddFhirR4BValueSets(_valueSets);
                break;
            case FhirVersion.R5:
                AddFhirR5ValueSets(_valueSets);
                break;
            case FhirVersion.R6:
                AddFhirR6ValueSets(_valueSets);
                break;
            case FhirVersion.Stu3:
                AddFhirSTU3ValueSets(_valueSets);
                break;
            default:
                throw new ArgumentException($"Unsupported FHIR version: {fhirVersion}", nameof(fhirVersion));
        }
    }
    /// Returns ERROR for known ValueSets with invalid codes.
    /// </summary>
    /// <param name="system">The code system URL.</param>
    /// <param name="code">The code to validate.</param>
    /// <param name="display">The display text (not validated in this implementation).</param>
    /// <param name="valueSetUrl">The ValueSet canonical URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with severity and message.</returns>
    public Task<TerminologyValidationResult> ValidateCodeAsync(
        string? system,
        string? code,
        string? display,
        string? valueSetUrl,
        CancellationToken cancellationToken)
    {
        // Handle null/empty inputs
        if (string.IsNullOrWhiteSpace(code))
        {
            return Task.FromResult(new TerminologyValidationResult(
                IsValid: false,
                Severity: IssueSeverity.Error,
                Message: "Code is required for terminology validation"));
        }

        if (string.IsNullOrWhiteSpace(valueSetUrl))
        {
            return Task.FromResult(new TerminologyValidationResult(
                IsValid: true,
                Severity: IssueSeverity.Warning,
                Message: "No ValueSet URL provided - skipping terminology validation"));
        }

        // Normalize the URL by removing version specifier (e.g., "url|4.0.1" -> "url")
        var normalizedUrl = valueSetUrl.Contains('|', StringComparison.Ordinal)
            ? valueSetUrl[..valueSetUrl.LastIndexOf('|')]
            : valueSetUrl;

        // Check if we have this ValueSet in memory
        if (!_valueSets.TryGetValue(normalizedUrl, out var validCodes))
        {
            // Unknown ValueSet - return WARNING (graceful degradation)
            return Task.FromResult(new TerminologyValidationResult(
                IsValid: true,
                Severity: IssueSeverity.Warning,
                Message: $"Terminology validation unavailable for ValueSet '{valueSetUrl}' - in-memory provider does not contain this ValueSet"));
        }

        // Check if the code is in the ValueSet
        if (!validCodes.Contains(code))
        {
            // Invalid code - return ERROR
            var message = system != null
                ? $"The provided code '{system}#{code}' was not found in the value set '{valueSetUrl}'"
                : $"The provided code '{code}' was not found in the value set '{valueSetUrl}'";

            return Task.FromResult(new TerminologyValidationResult(
                IsValid: false,
                Severity: IssueSeverity.Error,
                Message: message));
        }

        // Valid code
        return Task.FromResult(new TerminologyValidationResult(
            IsValid: true,
            Severity: IssueSeverity.Information,
            Message: null));
    }

    /// <summary>
    /// $lookup operation is not supported by the in-memory implementation.
    /// Returns not found for all lookups.
    /// </summary>
    public Task<LookupResult> LookupCodeAsync(
        string system,
        string code,
        string? version,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new LookupResult(
            Found: false,
            Name: null,
            Version: null,
            Display: null,
            Definition: null,
            Properties: null,
            Designations: null));
    }

    /// <summary>
    /// $expand operation is not supported by the in-memory implementation.
    /// Always returns null (expansion not available).
    /// </summary>
    public Task<ExpandResult?> ExpandValueSetAsync(
        ExpansionParameters parameters,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<ExpandResult?>(null);
    }

    /// <summary>
    /// Validates a coded element against a terminology binding.
    /// In-memory implementation uses hardcoded ValueSets for common bindings.
    /// </summary>
    public async Task<BindingValidationResult> ValidateBindingAsync(
        string valueSetUrl,
        BindingStrength strength,
        string? system,
        string? code,
        string? display,
        string? version,
        CancellationToken cancellationToken)
    {
        // Use existing ValidateCodeAsync for code validation
        var codeValidation = await ValidateCodeAsync(system, code, display, valueSetUrl, cancellationToken);

        // Determine severity based on binding strength and code validation result
        var (isValid, severity, message) = DetermineSeverity(strength, codeValidation);

        // For display validation, we don't have CodeSystem lookups in memory, so return null
        return new BindingValidationResult(
            IsValid: isValid,
            Strength: strength,
            Severity: severity,
            Message: message,
            SuggestedDisplay: null); // In-memory service doesn't have CodeSystem definitions
    }

    /// <summary>
    /// $translate operation is not supported by the in-memory implementation.
    /// Returns no matches.
    /// </summary>
    public Task<TranslateResult> TranslateCodeAsync(
        TranslateParameters parameters,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new TranslateResult(
            Result: false,
            Message: "ConceptMap translation not supported in in-memory terminology service",
            Matches: Array.Empty<TranslateMatch>()));
    }

    /// <summary>
    /// $subsumes operation is not supported by the in-memory implementation.
    /// Returns not-subsumed.
    /// </summary>
    public Task<SubsumesResult> SubsumesAsync(
        SubsumesParameters parameters,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new SubsumesResult("not-subsumed"));
    }

    /// <summary>
    /// Determines validation result severity based on binding strength and code validation outcome.
    /// </summary>
    private static (bool IsValid, IssueSeverity Severity, string? Message) DetermineSeverity(
        BindingStrength strength,
        TerminologyValidationResult codeValidation)
    {
        return strength switch
        {
            BindingStrength.Required => codeValidation.IsValid
                ? (true, codeValidation.Severity, codeValidation.Message)  // Preserve warnings from unknown ValueSets
                : (false, IssueSeverity.Error, codeValidation.Message),

            BindingStrength.Extensible => codeValidation.IsValid
                ? (true, codeValidation.Severity, codeValidation.Message)  // Preserve warnings
                : (true, IssueSeverity.Warning, codeValidation.Message), // Warning, not error for extensible

            BindingStrength.Preferred => (true, codeValidation.Severity, codeValidation.Message),  // Preserve warnings

            BindingStrength.Example => (true, codeValidation.Severity, codeValidation.Message),  // Preserve warnings

            _ => (true, IssueSeverity.Warning, "Unknown binding strength")
        };
    }
}
