// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// <c>patient_list</c>: the patients matching a query, read from the Patient resources alone (a lookup), or every patient
/// with what each one's context holds (a listing).
/// </summary>
public static class PatientLister
{
    /// <summary>By name (case-insensitively, patients without one last), then id.</summary>
    public const string ByName = "name";

    /// <summary>By latest clinical date, most recent first (patients without one last), then id.</summary>
    public const string ByLatest = "latest";

    /// <summary>Longest <c>query</c> accepted.</summary>
    public const int MaxQueryLength = 256;

    /// <summary>
    /// A page of the folder's Patient resources. With <paramref name="query"/>, only the patients matching it (see
    /// <see cref="PatientMatch"/>), each with the Patient's own fields: sorted by name, the lookup reads the catalog and the
    /// Patient resources, never a search index, so it is fast even before any index exists. Without a query, or sorted by
    /// latest date, each row also has its compartment's size, date span and domains. A request that cannot be answered
    /// (unreadable folder, bad paging, unknown sort, blank query) yields null facts and an <c>error</c> issue.
    /// </summary>
    public static ToolEnvelope<PatientListFacts?> List(FolderSession session, int? count = null, int offset = 0, string? sort = null, string? query = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<PatientListFacts>(session, folderProblem, cancellationToken);
        }

        try
        {
            return Execute(session, count, offset, sort ?? ByName, query, cancellationToken);
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<PatientListFacts>(session, ex.Issue, cancellationToken);
        }
    }

    private static ToolEnvelope<PatientListFacts?> Execute(FolderSession session, int? count, int offset, string sort, string? query, CancellationToken cancellationToken)
    {
        List<IssueItem> issues = [];
        int pageSize = Paging.Resolve(count, offset, issues);
        if (sort is not (ByName or ByLatest))
        {
            throw ToolRequestException.Error("invalid-parameter", $"'sort' must be '{ByName}' or '{ByLatest}', not '{sort}'.");
        }

        PatientMatch? match = query is null ? null : PatientMatch.Parse(query);
        ClinicalRules.Precompile();
        // Names, identifiers and dates read no references, so the Bundle references need not be resolved.
        var elements = new ResourceElements(session, resolveReferences: false);
        List<CatalogEntry> everyone = [.. session.Catalog.Entries.Where(entry => entry.ResourceType == "Patient")];
        Dictionary<string, string?> names = everyone.ToDictionary(
            patient => patient.Id,
            patient => elements.Get(patient) is { } element ? ClinicalValues.PatientName(element) : null,
            StringComparer.Ordinal);
        IReadOnlyList<CatalogEntry> patients = match is null
            ? everyone
            : [.. everyone.Where(patient => elements.Get(patient) is { } element && match.Matches(patient.Id, names[patient.Id], element))];
        var shapes = new ValueShapes(session.Schema.Version, BindingMembership.IfLoaded(session));
        // A lookup by name order reads only the Patient resources; anything else summarizes compartments.
        Summaries? summaries = match is null || sort == ByLatest ? new Summaries(session.PatientCompartments, elements, names, shapes, cancellationToken) : null;

        // Sorting by date needs every patient's span; by name, only the page's.
        IEnumerable<CatalogEntry> ordered = sort == ByName
            ? patients
                .OrderBy(patient => names[patient.Id] is null)
                .ThenBy(patient => names[patient.Id], StringComparer.OrdinalIgnoreCase)
                .ThenBy(patient => names[patient.Id], StringComparer.Ordinal)
                .ThenBy(patient => patient.Id, StringComparer.Ordinal)
            : summaries!.Prepare(patients)
                .OrderBy(patient => summaries.Get(patient).Latest is null)
                .ThenByDescending(patient => summaries.Get(patient).Latest)
                .ThenBy(patient => patient.Id, StringComparer.Ordinal);

        issues.AddRange(Paging.BeyondTotal(offset, patients.Count, "patients"));
        List<CatalogEntry> page = [.. ordered.Skip(offset).Take(pageSize)];
        List<PatientSummary> rows = summaries is null
            ? [.. page.Select(patient => Identify(patient, names[patient.Id], elements, shapes))]
            : [.. summaries.Prepare(page).Select(patient => summaries.Get(patient).Summary)];

        List<OmittedItem> omitted = [];
        List<NextCall> next = [];
        if (offset + page.Count < patients.Count)
        {
            omitted.Add(new OmittedItem($"{patients.Count - offset - page.Count} more patients (offset={offset + page.Count})", "limit"));
            var nextPage = new Dictionary<string, object>(StringComparer.Ordinal) { ["offset"] = offset + page.Count, ["sort"] = sort };
            if (query is not null)
            {
                nextPage["query"] = query;
            }

            next.Add(new NextCall("patient_list", nextPage));
        }

        next.AddRange(page.Take(1).Select(patient => new NextCall("patient_context", new Dictionary<string, object>(StringComparer.Ordinal) { ["operation"] = "root", ["patientId"] = FhirIds.Safe(patient.Id) })));
        if (!ClinicalRules.Verified(session.Schema.Version))
        {
            issues.Add(PatientContext.UnsupportedVersionIssue(session.Schema.Version));
        }

        issues.AddRange(summaries?.UnevaluableIssue() ?? []);
        issues.AddRange(elements.Problems);
        issues.AddRange(shapes.Issues());
        issues.AddRange(NameIssues.For(page));
        if (summaries is null)
        {
            issues.AddRange(DuplicateIdIssues.For(session.Catalog, ["Patient"]));
        }
        else
        {
            ResourceSearcher.AddDiagnostics(session, session.PatientCompartments.IndexedTypes, session.PatientCompartments.Diagnostics, issues, omitted);
        }

        List<AbsentItem> absent = patients.Count == 0
            ? [new AbsentItem(
                query is null ? "Patient" : $"Patient matching '{query}'",
                query is null
                    ? $"catalog of {session.RootPath}: 0 Patient resources in {session.Catalog.FilesScanned} files"
                    : $"{everyone.Count} Patient resources in {session.Catalog.FilesScanned} files: id, name, identifier values and birthDate",
                session.Catalog.BuiltAt)]
            : [];
        Dictionary<string, string>? dateRule = sort == ByLatest
            ? ClinicalRules.MainDate
                .Where(rule => session.HoldsType(rule.Key))
                .OrderBy(rule => rule.Key, StringComparer.Ordinal)
                .ToDictionary(rule => rule.Key, rule => string.Join(", else ", rule.Value), StringComparer.Ordinal)
            : null;

        var facts = new PatientListFacts(patients.Count, offset, rows.Count, sort, rows, shapes.Membership, query, dateRule);
        DateTimeOffset builtAt = summaries is null ? session.Catalog.BuiltAt : session.PatientCompartments.BuiltAt;
        return EnvelopeAssembler.Create<PatientListFacts?>(session, facts, builtAt, ContextRoot.Evidence(page), omitted, absent, issues, cancellationToken) with { Next = next };
    }

    /// <summary>The Patient resource's own fields, as a lookup gives them.</summary>
    private static PatientSummary Identify(CatalogEntry patient, string? name, ResourceElements elements, ValueShapes shapes)
    {
        IElement? element = elements.Get(patient);
        return new PatientSummary(
            FhirIds.Safe(patient.Id),
            name,
            element is null ? null : shapes.Gender("Patient", ClinicalValues.Literal(element, "gender")),
            element is null ? null : shapes.Date("Patient", "birthDate", ClinicalValues.Literal(element, "birthDate")));
    }

    /// <summary>
    /// Each patient's summary from its compartment (<see cref="FolderSession.PatientCompartments"/>); the date span reads every
    /// member's main date with FHIRPath, so only the patients a page (or a latest-first order) needs are summarized.
    /// </summary>
    private sealed class Summaries(CompartmentIndex compartments, ResourceElements elements, Dictionary<string, string?> names, ValueShapes shapes, CancellationToken cancellationToken)
    {
        private readonly Dictionary<string, (PatientSummary Summary, FhirTemporal? Latest)> _summaries = new(StringComparer.Ordinal);

        /// <summary>Summarizes those of <paramref name="patients"/> not yet summarized, and returns them all, in order.</summary>
        public IReadOnlyList<CatalogEntry> Prepare(IReadOnlyList<CatalogEntry> patients)
        {
            foreach (CatalogEntry patient in patients.Where(patient => !_summaries.ContainsKey(patient.Id)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _summaries.Add(patient.Id, Summarize(patient));
            }

            return patients;
        }

        /// <summary>The summary of a patient <see cref="Prepare"/> was given.</summary>
        public (PatientSummary Summary, FhirTemporal? Latest) Get(CatalogEntry patient) => _summaries[patient.Id];

        public IEnumerable<IssueItem> UnevaluableIssue()
        {
            if (compartments.UnevaluableTypes.Count > 0)
            {
                yield return new IssueItem("warning", PatientCompartment.UnevaluableCode, $"{string.Join(", ", compartments.UnevaluableTypes)} resources in the folder were not counted in any patient's compartment: the CompartmentDefinition lists codes for them that are not reference search parameters, so their membership cannot be evaluated.", Location: null);
            }
        }

        private (PatientSummary Summary, FhirTemporal? Latest) Summarize(CatalogEntry patient)
        {
            List<CatalogEntry> members = [patient, .. compartments.MembersOf(patient.Id).Where(member => member != patient)];
            List<FhirTemporal> dates = [.. members
                .Where(member => ClinicalRules.MainDate.ContainsKey(member.ResourceType))
                .Select(member => elements.Get(member) is { } element ? ClinicalValues.MainDate(member.ResourceType, element) : null)
                .OfType<FhirTemporal>()];
            FhirTemporal? earliest = dates.Count == 0 ? null : dates.Min();
            FhirTemporal? latest = dates.Count == 0 ? null : dates.Max();
            IElement? element = elements.Get(patient);
            var summary = new PatientSummary(
                FhirIds.Safe(patient.Id),
                names[patient.Id],
                element is null ? null : shapes.Gender("Patient", ClinicalValues.Literal(element, "gender")),
                element is null ? null : shapes.Date("Patient", "birthDate", ClinicalValues.Literal(element, "birthDate")),
                members.Count,
                earliest?.Literal,
                latest?.Literal,
                ClinicalDomains.Present(members.Select(member => member.ResourceType).ToHashSet(StringComparer.Ordinal)));
            return (summary, latest);
        }
    }
}
