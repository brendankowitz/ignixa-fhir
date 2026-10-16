// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// One patient's resources: the Patient itself and every resource in its Patient compartment, by the session's
/// CompartmentDefinition, as the session's <see cref="FolderSession.PatientCompartments"/> holds them, so reading them needs
/// no search index. The Patient is added explicitly, since the R4 definition has no <c>{def}</c> and so does not make it a
/// member of its own compartment (R5 does; it is counted once). Created for one call; not thread-safe.
/// </summary>
internal sealed class PatientCompartment
{
    /// <summary>The issue code for types whose membership the definition makes impossible to evaluate.</summary>
    public const string UnevaluableCode = "compartment-type-unevaluable";

    private readonly Dictionary<string, List<CatalogEntry>> _byType;
    private readonly CompartmentIndex _index;

    private PatientCompartment(FolderSession session, CompartmentIndex index, CatalogEntry patient)
    {
        _index = index;
        Folder = new FolderSearchSource(session);
        Patient = patient;
        Members = [patient, .. index.MembersOf(patient.Id).Where(member => member != patient)];
        _byType = Members
            .GroupBy(member => member.ResourceType, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
    }

    /// <summary>
    /// The whole folder, for what the compartment does not hold (another compartment, the resolved references of a page); its
    /// <see cref="FolderSearchSource.IndexesRead"/> are the indexes this answer read beyond the compartments.
    /// </summary>
    public FolderSearchSource Folder { get; }

    /// <summary>The Patient resource.</summary>
    public CatalogEntry Patient { get; }

    /// <summary>The Patient first, then the compartment's members type by type in ordinal order, in catalog order within a type.</summary>
    public IReadOnlyList<CatalogEntry> Members { get; }

    /// <summary>Types the compartment could hold whose membership cannot be evaluated, so they are left out (STU3 AuditEvent, Provenance).</summary>
    public IReadOnlyList<string> UnevaluableTypes => _index.UnevaluableTypes;

    /// <summary>The resource types among <see cref="Members"/>.</summary>
    public IReadOnlySet<string> Types => _byType.Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>When the newest index this answer depends on was built: the compartments' and any read beyond them.</summary>
    public DateTimeOffset IndexBuiltAt => Folder.IndexesRead.Select(index => index.BuiltAt).Append(_index.BuiltAt).Max();

    /// <summary>Loads the compartment of <c>Patient/<paramref name="patientId"/></c> from the session's patient compartments.</summary>
    /// <exception cref="ToolRequestException">The id is not a FHIR id, or the folder holds no such Patient.</exception>
    public static PatientCompartment Load(FolderSession session, string patientId)
    {
        ArgumentNullException.ThrowIfNull(session);

        PatientIds.Validate(patientId);
        // A digest names the Patient whose id is not a FHIR id and hashes to it, as patient_list lists it.
        if (session.FindByName("Patient", patientId) is not { } patient)
        {
            int patients = session.Catalog.Entries.Count(entry => entry.ResourceType == "Patient");
            throw ToolRequestException.Error("unknown-patient", $"The folder holds no Patient/{patientId} ({patients} Patient resources). Call patient_list (CLI: patients) for the patient ids.");
        }

        return new PatientCompartment(session, session.PatientCompartments, patient);
    }

    /// <summary>The members of <paramref name="resourceType"/>, in catalog order.</summary>
    public IReadOnlyList<CatalogEntry> OfType(string resourceType) =>
        _byType.TryGetValue(resourceType, out List<CatalogEntry>? members) ? members : [];

    /// <summary>The indexed form of a member, which reads (or builds) its type's index on first use.</summary>
    public IndexedResource Indexed(CatalogEntry member)
    {
        ArgumentNullException.ThrowIfNull(member);

        return Folder.Indexed(new(member.ResourceType, member.Id));
    }

    /// <summary>
    /// The duplicate-id warnings and the diagnostics of every index the answer depends on: those the compartments were read
    /// from and any read beyond them (diagnostics beyond the limit are counted in <paramref name="omitted"/>).
    /// </summary>
    public void AddDiagnostics(FolderSession session, List<IssueItem> issues, List<OmittedItem> omitted)
    {
        HashSet<string> covered = new(_index.IndexedTypes, StringComparer.Ordinal);
        List<ResourceTypeIndex> beyond = [.. Folder.IndexesRead.Where(index => !covered.Contains(index.ResourceType))];
        ResourceSearcher.AddDiagnostics(
            session,
            [.. _index.IndexedTypes, .. beyond.Select(index => index.ResourceType)],
            [.. _index.Diagnostics, .. beyond.SelectMany(index => index.Diagnostics)],
            issues,
            omitted);
    }

    /// <summary>The issue naming the types left out because their membership cannot be evaluated; null when there are none.</summary>
    public IssueItem? UnevaluableIssue(string patientReference) => UnevaluableTypes.Count == 0
        ? null
        : new IssueItem(
            "warning",
            UnevaluableCode,
            $"{string.Join(", ", UnevaluableTypes)} resources in the folder were not considered for {patientReference}'s compartment: the CompartmentDefinition lists codes for them that are not reference search parameters, so their membership cannot be evaluated.",
            Location: null);
}
