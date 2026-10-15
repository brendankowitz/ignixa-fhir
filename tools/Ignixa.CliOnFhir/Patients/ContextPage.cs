// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
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
    public static ToolEnvelope<TFacts?> Build<TFacts>(
        PatientContext context,
        IReadOnlyList<ContextEntry> entries,
        int offset,
        int count,
        ElementsProjection? projection,
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
        issues.AddRange(Paging.BeyondTotal(offset, entries.Count, "items"));
        List<ContextEntry> page = [.. entries.Skip(offset).Take(count)];
        List<OmittedItem> omitted = [];
        issues.AddRange(context.Issues(omitted));
        if (projection is not null && !withResources)
        {
            issues.Add(new IssueItem("warning", "parameter-ignored", "'elements' was ignored because this node returns no resources.", Location: null));
        }
        else if (projection is not null)
        {
            foreach (string type in page.Select(entry => entry.Resource.Key.ResourceType).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                issues.AddRange(projection.Validate(type));
            }
        }

        int consumed = page.Count;
        Contents contents = new([.. page.Select(entry => entry.Item)], null, null);
        if (withResources)
        {
            (contents, consumed) = Read(context, page, projection, facts, issues, absent, omitted, cancellationToken);
        }

        List<NextCall> next = [];
        int resume = offset + consumed;
        if (resume < entries.Count)
        {
            omitted.Add(new OmittedItem($"{entries.Count - resume} more items (offset={resume})", consumed < page.Count ? "size-budget" : "limit"));
            next.Add(nextPage(resume));
        }

        List<EvidenceItem> evidence = ContextRoot.Evidence(page.Take(consumed).SelectMany(entry => entry.Sources));
        absent.AddRange(page.Take(consumed).SelectMany(entry => entry.Absent).DistinctBy(item => item.What, StringComparer.Ordinal));
        DateTimeOffset builtAt = context.Compartment.Folder.IndexesRead.Max(index => index.BuiltAt);
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
        CancellationToken cancellationToken)
    {
        // The listed resolutions can only shrink once the resources are read, so reserving the candidates keeps the cap.
        Dictionary<string, Dictionary<string, string>> candidates = ResolvedReferences.Candidates(page.Select(entry => entry.Resource), ResolvedReferences.Limit);
        int reserved = EnvelopeJson.SizeOf(new ToolEnvelope<TFacts>(
            facts(new Contents([.. page.Select(entry => entry.Item)], [], Facts(candidates))),
            ContextRoot.Evidence(page.SelectMany(entry => entry.Sources)),
            [.. omitted, Placeholder, Placeholder],
            [.. absent, .. page.SelectMany(entry => entry.Absent)],
            issues,
            new FreshnessInfo(DateTimeOffset.MaxValue, StaleFiles: int.MaxValue))
        {
            Next = [ContextRoot.Expand(context, new string('x', 96), int.MaxValue)],
        });

        // The reservation already holds every page item's evidence, which the batch counts again per resource: the
        // envelope's evidence is the items' sources, so the double count only errs towards a smaller page.
        ResourceBatch batch = ResourceBatch.Collect(context.Session.RootPath, [.. page.Select(entry => entry.Resource.Entry)], projection, reserved, cancellationToken);
        issues.AddRange(batch.Issues);
        omitted.AddRange(batch.Skipped);
        List<ContextEntry> returned = [.. page.Take(batch.Consumed)];
        (Dictionary<string, Dictionary<string, string>> shown, int notListed) = ResolvedReferences.Shown(returned.Select(entry => entry.Resource), batch, candidates);
        if (notListed > 0)
        {
            omitted.Add(new OmittedItem($"{notListed} more resolved Bundle references in the returned resources (resolvedReferences lists at most {ResolvedReferences.Limit})", "limit"));
        }

        return (new Contents([.. returned.Select(entry => entry.Item)], batch.Resources, Facts(shown)), batch.Consumed);
    }

    /// <summary>
    /// The facts form of a resolved-references map: null when it lists nothing, so the member is left out. Resources and
    /// targets are named as summaries name them (<see cref="FhirIds.Reference(string, string)"/>); the references as written
    /// are those the returned JSON shows.
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Facts(Dictionary<string, Dictionary<string, string>> resolved) =>
        resolved.Count == 0 ? null : resolved.ToDictionary(
            pair => Safe(pair.Key),
            pair => (IReadOnlyDictionary<string, string>)pair.Value.ToDictionary(reference => reference.Key, reference => Safe(reference.Value), StringComparer.Ordinal),
            StringComparer.Ordinal);

    /// <summary>A <c>Type/id</c> the index produced, with the id made safe.</summary>
    private static string Safe(string reference) =>
        reference.IndexOf('/', StringComparison.Ordinal) is int slash and > 0 ? FhirIds.Reference(reference[..slash], reference[(slash + 1)..]) : FhirIds.Safe(reference);
}
