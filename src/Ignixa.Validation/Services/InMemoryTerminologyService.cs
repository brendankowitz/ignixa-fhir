// <copyright file="InMemoryTerminologyService.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Services;

/// <summary>
/// In-memory terminology service with hardcoded ValueSets for common FHIR code systems.
/// Returns warnings for unknown ValueSets to enable graceful degradation.
/// Intended for testing and prototype scenarios - production systems should use external terminology servers.
/// </summary>
public class InMemoryTerminologyService : ITerminologyService
{
    // Hardcoded ValueSets for common FHIR administrative codes
    private static readonly Dictionary<string, HashSet<string>> _valueSets = new(StringComparer.Ordinal)
    {
        // Administrative Gender (http://hl7.org/fhir/ValueSet/administrative-gender)
        ["http://hl7.org/fhir/ValueSet/administrative-gender"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "male", "female", "other", "unknown"
        },

        // Publication Status (http://hl7.org/fhir/ValueSet/publication-status)
        ["http://hl7.org/fhir/ValueSet/publication-status"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "draft", "active", "retired", "unknown"
        },

        // Observation Status (http://hl7.org/fhir/ValueSet/observation-status)
        ["http://hl7.org/fhir/ValueSet/observation-status"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "registered", "preliminary", "final", "amended", "corrected",
            "cancelled", "entered-in-error", "unknown"
        },

        // Contact Point System (http://hl7.org/fhir/ValueSet/contact-point-system)
        ["http://hl7.org/fhir/ValueSet/contact-point-system"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "phone", "fax", "email", "pager", "url", "sms", "other"
        },

        // Contact Point Use (http://hl7.org/fhir/ValueSet/contact-point-use)
        ["http://hl7.org/fhir/ValueSet/contact-point-use"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "home", "work", "temp", "old", "mobile"
        },

        // Address Use (http://hl7.org/fhir/ValueSet/address-use)
        ["http://hl7.org/fhir/ValueSet/address-use"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "home", "work", "temp", "old", "billing"
        },

        // Address Type (http://hl7.org/fhir/ValueSet/address-type)
        ["http://hl7.org/fhir/ValueSet/address-type"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "postal", "physical", "both"
        },

        // Name Use (http://hl7.org/fhir/ValueSet/name-use)
        ["http://hl7.org/fhir/ValueSet/name-use"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "usual", "official", "temp", "nickname", "anonymous", "old", "maiden"
        },

        // Identifier Use (http://hl7.org/fhir/ValueSet/identifier-use)
        ["http://hl7.org/fhir/ValueSet/identifier-use"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "usual", "official", "temp", "secondary", "old"
        },

        // Quantity Comparator (http://hl7.org/fhir/ValueSet/quantity-comparator)
        ["http://hl7.org/fhir/ValueSet/quantity-comparator"] = new HashSet<string>(StringComparer.Ordinal)
        {
            "<", "<=", ">=", ">"
        }
    };

    /// <summary>
    /// Validates a code against a ValueSet binding.
    /// Returns WARNING for unknown ValueSets (graceful degradation).
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

        // Check if we have this ValueSet in memory
        if (!_valueSets.TryGetValue(valueSetUrl, out var validCodes))
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
}
