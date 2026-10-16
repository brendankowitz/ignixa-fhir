// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Session;
using Ignixa.Search.InMemory;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// The <c>patientSearch(type, query)</c> FHIRPath function of <c>fhirpath</c> and <c>columns</c>: a FHIR search of
/// <c>type</c> inside the focus Patient's compartment, as <c>Patient/&lt;id&gt;/&lt;type&gt;?&lt;query&gt;</c> would run, returning
/// the matching resources in <c>_sort</c> order (else catalog order), at most <c>_count</c> of them. It lets a cohort be one
/// call on Patient: <c>entry.resource.where(patientSearch('Condition','code=...&amp;clinical-status=active').exists()).count()</c>.
/// </summary>
/// <remarks>
/// <para>
/// Search semantics are the tool's own, unchanged: nothing is filtered implicitly, so a Condition of any clinical or
/// verification status matches unless the query says <c>clinical-status=active</c> (or <c>verification-status:not=refuted</c>).
/// <c>_include</c>, <c>_revinclude</c>, <c>_summary</c> and <c>_elements</c> are refused: the function returns whole resources.
/// </para>
/// <para>
/// Each distinct (type, query) is searched once over the whole folder and its matches grouped by the patients whose Patient
/// compartment they are in (the membership <c>compartment</c> and <c>perPatient</c> use), so a call that runs the same
/// subquery for 5,000 patients searches once. A compartment search equals the folder search restricted to the compartment,
/// and restriction keeps the order, so each patient gets exactly what the compartment search would return. Results per
/// patient are cached for the call. Work is bounded: at most <see cref="MaxQueries"/> distinct subqueries and
/// <see cref="MaxResources"/> resources handed out per call, beyond which the call fails as <c>too-many-matches</c>.
/// </para>
/// <para>Created for one call; not thread-safe.</para>
/// </remarks>
/// <param name="session">The folder.</param>
/// <param name="read">Reads a match as FHIRPath input (null when it cannot be read now), so its references resolve as the caller's do.</param>
/// <param name="maxQueries">Most distinct subqueries per call; <see cref="MaxQueries"/> but in tests.</param>
/// <param name="maxResources">Most resources handed out per call; <see cref="MaxResources"/> but in tests.</param>
internal sealed class PatientSearch(FolderSession session, Func<CatalogEntry, IElement?> read, int maxQueries = PatientSearch.MaxQueries, int maxResources = PatientSearch.MaxResources)
{
    public const string FunctionName = "patientSearch";

    /// <summary>Most distinct (type, query) pairs one call may search; each is a search over the whole folder.</summary>
    public const int MaxQueries = 50;

    /// <summary>Most resources the function may hand out in one call, counted once per (subquery, patient); each is read and held in memory.</summary>
    public const int MaxResources = 50_000;

    private const string CompartmentType = "Patient";
    private const int MaxQueryEcho = 200;

    private readonly Dictionary<(string Type, string Query), Subquery> _queries = [];
    private readonly Dictionary<(Subquery Query, string PatientId), IReadOnlyList<IElement>> _results = [];
    private readonly List<IssueItem> _notes = [];
    private readonly List<OmittedItem> _omitted = [];
    private int _handedOut;

    /// <summary>
    /// The parser's warnings for each distinct subquery (a <c>parameter-ignored</c>, say), prefixed with the subquery, then the
    /// diagnostics of the indexes the subqueries read (a resource that could not be indexed may be missing from them).
    /// </summary>
    public IReadOnlyList<IssueItem> Notes => _notes;

    /// <summary>Diagnostics beyond those listed in <see cref="Notes"/>, counted.</summary>
    public IReadOnlyList<OmittedItem> Omitted => _omitted;

    /// <summary>The function as the evaluator calls it.</summary>
    /// <exception cref="ToolRequestException">The focus is not one Patient, an argument is not a string, the subquery fails, or a work limit is passed.</exception>
    public IEnumerable<IElement> Invoke(IReadOnlyList<IElement> focus, IReadOnlyList<IReadOnlyList<IElement>> arguments)
    {
        (string type, string query) = Arguments(arguments);
        if (focus.Count == 0)
        {
            return [];
        }

        if (focus.Count > 1 || focus[0].InstanceType != CompartmentType)
        {
            string got = focus.Count > 1 ? $"{focus.Count} items" : $"a {focus[0].InstanceType}";
            throw Invalid($"{FunctionName}() searches inside one Patient's compartment, so its focus must be a single Patient, not {got}. Search Patient and write entry.resource.where({FunctionName}('{type}', '...').exists()), or follow a reference first: subject.resolve().{FunctionName}(...).");
        }

        if (focus[0].Children("id") is not [{ Value: string patientId }, ..])
        {
            return [];
        }

        Subquery subquery = Search(type, query);
        if (_results.TryGetValue((subquery, patientId), out IReadOnlyList<IElement>? cached))
        {
            return cached;
        }

        List<IndexedResource> matches = subquery.ByPatient.GetValueOrDefault(patientId) ?? [];
        IEnumerable<IndexedResource> taken = subquery.Count is int count ? matches.Take(count) : matches;
        List<IElement> resources = [.. taken.Select(match => read(match.Entry)).OfType<IElement>()];
        _handedOut += resources.Count;
        if (_handedOut > maxResources)
        {
            throw ToolRequestException.Error(
                "too-many-matches",
                $"{FunctionName}() handed out more than {maxResources} resources in this call ({Describe(type, query)} alone has {matches.Count} for Patient/{FhirIds.Safe(patientId)}); add _count=1 with _sort to keep only the latest, narrow the subquery with a code or date, or narrow the Patient search.");
        }

        _results.Add((subquery, patientId), resources);
        return resources;
    }

    private Subquery Search(string type, string query)
    {
        if (_queries.TryGetValue((type, query), out Subquery? known))
        {
            return known;
        }

        if (_queries.Count >= maxQueries)
        {
            throw ToolRequestException.Error(
                "too-many-matches",
                $"{FunctionName}() ran {maxQueries} distinct subqueries in this call, the most allowed: each is a search over the whole folder. Write each subquery as a literal so it is the same for every patient (dates fixed, not computed from the patient), and combine conditions in one query.");
        }

        InMemoryCompartmentMembership membership = session.Compartments;
        if (!membership.ResourceTypes(CompartmentType).Contains(type, StringComparer.Ordinal))
        {
            throw Invalid($"{FunctionName}('{type}', ...): {type} is never in a Patient compartment ({session.Schema.Version}), so no patient has any; use a type such as Condition, Observation, MedicationRequest, Encounter or Procedure.");
        }

        SearchMatch match;
        try
        {
            match = ResourceSearcher.MatchAll(session, new SearchRequest(type, query));
        }
        catch (ToolRequestException ex)
        {
            throw new ToolRequestException(ex.Issue with { Diagnostics = $"{Describe(type, query)}: {ex.Issue.Diagnostics}" });
        }

        RefuseProjections(type, query, match.Parsed);
        _notes.AddRange(match.Issues.Select(issue => issue with { Diagnostics = $"{Describe(type, query)}: {issue.Diagnostics}" }));
        List<IssueItem> diagnostics = [];
        ResourceSearcher.AddDiagnostics(session, match.Source, diagnostics, _omitted);
        _notes.AddRange(diagnostics.Where(issue => !_notes.Contains(issue)));

        Dictionary<string, List<IndexedResource>> byPatient = new(StringComparer.Ordinal);
        foreach (IndexedResource resource in match.Matches)
        {
            foreach (string owner in PerPatient.Owners(membership, resource))
            {
                (byPatient.TryGetValue(owner, out List<IndexedResource>? list) ? list : byPatient[owner] = []).Add(resource);
            }
        }

        var subquery = new Subquery(byPatient, match.Parsed.RequestedCount);
        _queries.Add((type, query), subquery);
        return subquery;
    }

    private static void RefuseProjections(string type, string query, ParsedSearch parsed)
    {
        if (parsed.CountOnly || parsed.Elements is not null || parsed.Options.Include.Count > 0 || parsed.Options.RevInclude.Count > 0)
        {
            throw Invalid($"{Describe(type, query)}: {FunctionName}() returns the matching resources whole, so _summary, _elements, _include and _revinclude do not apply; leave them out (use .count() or .exists() on the result, and resolve() to follow references).");
        }
    }

    private static (string Type, string Query) Arguments(IReadOnlyList<IReadOnlyList<IElement>> arguments)
    {
        if (arguments.Count is < 1 or > 2)
        {
            throw Invalid($"{FunctionName}() takes a resource type and a query: {FunctionName}('Observation', 'code=http://loinc.org|4548-4&_sort=-date&_count=1').");
        }

        string type = Text(arguments[0], "its first argument, the resource type");
        string query = arguments.Count == 2 ? Text(arguments[1], "its second argument, the FHIR search query") : string.Empty;
        return (type, query);
    }

    private static string Text(IReadOnlyList<IElement> argument, string what) =>
        argument is [{ Value: string text }]
            ? text
            : throw Invalid($"{FunctionName}() needs a single string as {what}, for example '{(what.Contains("type", StringComparison.Ordinal) ? "Condition" : "code=http://snomed.info/sct|44054006&clinical-status=active")}'.");

    private static string Describe(string type, string query) =>
        $"{FunctionName}('{type}', '{(query.Length > MaxQueryEcho ? query[..MaxQueryEcho] + "..." : query)}')";

    private static ToolRequestException Invalid(string message) => ToolRequestException.Error("invalid-parameter", message);

    /// <summary>One distinct subquery: its matches per patient id, in result order, and its <c>_count</c>. Compared by identity.</summary>
    private sealed class Subquery(IReadOnlyDictionary<string, List<IndexedResource>> byPatient, int? count)
    {
        public IReadOnlyDictionary<string, List<IndexedResource>> ByPatient { get; } = byPatient;

        public int? Count { get; } = count;
    }
}
