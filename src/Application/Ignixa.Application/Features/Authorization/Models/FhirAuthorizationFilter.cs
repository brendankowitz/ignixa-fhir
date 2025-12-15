// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.Features.Authorization.Models;

/// <summary>
/// Data filtering rules applied by authorization to restrict query results.
/// Used to implement compartment filtering for SMART on FHIR v2 scopes.
/// </summary>
public record FhirAuthorizationFilter
{
    /// <summary>
    /// Patient ID for patient compartment filtering.
    /// When set, search results are filtered to only include resources in this patient's compartment.
    /// Used for patient/*.rs scopes.
    /// </summary>
    public string? PatientFilter { get; init; }

    /// <summary>
    /// Practitioner ID for practitioner compartment filtering.
    /// When set, search results are filtered to only include resources in this practitioner's compartment.
    /// Used for practitioner/*.rs scopes (SMART v2).
    /// </summary>
    public string? PractitionerFilter { get; init; }

    /// <summary>
    /// Custom search parameter filters to apply to all queries.
    /// Key: search parameter name, Value: search parameter value.
    /// Example: { "patient": "Patient/123" } automatically adds patient=Patient/123 to all searches.
    /// Also used for SMART v2 search constraints (e.g., category=laboratory).
    /// </summary>
    public Dictionary<string, string>? SearchFilters { get; init; }

    /// <summary>
    /// Encounter ID for encounter compartment filtering.
    /// When set, search results are filtered to resources associated with this encounter.
    /// </summary>
    public string? EncounterFilter { get; init; }

    /// <summary>
    /// Creates an empty filter (no restrictions).
    /// </summary>
    public static FhirAuthorizationFilter None => new();

    /// <summary>
    /// Creates a patient compartment filter.
    /// PatientFilter is used for compartment-based filtering logic.
    /// SearchFilters should only be added if there are explicit SMART v2 search constraints.
    /// </summary>
    /// <param name="patientId">The patient ID to filter by.</param>
    /// <returns>A filter configured for patient compartment.</returns>
    public static FhirAuthorizationFilter ForPatient(string patientId)
    {
        return new FhirAuthorizationFilter
        {
            PatientFilter = patientId
        };
    }

    /// <summary>
    /// Creates a practitioner compartment filter (SMART v2).
    /// PractitionerFilter is used for compartment-based filtering logic.
    /// SearchFilters should only be added if there are explicit SMART v2 search constraints.
    /// </summary>
    /// <param name="practitionerId">The practitioner reference (e.g., "Practitioner/123").</param>
    /// <returns>A filter configured for practitioner compartment.</returns>
    public static FhirAuthorizationFilter ForPractitioner(string practitionerId)
    {
        return new FhirAuthorizationFilter
        {
            PractitionerFilter = practitionerId
        };
    }

    /// <summary>
    /// Merges this filter with another filter.
    /// Used when multiple authorization handlers contribute filters.
    /// </summary>
    /// <param name="other">The other filter to merge with.</param>
    /// <returns>A new filter containing restrictions from both filters.</returns>
    public FhirAuthorizationFilter Merge(FhirAuthorizationFilter? other)
    {
        if (other == null)
        {
            return this;
        }

        var mergedSearchFilters = new Dictionary<string, string>();

        if (SearchFilters != null)
        {
            foreach (var kvp in SearchFilters)
            {
                mergedSearchFilters[kvp.Key] = kvp.Value;
            }
        }

        if (other.SearchFilters != null)
        {
            foreach (var kvp in other.SearchFilters)
            {
                mergedSearchFilters[kvp.Key] = kvp.Value;
            }
        }

        return new FhirAuthorizationFilter
        {
            PatientFilter = other.PatientFilter ?? PatientFilter,
            PractitionerFilter = other.PractitionerFilter ?? PractitionerFilter,
            EncounterFilter = other.EncounterFilter ?? EncounterFilter,
            SearchFilters = mergedSearchFilters.Count > 0 ? mergedSearchFilters : null
        };
    }
}
