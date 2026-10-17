// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Drafting;

/// <summary>
/// Checks that every resource a draft refers to is in the folder, by the catalog, and that each one other than the subject (the
/// encounter, a reason) is the subject patient's: its <c>subject</c>, read as the search index resolves Bundle references, must be
/// that patient. A draft for a patient the folder does not hold, or that points at another patient's encounter, fails.
/// </summary>
internal static class DraftReferences
{
    public const string Found = "found";
    public const string NotFound = "not-found";
    public const string OtherPatient = "other-patient";
    public const string SubjectUnknown = "subject-unknown";

    /// <summary>One check per reference, the issues for those that do not pass, and the resources found, as evidence.</summary>
    public static (List<DraftCheck> Checks, List<IssueItem> Issues, List<EvidenceItem> Evidence) Check(FolderSession session, IReadOnlyList<DraftReference> references)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(references);

        string patient = references.Single(reference => reference.IsSubject).Reference;
        List<DraftCheck> checks = [];
        List<IssueItem> issues = [];
        List<EvidenceItem> evidence = [];
        foreach (DraftReference reference in references)
        {
            CatalogEntry? entry = session.FindEntry(reference.ResourceType, reference.Id);
            string result = entry is null ? NotFound : reference.IsSubject ? Found : SubjectOf(session, entry) switch
            {
                null => SubjectUnknown,
                var subject when subject == patient => Found,
                _ => OtherPatient,
            };

            if (entry is not null)
            {
                evidence.Add(new EvidenceItem(FhirIds.Reference(entry), entry.FilePath, entry.Line, entry.Offset));
            }

            if (Issue(reference, result, patient) is { } issue)
            {
                issues.Add(issue);
            }

            string outcome = result switch
            {
                Found => DraftCheck.Pass,
                SubjectUnknown => DraftCheck.Unknown,
                _ => DraftCheck.Fail,
            };
            checks.Add(new DraftCheck("reference", reference.Element, outcome, new ReferenceCheckDetails(reference.Reference, result)));
        }

        return (checks, issues, evidence);
    }

    // The ids were checked to be FHIR ids when the input was read, so the references can be named.
    private static IssueItem? Issue(DraftReference reference, string result, string patient) => result switch
    {
        NotFound when reference.IsSubject => new IssueItem("error", "patient-not-found", $"{reference.Reference} is not in the folder, so a draft for it would point at nobody; find the patient with the patients verb and give its id.", Location: null),
        NotFound when reference.ResourceType == "Encounter" => new IssueItem("error", "encounter-not-found", $"{reference.Reference} ({reference.Element}) is not in the folder; give one of the patient's encounters, or leave encounter out.", Location: null),
        NotFound => new IssueItem("error", "reference-not-found", $"{reference.Reference} ({reference.Element}) is not in the folder.", Location: null),
        OtherPatient => new IssueItem("error", "reference-other-patient", $"{reference.Reference} ({reference.Element}) is not {patient}'s: its subject is another resource.", Location: null),
        SubjectUnknown => new IssueItem("warning", "reference-subject-unknown", $"{reference.Reference} ({reference.Element}) names no subject the tool can read, so whether it is {patient}'s was not checked.", Location: null),
        _ => null,
    };

    // The resource's subject as Type/id, its Bundle references resolved as the index resolves them; null when it has none or cannot be read.
    private static string? SubjectOf(FolderSession session, CatalogEntry entry)
    {
        (byte[]? bytes, IssueItem? _) = ResourceReader.ReadBytes(session.RootPath, entry);
        if (bytes is null)
        {
            return null;
        }

        JsonObject? resource;
        try
        {
            resource = JsonNode.Parse(bytes) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        if (resource is null)
        {
            return null;
        }

        if (session.Catalog.FullUrls.Count > 0)
        {
            session.BundleReferences.Rewrite(entry, resource);
        }

        if (resource["subject"] is not JsonObject subject || subject["reference"] is not JsonValue value || !value.TryGetValue(out string? reference))
        {
            return null;
        }

        if (RestfulUrl.TryParse(reference, out _, out string type, out string id))
        {
            return $"{type}/{id}";
        }

        // A relative reference, perhaps to a version: Type/id or Type/id/_history/vid.
        string[] parts = reference.Split('/');
        return parts.Length == 2 || (parts.Length == 4 && parts[2] == "_history") ? $"{parts[0]}/{parts[1]}" : reference;
    }
}
