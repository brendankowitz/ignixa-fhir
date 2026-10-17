// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// Names one Patient from what a caller knows of it: its id (or the digest <c>patient_list</c> gives), an identifier value
/// such as an MRN, or an identifier token <c>system|value</c>. Like the <c>patient_list</c> lookup (<see cref="PatientMatch"/>)
/// it reads only the Patient resources, never a search index. It never guesses: an identifier that names several patients
/// fails with their ids, and one that names none fails with the ids of the patients whose fields contain it.
/// </summary>
internal static class PatientLookup
{
    /// <summary>Most candidates an ambiguous or unknown identifier fails with.</summary>
    public const int MaxCandidates = 5;

    /// <summary>The id of the one Patient <paramref name="patient"/> names.</summary>
    /// <exception cref="ToolRequestException">
    /// <c>invalid-parameter</c> for a blank or overlong value, <c>ambiguous-patient</c> when it names several patients and
    /// <c>unknown-patient</c> when it names none.
    /// </exception>
    public static string Resolve(FolderSession session, string patient)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(patient);

        string wanted = patient.Trim();
        if (wanted.Length == 0 || wanted.Length > PatientLister.MaxQueryLength || wanted.Any(char.IsWhiteSpace))
        {
            throw ToolRequestException.Error("invalid-parameter", $"'patient' must be a Patient id, an identifier value (an MRN) or 'system|value', of 1 to {PatientLister.MaxQueryLength} characters without spaces.");
        }

        bool isToken = wanted.Contains('|', StringComparison.Ordinal);
        // Ids are unique in the folder, so an id the folder holds is the answer without reading any Patient.
        if (!isToken && (FhirIds.IsValid(wanted) || FhirIds.IsDigest(wanted)) && session.FindByName("Patient", wanted) is { } byId)
        {
            return byId.Id;
        }

        var elements = new ResourceElements(session, resolveReferences: false);
        List<(CatalogEntry Entry, IElement Element)> everyone = [.. session.Catalog.Entries
            .Where(entry => entry.ResourceType == "Patient")
            .Select(entry => (Entry: entry, Element: elements.Get(entry)))
            .Where(patient => patient.Element is not null)
            .Select(patient => (patient.Entry, patient.Element!))];

        PatientMatch match = PatientMatch.Parse(wanted);
        List<(CatalogEntry Entry, IElement Element)> named = isToken
            ? [.. everyone.Where(candidate => match.Matches(candidate.Entry.Id, name: null, candidate.Element))]
            : [.. everyone.Where(candidate => HasIdentifierValue(candidate.Element, wanted))];

        return named.Count switch
        {
            1 => named[0].Entry.Id,
            0 => throw ToolRequestException.Error("unknown-patient", NotFound(wanted, everyone, match)),
            _ => throw ToolRequestException.Error(
                "ambiguous-patient",
                $"'{wanted}' names {named.Count} patients; pass one of their ids: {Candidates(named)}{(named.Count > MaxCandidates ? ", ..." : string.Empty)}."),
        };
    }

    private static bool HasIdentifierValue(IElement patient, string value) =>
        ClinicalValues.Strings(patient, "identifier.value").Contains(value, StringComparer.Ordinal);

    private static string NotFound(string wanted, List<(CatalogEntry Entry, IElement Element)> everyone, PatientMatch match)
    {
        List<(CatalogEntry Entry, IElement Element)> near = [.. everyone.Where(candidate => match.Matches(candidate.Entry.Id, ClinicalValues.PatientName(candidate.Element), candidate.Element))];
        string nearText = near.Count == 0 ? string.Empty : $" Patients whose id, name or identifiers contain it: {Candidates(near)}.";
        return $"No Patient has the id or an identifier '{wanted}' ({everyone.Count} Patient resources; an identifier value must match exactly).{nearText}";
    }

    // Ids only: names and identifier values are folder data, which an issue never quotes.
    private static string Candidates(List<(CatalogEntry Entry, IElement Element)> patients) =>
        string.Join(", ", patients.Take(MaxCandidates).Select(candidate => FhirIds.Safe(candidate.Entry.Id)));
}
