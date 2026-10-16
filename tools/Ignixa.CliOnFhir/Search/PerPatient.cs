// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Session;
using Ignixa.Search.InMemory;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// The <c>perPatient</c> argument: of a search's matches, keep only each patient's first in result order, so a cohort question
/// ("each patient's latest LDL, then a threshold") is one search with <c>fhirpath</c> or <c>summary=count</c> instead of pages
/// of rows grouped by the caller. FHIR search has no group-by and FHIRPath none either, so this is a tool argument rather than
/// part of <c>query</c>, which stays valid FHIR.
/// </summary>
/// <remarks>
/// A match belongs to the patients whose Patient compartment it is in, by the FHIR version's CompartmentDefinition (an R4
/// Observation through <c>subject</c> or <c>performer</c>, a MedicationRequest through <c>subject</c>), the membership the
/// <c>compartment</c> argument uses; a Patient belongs to itself. A match in two patients' compartments is kept once, when it
/// is the first of either.
/// </remarks>
internal static class PerPatient
{
    public const string Latest = "latest";

    public const string Earliest = "earliest";

    public const string First = "first";

    private const string CompartmentType = "Patient";

    /// <summary>
    /// The <c>_sort</c> value <paramref name="pick"/> orders by: the type's date parameter, descending for <see cref="Latest"/>
    /// and ascending for <see cref="Earliest"/>; null for <see cref="First"/>, which keeps the query's own <c>_sort</c>.
    /// </summary>
    /// <exception cref="ToolRequestException">
    /// An unknown pick; <see cref="Latest"/> or <see cref="Earliest"/> beside a <c>_sort</c> of the query's own, or for a type
    /// with no date parameter to order by; <see cref="First"/> without a <c>_sort</c>, whose order would be the catalog's.
    /// </exception>
    public static string? SortFor(string pick, string resourceType, ParsedSearch parsed)
    {
        bool sorted = parsed.Options.Sort.Count > 0;
        switch (pick)
        {
            case First when !sorted:
                throw Invalid("perPatient=first keeps each patient's first match in _sort order, and the query has no _sort; add one (_sort=-value-quantity for the highest value), or use perPatient=latest or earliest to order by date.");
            case First:
                return null;
            case Latest or Earliest when sorted:
                throw Invalid($"perPatient={pick} orders the matches by date itself, so leave _sort out of the query; to keep each patient's first match in an order of your own, use perPatient=first with that _sort.");
            case Latest or Earliest when SearchNarrowing.DateParameters.TryGetValue(resourceType, out string? date):
                return pick == Latest ? "-" + date : date;
            case Latest or Earliest:
                throw Invalid($"perPatient={pick} needs a date to order {resourceType} by and has none for it; give _sort in the query (_sort=-<date parameter>) and perPatient=first.");
            default:
                throw Invalid($"'perPatient' takes '{Latest}', '{Earliest}' or '{First}', not '{pick}'.");
        }
    }

    /// <summary>The query with <c>_sort=<paramref name="sort"/></c> added.</summary>
    public static string WithSort(string? query, string sort) =>
        string.IsNullOrEmpty(query) ? $"_sort={sort}" : $"{query}&_sort={sort}";

    /// <summary>
    /// Each patient's first of <paramref name="matches"/>, in their order, and what the selection kept and dropped.
    /// </summary>
    /// <exception cref="ToolRequestException">The searched type is never in a Patient compartment, or its membership cannot be evaluated.</exception>
    public static (List<IndexedResource> Kept, PerPatientFacts Facts) Select(FolderSession session, string resourceType, List<IndexedResource> matches, string pick, string? sort)
    {
        InMemoryCompartmentMembership membership = session.Compartments;
        if (resourceType != CompartmentType && !membership.ResourceTypes(CompartmentType).Contains(resourceType, StringComparer.Ordinal))
        {
            throw Invalid($"{resourceType} is never in a Patient compartment, so its matches belong to no patient and perPatient cannot apply; leave perPatient out.");
        }

        if (membership.UnevaluableParameters(CompartmentType, resourceType) is { Count: > 0 } unevaluable)
        {
            throw SearchFailures.NotSupported($"perPatient cannot tell which patient a {resourceType} belongs to: its Patient compartment parameters {string.Join(", ", unevaluable)} are not search parameters of this FHIR version.");
        }

        HashSet<string> patients = new(StringComparer.Ordinal);
        List<IndexedResource> kept = [];
        int withoutPatient = 0;
        foreach (IndexedResource match in matches)
        {
            List<string> owners = [.. Owners(membership, match)];
            if (owners.Count == 0)
            {
                withoutPatient++;
                continue;
            }

            bool firstOfAny = false;
            foreach (string owner in owners)
            {
                firstOfAny |= patients.Add(owner);
            }

            if (firstOfAny)
            {
                kept.Add(match);
            }
        }

        return (kept, new PerPatientFacts(pick, sort, matches.Count, patients.Count, kept.Count, withoutPatient));
    }

    /// <summary>The bytes <paramref name="facts"/> add to a response's <c>facts</c>, so the rest can be fitted to what is left.</summary>
    public static int SizeOf(PerPatientFacts facts) => EnvelopeJson.SizeOf(facts) + ",\"perPatient\":".Length;

    /// <summary>The ids of the patients whose Patient compartment <paramref name="match"/> is in; a Patient's own id for a Patient.</summary>
    internal static IEnumerable<string> Owners(InMemoryCompartmentMembership membership, IndexedResource match) =>
        match.Key.ResourceType == CompartmentType
            ? [match.Key.Id]
            : FolderSearchSource.Candidates(match, CompartmentType).Where(id => membership.IsInCompartment(match.Key, match.Index, CompartmentType, id));

    private static ToolRequestException Invalid(string message) => ToolRequestException.Error("invalid-parameter", message);
}
