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
using Ignixa.CliOnFhir.Search.Candle;
using Ignixa.CliOnFhir.Session;
using Ignixa.Search.Expressions;
using Ignixa.Search.InMemory;

namespace Ignixa.CliOnFhir.Search;

/// <summary>Runs FHIR searches over the per-type index.</summary>
public static class ResourceSearcher
{
    public const int DefaultCount = 10;

    public const int MaxCount = 50;

    /// <summary>
    /// Searches the folder. A request that cannot be answered (unreadable folder, bad query, unsupported feature, unknown
    /// type) yields an envelope with null facts and an <c>error</c> issue rather than an exception.
    /// </summary>
    public static ToolEnvelope<SearchFacts?> Search(FolderSession session, SearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        if (EnvelopeAssembler.FolderProblem(session) is { } folderProblem)
        {
            return EnvelopeAssembler.Failure<SearchFacts>(session, folderProblem, cancellationToken);
        }

        try
        {
            return Execute(session, request, cancellationToken);
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Failure<SearchFacts>(session, ex.Issue, cancellationToken);
        }
    }

    private static ToolEnvelope<SearchFacts?> Execute(FolderSession session, SearchRequest request, CancellationToken cancellationToken)
    {
        ValidatePaging(request);
        ParsedSearch parsed = session.QueryParser.Parse(request);
        List<IssueItem> issues = [.. parsed.Warnings];

        int count = EffectiveCount(request.Count ?? parsed.RequestedCount, issues);
        ResourceTypeIndex index = session.GetIndex(request.ResourceType);
        List<IndexedResource> matches = Match(index, parsed);

        issues.AddRange(index.Diagnostics.Take(FolderInspector.DefaultIssueLimit).Select(EnvelopeAssembler.ToIssue));
        List<OmittedItem> omitted = index.Diagnostics.Count > FolderInspector.DefaultIssueLimit
            ? [new OmittedItem($"{index.Diagnostics.Count - FolderInspector.DefaultIssueLimit} more diagnostics", "limit")]
            : [];

        string query = request.Query ?? string.Empty;
        if (parsed.CountOnly)
        {
            var countFacts = new SearchFacts(request.ResourceType, query, matches.Count, request.Offset, Returned: 0, Resources: null);
            return EnvelopeAssembler.Create<SearchFacts?>(session, countFacts, index.BuiltAt, [], omitted, [], issues, cancellationToken);
        }

        if (request.Offset > 0 && request.Offset >= matches.Count)
        {
            issues.Add(new IssueItem("information", "offset-beyond-total", $"offset {request.Offset} is at or beyond the {matches.Count} matches, so no resources are returned.", Location: null));
        }

        IReadOnlyList<CatalogEntry> page = [.. matches.Skip(request.Offset).Take(count).Select(match => match.Entry)];
        int reserved = ReservedBytes(request, query, matches.Count, index.BuiltAt, issues, omitted);
        ResourceBatch batch = ResourceBatch.Collect(session.RootPath, page, parsed.Elements, reserved, cancellationToken);
        issues.AddRange(batch.Issues);
        omitted.AddRange(batch.Skipped);

        int next = request.Offset + batch.Consumed;
        if (next < matches.Count)
        {
            string why = batch.Consumed < page.Count ? "size-budget" : "limit";
            omitted.Add(new OmittedItem($"{matches.Count - next} more matches (offset={next})", why));
        }

        var facts = new SearchFacts(request.ResourceType, query, matches.Count, request.Offset, batch.Resources.Count, batch.Resources);
        return EnvelopeAssembler.Create<SearchFacts?>(session, facts, index.BuiltAt, batch.Evidence, omitted, [], issues, cancellationToken);
    }

    /// <summary>The serialized size of the response with no resources, evidence or batch issues, leaving room for one more omitted item.</summary>
    private static int ReservedBytes(SearchRequest request, string query, int total, DateTimeOffset builtAt, List<IssueItem> issues, List<OmittedItem> omitted)
    {
        var skeleton = new ToolEnvelope<SearchFacts?>(
            new SearchFacts(request.ResourceType, query, total, request.Offset, Returned: 0, Resources: []),
            [],
            [.. omitted, new OmittedItem(new string('x', 96), "size-budget")],
            [],
            issues,
            new FreshnessInfo(builtAt, StaleFiles: int.MaxValue));
        return EnvelopeJson.SizeOf(skeleton);
    }

    private static void ValidatePaging(SearchRequest request)
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

    private static int EffectiveCount(int? requested, List<IssueItem> issues)
    {
        if (requested > MaxCount)
        {
            issues.Add(new IssueItem("warning", "count-clamped", $"count {requested} exceeds the maximum of {MaxCount}, so {MaxCount} were returned per page; use offset to read further.", Location: null));
            return MaxCount;
        }

        return requested ?? DefaultCount;
    }

    /// <summary>The indexed resources matching the query, in catalog order.</summary>
    private static List<IndexedResource> Match(ResourceTypeIndex index, ParsedSearch parsed)
    {
        if (parsed.Options.Expression is not { } expression)
        {
            return [.. index.Resources];
        }

        HashSet<ResourceKey> matched = Evaluate(expression, index.Resources, parsed);
        return [.. index.Resources.Where(resource => matched.Contains(resource.Key))];
    }

    private static HashSet<ResourceKey> Evaluate(Expression expression, IReadOnlyList<IndexedResource> candidates, ParsedSearch parsed)
    {
        try
        {
            return Run(expression, candidates);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The interpreter evaluates lazily and signals unsupported expressions (and some unexpected input) with
            // assorted exception types, so find which parameter's expression fails on its own and name it.
            throw SearchFailures.NotSupported($"{NameFailingParameter(expression, candidates, parsed)} cannot be evaluated: {Detail(ex)}");
        }
    }

    private static HashSet<ResourceKey> Run(Expression expression, IReadOnlyList<IndexedResource> candidates)
    {
        var interpreter = new CandleSearchQueryInterpreter();
        SearchPredicate predicate = expression.AcceptVisitor(interpreter, interpreter.InitialContext);
        return [.. predicate(candidates.Select(resource => (resource.Key, resource.Index))).Select(match => match.Location)];
    }

    private static string NameFailingParameter(Expression expression, IReadOnlyList<IndexedResource> candidates, ParsedSearch parsed)
    {
        IReadOnlyList<Expression> parts = expression is MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and ? and.Expressions : [expression];
        List<string> failing = [];
        foreach (Expression part in parts)
        {
            try
            {
                Run(part, candidates);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failing.Add(parsed.Traces.FirstOrDefault(trace => ReferenceEquals(trace.Ir, part)) is { } trace ? $"'{trace.Key}={trace.Value}'" : $"({part.GetType().Name})");
            }
        }

        return failing.Count > 0 ? $"Query parameter {string.Join(", ", failing)}" : "The query";
    }

    private static string Detail(Exception ex) =>
        ex is Ignixa.Serialization.Abstractions.FhirException { Issues.Count: > 0 } fhir
            ? fhir.Issues.First().Diagnostics ?? ex.Message
            : $"{ex.GetType().Name}: {ex.Message}";
}
