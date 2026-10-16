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

/// <summary><c>patient_list</c>: the folder's patients with what each one's context holds, optionally only those matching a query.</summary>
public static class PatientLister
{
    /// <summary>By name (case-insensitively, patients without one last), then id.</summary>
    public const string ByName = "name";

    /// <summary>By latest clinical date, most recent first (patients without one last), then id.</summary>
    public const string ByLatest = "latest";

    /// <summary>Longest <c>query</c> accepted.</summary>
    public const int MaxQueryLength = 256;

    /// <summary>
    /// A page of the folder's Patient resources, each with its compartment's size, date span and domains. With
    /// <paramref name="query"/>, only the patients matching it (see <see cref="PatientMatch"/>). A request that cannot
    /// be answered (unreadable folder, bad paging, unknown sort, blank query) yields null facts and an <c>error</c> issue.
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
        var folder = new FolderSearchSource(session);
        // Names and dates read no references, so the Bundle references need not be resolved.
        var elements = new ResourceElements(session, resolveReferences: false);
        IReadOnlyList<IndexedResource> everyone = session.HoldsType("Patient") ? folder.Resources("Patient") : [];
        Dictionary<string, string?> names = everyone.ToDictionary(
            patient => patient.Key.Id,
            patient => elements.Get(patient.Entry) is { } element ? ClinicalValues.PatientName(element) : null,
            StringComparer.Ordinal);
        IReadOnlyList<IndexedResource> patients = match is null
            ? everyone
            : [.. everyone.Where(patient => elements.Get(patient.Entry) is { } element && match.Matches(patient.Key.Id, names[patient.Key.Id], element))];
        var shapes = new ValueShapes(session.Schema.Version, BindingMembership.IfLoaded(session));
        var summaries = new Summaries(folder, elements, names, shapes, cancellationToken);

        // Sorting by date needs every patient's span; by name, only the page's.
        IEnumerable<IndexedResource> ordered = sort == ByName
            ? patients
                .OrderBy(patient => names[patient.Key.Id] is null)
                .ThenBy(patient => names[patient.Key.Id], StringComparer.OrdinalIgnoreCase)
                .ThenBy(patient => names[patient.Key.Id], StringComparer.Ordinal)
                .ThenBy(patient => patient.Key.Id, StringComparer.Ordinal)
            : summaries.Prepare(patients)
                .OrderBy(patient => summaries.Get(patient).Latest is null)
                .ThenByDescending(patient => summaries.Get(patient).Latest)
                .ThenBy(patient => patient.Key.Id, StringComparer.Ordinal);

        issues.AddRange(Paging.BeyondTotal(offset, patients.Count, "patients"));
        List<IndexedResource> page = [.. ordered.Skip(offset).Take(pageSize)];
        List<PatientSummary> rows = [.. summaries.Prepare(page).Select(patient => summaries.Get(patient).Summary)];

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

        next.AddRange(page.Take(1).Select(patient => new NextCall("context_get_root", new Dictionary<string, object>(StringComparer.Ordinal) { ["patientId"] = FhirIds.Safe(patient.Key.Id) })));
        if (!ClinicalRules.Verified(session.Schema.Version))
        {
            issues.Add(PatientContext.UnsupportedVersionIssue(session.Schema.Version));
        }

        issues.AddRange(summaries.UnevaluableIssue());
        issues.AddRange(elements.Problems);
        issues.AddRange(shapes.Issues());
        issues.AddRange(NameIssues.For(page.Select(patient => patient.Entry)));
        ResourceSearcher.AddDiagnostics(session, folder, issues, omitted);

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
        DateTimeOffset builtAt = folder.IndexesRead.Count > 0 ? folder.IndexesRead.Max(index => index.BuiltAt) : session.Catalog.BuiltAt;
        return EnvelopeAssembler.Create<PatientListFacts?>(session, facts, builtAt, ContextRoot.Evidence(page.Select(patient => patient.Entry)), omitted, absent, issues, cancellationToken) with { Next = next };
    }

    /// <summary>
    /// Each patient's summary from its compartment, the compartments of a batch of patients found in one pass over the folder
    /// (<see cref="FolderSearchSource.CompartmentMembersOf"/>); the date span reads every member's main date with FHIRPath.
    /// </summary>
    private sealed class Summaries(FolderSearchSource folder, ResourceElements elements, Dictionary<string, string?> names, ValueShapes shapes, CancellationToken cancellationToken)
    {
        private readonly Dictionary<string, (PatientSummary Summary, FhirTemporal? Latest)> _summaries = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _unevaluable = new(StringComparer.Ordinal);

        /// <summary>Summarizes those of <paramref name="patients"/> not yet summarized, and returns them all, in order.</summary>
        public IReadOnlyList<IndexedResource> Prepare(IReadOnlyList<IndexedResource> patients)
        {
            HashSet<string> pending = [.. patients.Select(patient => patient.Key.Id).Where(id => !_summaries.ContainsKey(id))];
            if (pending.Count > 0)
            {
                (IReadOnlyDictionary<string, List<IndexedResource>> members, IReadOnlyList<string> unevaluable) = folder.CompartmentMembersOf("Patient", pending);
                _unevaluable.UnionWith(unevaluable);
                foreach (IndexedResource patient in patients.Where(patient => pending.Contains(patient.Key.Id)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _summaries.Add(patient.Key.Id, Summarize(PatientCompartment.Of(folder, patient, members[patient.Key.Id], unevaluable)));
                }
            }

            return patients;
        }

        /// <summary>The summary of a patient <see cref="Prepare"/> was given.</summary>
        public (PatientSummary Summary, FhirTemporal? Latest) Get(IndexedResource patient) => _summaries[patient.Key.Id];

        public IEnumerable<IssueItem> UnevaluableIssue()
        {
            if (_unevaluable.Count > 0)
            {
                yield return new IssueItem("warning", PatientCompartment.UnevaluableCode, $"{string.Join(", ", _unevaluable)} resources in the folder were not counted in any patient's compartment: the CompartmentDefinition lists codes for them that are not reference search parameters, so their membership cannot be evaluated.", Location: null);
            }
        }

        private (PatientSummary Summary, FhirTemporal? Latest) Summarize(PatientCompartment compartment)
        {
            IndexedResource patient = compartment.Patient;
            List<FhirTemporal> dates = [.. compartment.Members
                .Where(member => ClinicalRules.MainDate.ContainsKey(member.Key.ResourceType))
                .Select(member => elements.Get(member.Entry) is { } element ? ClinicalValues.MainDate(member.Key.ResourceType, element) : null)
                .OfType<FhirTemporal>()];
            FhirTemporal? earliest = dates.Count == 0 ? null : dates.Min();
            FhirTemporal? latest = dates.Count == 0 ? null : dates.Max();
            IElement? element = elements.Get(patient.Entry);
            var summary = new PatientSummary(
                FhirIds.Safe(patient.Key.Id),
                names[patient.Key.Id],
                element is null ? null : shapes.Gender("Patient", ClinicalValues.Literal(element, "gender")),
                element is null ? null : shapes.Date("Patient", "birthDate", ClinicalValues.Literal(element, "birthDate")),
                compartment.Members.Count,
                earliest?.Literal,
                latest?.Literal,
                ClinicalDomains.Present(compartment.Types));
            return (summary, latest);
        }
    }
}
