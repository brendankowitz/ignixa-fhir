// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Reading;
using Ignixa.CliOnFhir.Search;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>
/// One page of a node's or a find's items, with the page's resources read within the 50 KB budget the way search reads them
/// (<see cref="ResourceBatch"/>): items stop where the resources stop, a resource too large to ever fit is named in
/// <c>omitted</c>, and <c>resolvedReferences</c> lists the Bundle references the returned resources show
/// (<see cref="ResolvedReferences"/>, same cap and omitted rules).
/// </summary>
internal static class ContextPage
{
    /// <summary>Stands in for an omitted item added after the batch is read, when reserving room for it.</summary>
    private static readonly OmittedItem Placeholder = new(new string('x', 96), "size-budget");

    /// <summary>The page's contents, before they are put in facts.</summary>
    /// <param name="Items">The returned items.</param>
    /// <param name="Resources">Their resources; null when the page carries none.</param>
    /// <param name="ResolvedReferences">The resolved Bundle references the resources show; null when none.</param>
    public sealed record Contents(
        IReadOnlyList<ContextItem> Items,
        IReadOnlyList<JsonNode>? Resources,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? ResolvedReferences);

    /// <summary>
    /// Builds the envelope for the page of <paramref name="entries"/> at <paramref name="offset"/>.
    /// </summary>
    /// <param name="withResources">Whether to return the page's resources; false for items that summarize several (lab codes).</param>
    /// <param name="facts">Makes the facts from the page's contents.</param>
    /// <param name="nextPage">The call that reads the page starting at the given offset.</param>
    /// <param name="elements">The caller's <c>elements</c> argument as given (<paramref name="projection"/> is its parse), so the calls in <c>next</c> keep it.</param>
    /// <param name="maxBytes">The caller's <c>maxBytes</c> argument as given; null for the default budget.</param>
    public static ToolEnvelope<TFacts?> Build<TFacts>(
        PatientContext context,
        IReadOnlyList<ContextEntry> entries,
        int offset,
        int count,
        ElementsProjection? projection,
        string? elements,
        int? maxBytes,
        bool withResources,
        Func<Contents, TFacts> facts,
        Func<int, NextCall> nextPage,
        List<IssueItem> issues,
        List<AbsentItem> absent,
        CancellationToken cancellationToken)
        where TFacts : class
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(nextPage);
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(absent);

        FolderSession session = context.Session;
        int budget = ResponseBudget.Resolve(maxBytes, issues);
        issues.AddRange(Paging.BeyondTotal(offset, entries.Count, "items"));
        List<ContextEntry> page = [.. entries.Skip(offset).Take(count)];
        List<OmittedItem> omitted = [];
        issues.AddRange(context.Issues(omitted, page.SelectMany(entry => entry.Sources)));
        if (projection is not null && !withResources)
        {
            issues.Add(new IssueItem("warning", "parameter-ignored", "'elements' was ignored because this node returns no resources.", Location: null));
        }
        else if (projection is not null)
        {
            foreach (string type in page.Select(entry => entry.Resource.ResourceType).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                issues.AddRange(projection.Validate(type));
            }
        }

        int consumed = page.Count;
        Contents contents = new([.. page.Select(entry => entry.Item)], null, null);
        if (withResources)
        {
            // The calls in next depend on whether the budget cuts the page, so Read first expects that it does not.
            int pageEnd = offset + page.Count;
            Func<bool, List<NextCall>> reserveNext = cut => cut || pageEnd < entries.Count
                ? After(nextPage, offset, pageEnd, entries.Count - offset, elements, maxBytes, cut, budget)
                : [];
            (contents, consumed) = Read(context, page, projection, facts, issues, absent, omitted, reserveNext, budget, cancellationToken);
        }

        List<NextCall> next = [];
        int resume = offset + consumed;
        if (resume < entries.Count)
        {
            bool cutBySize = consumed < page.Count;
            omitted.Add(new OmittedItem($"{entries.Count - resume} more items (offset={resume})", cutBySize ? "size-budget" : "limit"));
            next = withResources ? After(nextPage, offset, resume, entries.Count - resume, elements, maxBytes, cutBySize, budget) : [nextPage(resume)];
        }

        List<EvidenceItem> evidence = ContextRoot.Evidence(page.Take(consumed).SelectMany(entry => entry.Sources));
        absent.AddRange(page.Take(consumed).SelectMany(entry => entry.Absent).DistinctBy(item => item.What, StringComparer.Ordinal));
        DateTimeOffset builtAt = context.Compartment.IndexBuiltAt;
        return EnvelopeAssembler.Create<TFacts?>(session, facts(contents), builtAt, evidence, omitted, absent, issues, cancellationToken) with { Next = next };
    }

    private static (Contents Contents, int Consumed) Read<TFacts>(
        PatientContext context,
        List<ContextEntry> page,
        ElementsProjection? projection,
        Func<Contents, TFacts> facts,
        List<IssueItem> issues,
        List<AbsentItem> absent,
        List<OmittedItem> omitted,
        Func<bool, List<NextCall>> reserveNext,
        int budget,
        CancellationToken cancellationToken)
    {
        // The listed resolutions can only shrink once the resources are read, so each resource is charged for its candidates.
        List<IndexedResource> indexed = [.. page.Select(entry => context.Compartment.Indexed(entry.Resource))];
        Dictionary<string, Dictionary<string, string>> candidates = ResolvedReferences.Candidates(indexed, ResolvedReferences.Limit);
        int Reserved(bool cutBySize) => EnvelopeJson.SizeOf(new ToolEnvelope<TFacts>(
            facts(new Contents([.. page.Select(entry => entry.Item)], [], ResolvedReferences: null)),
            ContextRoot.Evidence(page.SelectMany(entry => entry.Sources)),
            [.. omitted, Placeholder, Placeholder],
            [.. absent, .. page.SelectMany(entry => entry.Absent)],
            issues,
            new FreshnessInfo(DateTimeOffset.MaxValue, StaleFiles: int.MaxValue))
        {
            Next = reserveNext(cutBySize),
        });

        // The reservation already holds every page item's evidence, which the batch counts again per resource: the
        // envelope's evidence is the items' sources, so the double count only errs towards a smaller page.
        List<CatalogEntry> entries = [.. page.Select(entry => entry.Resource)];
        Func<int, JsonObject, int> costs = ResolvedReferences.Charge(indexed);
        ResourceBatch batch = ResourceBatch.Collect(context.Session.RootPath, entries, projection, Reserved(cutBySize: false), budget, costs, cancellationToken);
        if (batch.Consumed < page.Count)
        {
            batch = ResourceBatch.Collect(context.Session.RootPath, entries, projection, Reserved(cutBySize: true), budget, costs, cancellationToken);
        }

        issues.AddRange(batch.Issues);
        omitted.AddRange(batch.Skipped);
        List<ContextEntry> returned = [.. page.Take(batch.Consumed)];
        (Dictionary<string, Dictionary<string, string>> shown, int notListed) = ResolvedReferences.Shown(indexed.Take(batch.Consumed), batch, candidates);
        if (notListed > 0)
        {
            omitted.Add(new OmittedItem($"{notListed} more resolved Bundle references in the returned resources (resolvedReferences lists at most {ResolvedReferences.Limit})", "limit"));
        }

        return (new Contents([.. returned.Select(entry => entry.Item)], batch.Resources, ResolvedReferences.ToFacts(shown)), batch.Consumed);
    }

    /// <summary>
    /// The calls that follow a page cut at <paramref name="resume"/>: the rest of the items with the caller's trimming and
    /// budget kept, then, when the page was cut by size or much remains, the same page trimmed with <c>elements</c>, or with the largest budget.
    /// </summary>
    private static List<NextCall> After(Func<int, NextCall> nextPage, int offset, int resume, int remaining, string? elements, int? maxBytes, bool cutBySize, int budget)
    {
        List<NextCall> calls = [TruncationCalls.Continue(nextPage(resume), resume, elements, maxBytes)];
        if (!cutBySize && (resume - offset < ResourceSearcher.DefaultCount || remaining < ResourceSearcher.DefaultCount))
        {
            return calls;
        }

        NextCall current = TruncationCalls.Continue(nextPage(offset), offset, elements, maxBytes);
        if (elements is null)
        {
            calls.Add(TruncationCalls.Trim(current, suggested: null));
        }

        if (cutBySize && budget < EnvelopeJson.MaxBytes)
        {
            calls.Add(TruncationCalls.Larger(current));
        }

        return calls;
    }
}
