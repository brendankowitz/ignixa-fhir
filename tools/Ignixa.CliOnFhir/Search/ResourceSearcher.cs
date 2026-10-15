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
using Ignixa.Search.Indexing;
using Ignixa.Search.InMemory;

namespace Ignixa.CliOnFhir.Search;

/// <summary>Runs FHIR searches over the per-type index.</summary>
public static class ResourceSearcher
{
    public const int DefaultCount = 10;

    public const int MaxCount = 50;

    /// <summary>
    /// Searches the folder. A request that cannot be answered (bad query, unsupported feature, unknown type) yields
    /// an envelope with null facts and an <c>error</c> issue rather than an exception.
    /// </summary>
    public static ToolEnvelope<SearchFacts?> Search(FolderSession session, SearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return Execute(session, request, cancellationToken);
        }
        catch (ToolRequestException ex)
        {
            return EnvelopeAssembler.Create<SearchFacts?>(session, null, session.Catalog.BuiltAt, [], [], [], [ex.Issue], cancellationToken);
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

        if (parsed.CountOnly)
        {
            var countFacts = new SearchFacts(request.ResourceType, request.Query ?? string.Empty, matches.Count, request.Offset, Returned: 0, Resources: null);
            return EnvelopeAssembler.Create<SearchFacts?>(session, countFacts, index.BuiltAt, [], omitted, [], issues, cancellationToken);
        }

        if (request.Offset > 0 && request.Offset >= matches.Count)
        {
            issues.Add(new IssueItem("information", "offset-beyond-total", $"offset {request.Offset} is at or beyond the {matches.Count} matches, so no resources are returned.", Location: null));
        }

        IReadOnlyList<CatalogEntry> page = [.. matches.Skip(request.Offset).Take(count).Select(match => match.Entry)];
        ResourceBatch collection = ResourceBatch.Collect(session.RootPath, page, parsed.Elements, cancellationToken);
        issues.AddRange(collection.Issues);

        int next = request.Offset + collection.Consumed;
        if (next < matches.Count)
        {
            string why = collection.Consumed < page.Count ? "size-budget" : "limit";
            omitted.Add(new OmittedItem($"{matches.Count - next} more matches (offset={next})", why));
        }

        var facts = new SearchFacts(request.ResourceType, request.Query ?? string.Empty, matches.Count, request.Offset, collection.Resources.Count, collection.Resources);
        return EnvelopeAssembler.Create<SearchFacts?>(session, facts, index.BuiltAt, collection.Evidence, omitted, [], issues, cancellationToken);
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
        IEnumerable<IndexedResource> candidates = index.Resources;
        foreach (IReadOnlySet<string> ids in parsed.IdFilters)
        {
            candidates = candidates.Where(resource => ids.Contains(resource.Key.Id));
        }

        List<IndexedResource> narrowed = [.. candidates];
        if (parsed.Options.Expression is not { } expression)
        {
            return narrowed;
        }

        HashSet<ResourceKey> matched = Evaluate(expression, narrowed);
        return [.. narrowed.Where(resource => matched.Contains(resource.Key))];
    }

    private static HashSet<ResourceKey> Evaluate(Ignixa.Search.Expressions.Expression expression, List<IndexedResource> candidates)
    {
        var interpreter = new ReferenceAwareInterpreter();
        try
        {
            SearchPredicate predicate = expression.AcceptVisitor(interpreter, interpreter.InitialContext);

            // The interpreter evaluates lazily, so unsupported expressions surface here, while enumerating.
            return [.. predicate(candidates.Select(resource => (resource.Key, resource.Index))).Select(match => match.Location)];
        }
        catch (SearchOperationNotSupportedException ex)
        {
            throw SearchFailures.From(ex);
        }
        catch (Exception ex) when (ex is NotImplementedException or NotSupportedException)
        {
            throw SearchFailures.NotSupported($"The query uses a feature the in-memory search does not implement: {ex.Message}");
        }
    }
}
