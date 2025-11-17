// <copyright file="ITerminologyService.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Ignixa.Domain.Terminology;

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

    /// <summary>
    /// $lookup operation - Look up concept details by system and code.
    /// Returns concept display, definition, properties, and designations.
    /// </summary>
    /// <param name="system">The code system URL (e.g., "http://loinc.org").</param>
    /// <param name="code">The code to look up (e.g., "8310-5").</param>
    /// <param name="version">The code system version (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Lookup result with concept details or not found indication.</returns>
    Task<LookupResult> LookupCodeAsync(
        string system,
        string code,
        string? version,
        CancellationToken cancellationToken);

    /// <summary>
    /// $expand operation - Expand a ValueSet to a list of codes.
    /// Returns pre-computed expansion from TermValueSetExpansion table if available.
    /// </summary>
    /// <param name="parameters">Expansion parameters (URL, filter, count, offset).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Expansion result with list of codes and pagination metadata.</returns>
    Task<ExpandResult?> ExpandValueSetAsync(
        ExpansionParameters parameters,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates a coded element against a terminology binding.
    /// Returns severity based on binding strength: Required→ERROR, Extensible→WARNING (mode=full), Preferred→INFO.
    /// </summary>
    /// <param name="valueSetUrl">Canonical URL of the bound ValueSet.</param>
    /// <param name="strength">Binding strength (Required, Extensible, Preferred, Example).</param>
    /// <param name="system">Code system URL.</param>
    /// <param name="code">The code to validate.</param>
    /// <param name="display">The display text (optional, validated in mode=full only).</param>
    /// <param name="version">Code system version (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Binding validation result with severity based on binding strength and validation mode.</returns>
    Task<BindingValidationResult> ValidateBindingAsync(
        string valueSetUrl,
        BindingStrength strength,
        string? system,
        string? code,
        string? display,
        string? version,
        CancellationToken cancellationToken);

    /// <summary>
    /// $translate operation - Translate a code from one code system to another using a ConceptMap.
    /// Returns list of matching translations with equivalence information.
    /// </summary>
    /// <param name="parameters">Translation parameters (code, system, target, reverse, etc.).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Translation result with list of matches or empty if no translation found.</returns>
    Task<TranslateResult> TranslateCodeAsync(
        TranslateParameters parameters,
        CancellationToken cancellationToken);

    /// <summary>
    /// $subsumes operation - Test hierarchical subsumption relationship between two codes.
    /// Returns whether codeA subsumes codeB, is subsumed by codeB, is equivalent, or has no relationship.
    /// </summary>
    /// <param name="parameters">Subsumption parameters (codeA, codeB, system, version).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Subsumption result indicating relationship (equivalent, subsumes, subsumed-by, not-subsumed).</returns>
    Task<SubsumesResult> SubsumesAsync(
        SubsumesParameters parameters,
        CancellationToken cancellationToken);

    /// <summary>
    /// Check import status of a canonical resource (ValueSet, CodeSystem, ConceptMap).
    /// Used by HybridTerminologyService to route to SQL vs JSON fallback.
    /// </summary>
    /// <param name="canonical">Canonical URL of the resource.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Import status or null if resource not found.</returns>
    Task<TerminologyImportStatus?> GetImportStatusAsync(
        string canonical,
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
