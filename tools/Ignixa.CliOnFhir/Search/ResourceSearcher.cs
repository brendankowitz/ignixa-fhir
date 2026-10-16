// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Inspection;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Session;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.InMemory;
using Ignixa.Search.Models;
using Ignixa.Serialization.Abstractions;

namespace Ignixa.CliOnFhir.Search;

/// <summary>Runs FHIR searches over the per-type indexes with Ignixa's in-memory search engine.</summary>
public static class ResourceSearcher
{
    public const int DefaultCount = 10;

    public const int MaxCount = 50;

    /// <summary>Most included resources returned with one page.</summary>
    public const int IncludeLimit = 50;

    /// <summary>
    /// Every in-memory search feature: chains, <c>_has</c>, <c>_sort</c>, <c>_include</c> and <c>_revinclude</c>; compartments
    /// are enabled by giving the engine the session's compartment membership.
    /// </summary>
    private const InMemorySearchFeatures Features = InMemorySearchFeatures.All;

    /// <summary>Stands in for an omitted item added after a batch is read, when reserving room for it.</summary>
    private static readonly OmittedItem Placeholder = new(new string('x', 96), "size-budget");

    /// <summary>
    /// Searches the folder. A request that cannot be answered (unreadable folder, bad query, unsupported feature, unknown
    /// type) yields an envelope with null facts and an <c>error</c> issue rather than an exception.
    /// </summary>
    public static ToolEnvelope<SearchFacts?> Search(FolderSession session, SearchRequest request, CancellationToken cancellationToken = default) =>
        Search(session, request, extraBytes: 0, cancellationToken);

    /// <summary>
    /// <see cref="Search(FolderSession, SearchRequest, CancellationToken)"/> leaving <paramref name="extraBytes"/> of the size cap
    /// unused, for a caller that adds that much to the envelope (the search plan beside its results).
    /// </summary>
    internal static ToolEnvelope<SearchFacts?> Search(FolderSession session, SearchRequest request, int extraBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<SearchFacts>(session, folderProblem, cancellationToken);
        }

        try
        {
            return Execute(session, request, extraBytes, cancellationToken);
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<SearchFacts>(session, ex.Issue, cancellationToken);
        }
    }

    /// <summary>
    /// Parses <paramref name="request"/> (digest ids rewritten, the compartment applied) and finds every match, unpaged: the
    /// part of a search that decides which resources match, shared by <see cref="Search"/> and the tools that work on a
    /// search's results.
    /// </summary>
    /// <exception cref="ToolRequestException">The query cannot be parsed or evaluated.</exception>
    internal static SearchMatch MatchAll(FolderSession session, SearchRequest request)
    {
        (ParsedSearch parsed, List<IssueItem> issues) = Parse(session, request);
        var source = new FolderSearchSource(session);
        var engine = new InMemorySearchEngine(source, Features, session.Compartments);
        return new SearchMatch(parsed, source, engine, Match(engine, source, parsed), issues);
    }

    /// <summary>
    /// Parses <paramref name="request"/> as a search does, digest ids rewritten and the compartment applied, without evaluating
    /// it; the parser's warnings and the compartment's notes are returned with it.
    /// </summary>
    /// <exception cref="ToolRequestException">The query cannot be parsed.</exception>
    internal static (ParsedSearch Parsed, List<IssueItem> Issues) Parse(FolderSession session, SearchRequest request)
    {
        ParsedSearch parsed = session.QueryParser.Parse(request with { Query = DigestIdQuery.Rewrite(session, request.ResourceType, request.Query) });
        List<IssueItem> issues = [.. parsed.Warnings];
        if (request.Compartment is { } compartment)
        {
            (SearchOptions scoped, IReadOnlyList<IssueItem> compartmentIssues) = CompartmentArgument.Apply(session, parsed.Options, compartment);
            parsed = parsed with { Options = scoped };
            issues.AddRange(compartmentIssues);
        }

        return (parsed, issues);
    }

    /// <summary>
    /// Evaluates <paramref name="parsed"/> with the same engine and features as a search, over no resources: every refusal the
    /// engine decides from the query and the definitions alone (a canonical-only chain or include, a wildcard
    /// <c>_revinclude</c>, an unevaluable compartment, an expression the interpreter cannot compile) is raised exactly as a
    /// search raises it, without reading any index.
    /// </summary>
    /// <exception cref="ToolRequestException">The search would be refused.</exception>
    internal static void Compile(FolderSession session, ParsedSearch parsed)
    {
        var engine = new InMemorySearchEngine(EmptySearchSource.Instance, Features, session.Compartments);
        _ = Evaluate(engine, parsed);
    }

    private static ToolEnvelope<SearchFacts?> Execute(FolderSession session, SearchRequest request, int extraBytes, CancellationToken cancellationToken)
    {
        ValidatePaging(request);
        (ParsedSearch parsed, FolderSearchSource source, InMemorySearchEngine engine, List<IndexedResource> matches, List<IssueItem> issues) = MatchAll(session, request);
        int count = EffectiveCount(request.Count ?? parsed.RequestedCount, issues);

        string query = request.Query ?? string.Empty;
        List<OmittedItem> omitted = [];
        if (parsed.CountOnly)
        {
            issues.AddRange(ReferenceFollowChecks.UnresolvedReferences(source, new SearchOptions(parsed.Options) { Include = [], RevInclude = [] }, []));
            AddDiagnostics(session, source, issues, omitted);
            var countFacts = new SearchFacts(request.ResourceType, query, request.Compartment, matches.Count, request.Offset, Returned: 0, Resources: null);
            return Envelope(session, source, countFacts, [], omitted, issues, cancellationToken);
        }

        if (request.Offset > 0 && request.Offset >= matches.Count)
        {
            issues.Add(new IssueItem("information", "offset-beyond-total", $"offset {request.Offset} is at or beyond the {matches.Count} matches, so no resources are returned.", Location: null));
        }

        List<IndexedResource> page = [.. matches.Skip(request.Offset).Take(count)];
        List<IndexedResource>? included = null;
        if (HasIncludes(parsed.Options))
        {
            // One past the cap tells whether more exist without resolving every one :iterate could reach.
            included = Include(engine, source, page, parsed, IncludeLimit + 1);
            issues.AddRange(ReferenceFollowChecks.InapplicableIncludes(parsed.Options));
        }

        issues.AddRange(ReferenceFollowChecks.UnresolvedReferences(source, parsed.Options, [.. page, .. included ?? []]));
        AddDiagnostics(session, source, issues, omitted);
        issues.AddRange(NameIssues.For([.. page.Select(match => match.Entry), .. (included ?? []).Take(IncludeLimit).Select(resource => resource.Entry)]));

        // The listed resolutions can only shrink once the resources are read, so reserving the candidates keeps the cap.
        Dictionary<string, Dictionary<string, string>> pageCandidates = ResolvedReferences.Candidates(page, ResolvedReferences.Limit);
        int reserved = ReservedBytes(request, query, matches.Count, included, issues, omitted, pageCandidates) + extraBytes;
        ResourceBatch batch = ResourceBatch.Collect(session.RootPath, [.. page.Select(match => match.Entry)], parsed.Elements, reserved, cancellationToken);
        issues.AddRange(batch.Issues);
        omitted.AddRange(batch.Skipped);
        List<EvidenceItem> evidence = [.. batch.Evidence];
        (Dictionary<string, Dictionary<string, string>> resolved, int notListed) = ResolvedReferences.Shown(page, batch, pageCandidates);

        IReadOnlyList<System.Text.Json.Nodes.JsonNode>? includedResources = null;
        if (included is not null)
        {
            List<IndexedResource> wanted = [.. included.Take(IncludeLimit)];
            Dictionary<string, Dictionary<string, string>> includedCandidates = ResolvedReferences.Candidates(wanted, ResolvedReferences.Limit - ResolvedReferences.Count(resolved));
            var pageOnly = new SearchFacts(request.ResourceType, query, request.Compartment, matches.Count, request.Offset, batch.Resources.Count, batch.Resources, [], ResolvedReferences.ToFacts(ResolvedReferences.Merge(resolved, includedCandidates)));
            ResourceBatch includedBatch = CollectIncluded(session, pageOnly, included, evidence, omitted, issues, extraBytes, cancellationToken);
            includedResources = includedBatch.Resources;
            (Dictionary<string, Dictionary<string, string>> includedShown, int includedNotListed) = ResolvedReferences.Shown(wanted, includedBatch, includedCandidates);
            resolved = ResolvedReferences.Merge(resolved, includedShown);
            notListed += includedNotListed;
        }

        if (notListed > 0)
        {
            omitted.Add(new OmittedItem($"{notListed} more resolved Bundle references in the returned resources (resolvedReferences lists at most {ResolvedReferences.Limit})", "limit"));
        }

        int next = request.Offset + batch.Consumed;
        if (next < matches.Count)
        {
            string why = batch.Consumed < page.Count ? "size-budget" : "limit";
            omitted.Add(new OmittedItem($"{matches.Count - next} more matches (offset={next})", why));
        }

        var facts = new SearchFacts(request.ResourceType, query, request.Compartment, matches.Count, request.Offset, batch.Resources.Count, batch.Resources, includedResources, ResolvedReferences.ToFacts(resolved));
        return Envelope(session, source, facts, evidence, omitted, issues, cancellationToken);
    }

    private static bool HasIncludes(SearchOptions options) => options.Include.Count > 0 || options.RevInclude.Count > 0;

    /// <summary>
    /// Reads up to <see cref="IncludeLimit"/> included resources whole (the <c>elements</c> projection names elements of the
    /// searched type), within what the page left of the size budget, adding their evidence, problems and omissions.
    /// </summary>
    private static ResourceBatch CollectIncluded(
        FolderSession session,
        SearchFacts pageOnly,
        List<IndexedResource> included,
        List<EvidenceItem> evidence,
        List<OmittedItem> omitted,
        List<IssueItem> issues,
        int extraBytes,
        CancellationToken cancellationToken)
    {
        List<CatalogEntry> wanted = [.. included.Take(IncludeLimit).Select(resource => resource.Entry)];
        int reserved = EnvelopeJson.SizeOf(new ToolEnvelope<SearchFacts?>(
            pageOnly,
            evidence,
            [.. omitted, Placeholder, Placeholder, Placeholder],
            [],
            issues,
            new FreshnessInfo(DateTimeOffset.MaxValue, StaleFiles: int.MaxValue))) + extraBytes;

        ResourceBatch batch = ResourceBatch.Collect(session.RootPath, wanted, projection: null, reserved, cancellationToken);
        evidence.AddRange(batch.Evidence);
        issues.AddRange(batch.Issues);
        omitted.AddRange(batch.Skipped);

        string why = batch.Consumed < wanted.Count ? "size-budget" : "limit";
        if (included.Count > IncludeLimit)
        {
            omitted.Add(new OmittedItem($"more included resources than the {batch.Consumed} returned (at most {IncludeLimit} are resolved per page; ask for a smaller page or a narrower include)", why));
        }
        else if (batch.Consumed < included.Count)
        {
            omitted.Add(new OmittedItem($"{included.Count - batch.Consumed} more included resources (read them with read_resources or the get verb, or ask for a smaller page)", why));
        }

        return batch;
    }

    /// <summary>
    /// Reports the diagnostics of every index the search read: the searched type, and the types chains and includes read,
    /// whose unindexable resources a chain or include would otherwise miss without a trace; and, first, for each of those
    /// types, the duplicates the catalog dropped, which the search never saw.
    /// </summary>
    internal static void AddDiagnostics(FolderSession session, FolderSearchSource source, List<IssueItem> issues, List<OmittedItem> omitted)
    {
        issues.AddRange(DuplicateIdIssues.For(session.Catalog, source.IndexesRead.Select(index => index.ResourceType)));
        List<CatalogDiagnostic> diagnostics = [.. source.IndexesRead.SelectMany(index => index.Diagnostics)];
        issues.AddRange(diagnostics.Take(FolderInspector.DefaultIssueLimit).Select(EnvelopeAssembler.ToIssue));
        if (diagnostics.Count > FolderInspector.DefaultIssueLimit)
        {
            omitted.Add(new OmittedItem($"{diagnostics.Count - FolderInspector.DefaultIssueLimit} more diagnostics", "limit"));
        }
    }

    private static ToolEnvelope<SearchFacts?> Envelope(
        FolderSession session,
        FolderSearchSource source,
        SearchFacts facts,
        IReadOnlyList<EvidenceItem> evidence,
        IReadOnlyList<OmittedItem> omitted,
        IReadOnlyList<IssueItem> issues,
        CancellationToken cancellationToken) =>
        EnvelopeAssembler.Create<SearchFacts?>(session, facts, source.IndexesRead.Max(index => index.BuiltAt), evidence, omitted, [], issues, cancellationToken);

    /// <summary>
    /// The serialized size of the response with no resources, evidence or batch issues, leaving room for the omitted items
    /// added after the batch is read.
    /// </summary>
    private static int ReservedBytes(
        SearchRequest request,
        string query,
        int total,
        List<IndexedResource>? included,
        List<IssueItem> issues,
        List<OmittedItem> omitted,
        Dictionary<string, Dictionary<string, string>> resolved)
    {
        var skeleton = new ToolEnvelope<SearchFacts?>(
            new SearchFacts(request.ResourceType, query, request.Compartment, total, request.Offset, Returned: 0, Resources: [], included is null ? null : [], ResolvedReferences.ToFacts(resolved)),
            [],
            [.. omitted, .. Enumerable.Repeat(Placeholder, included is null ? 2 : 3)],
            [],
            issues,
            new FreshnessInfo(DateTimeOffset.MaxValue, StaleFiles: int.MaxValue));
        return EnvelopeJson.SizeOf(skeleton);
    }

    internal static void ValidatePaging(SearchRequest request)
    {
        if (request.Offset < 0)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'offset' must not be negative, not {request.Offset}.");
        }

        if (request.Count is < 1)
        {
            throw ToolRequestException.Error("invalid-parameter", $"'count' must be at least 1, not {request.Count}. Use summary=count to get only the total.");
        }
    }

    internal static int EffectiveCount(int? requested, List<IssueItem> issues)
    {
        if (requested > MaxCount)
        {
            issues.Add(new IssueItem("warning", "count-clamped", $"count {requested} exceeds the maximum of {MaxCount}, so {MaxCount} were returned per page; use offset to read further.", Location: null));
            return MaxCount;
        }

        return requested ?? DefaultCount;
    }

    /// <summary>The indexed resources matching the query, in <c>_sort</c> order or else catalog order.</summary>
    private static List<IndexedResource> Match(InMemorySearchEngine engine, FolderSearchSource source, ParsedSearch parsed) =>
        [.. Evaluate(engine, parsed).Select(match => source.Indexed(match.Location))];

    private static IReadOnlyList<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> Evaluate(InMemorySearchEngine engine, ParsedSearch parsed)
    {
        try
        {
            return engine.Search(parsed.Options);
        }
        catch (SearchOperationNotSupportedException ex)
        {
            // The engine evaluates the whole tree at once, so find which parameter's expression fails on its own and name it.
            throw SearchFailures.NotSupported($"{NameFailingParameter(engine, parsed)} cannot be evaluated: {Detail(ex)}");
        }
        catch (FhirException ex)
        {
            // A value the pipeline parsed but the engine rejects as a request fault (for example an _id with a system).
            throw SearchFailures.From(ex, NameFailingParameter(engine, parsed));
        }
    }

    private static List<IndexedResource> Include(InMemorySearchEngine engine, FolderSearchSource source, List<IndexedResource> page, ParsedSearch parsed, int maxIncluded)
    {
        try
        {
            return [.. engine.Include([.. page.Select(match => (match.Key, match.Index))], parsed.Options, maxIncluded).Select(resource => source.Indexed(resource.Location))];
        }
        catch (SearchOperationNotSupportedException ex)
        {
            throw SearchFailures.NotSupported($"The includes cannot be resolved: {Detail(ex)}");
        }
    }

    private static string NameFailingParameter(InMemorySearchEngine engine, ParsedSearch parsed)
    {
        if (parsed.Options.Expression is not { } expression)
        {
            return "The query";
        }

        IReadOnlyList<Expression> parts = expression is MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and ? and.Expressions : [expression];
        List<string> failing = [];
        foreach (Expression part in parts)
        {
            try
            {
                engine.Search(new SearchOptions { ResourceType = parsed.Options.ResourceType, Expression = part });
            }
            catch (FhirException)
            {
                failing.Add(part switch
                {
                    CompartmentSearchExpression compartment => $"'compartment={FhirIds.Reference(compartment.CompartmentType, compartment.CompartmentId)}'",
                    _ when parsed.Traces.FirstOrDefault(trace => ReferenceEquals(trace.Ir, part)) is { } trace => $"'{trace.Key}={trace.Value}'",
                    _ => $"({part.GetType().Name})",
                });
            }
        }

        return failing.Count > 0 ? $"Query parameter {string.Join(", ", failing)}" : "The query";
    }

    private static string Detail(FhirException ex) => ex.Issues.FirstOrDefault()?.Diagnostics ?? ex.Message;
}
