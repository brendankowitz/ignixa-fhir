// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.CodeSystems;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Session;
using Ignixa.CliOnFhir.Terminology;
using Ignixa.CliOnFhir.Terminology.Tools;

namespace Ignixa.CliOnFhir.Inspection;

/// <summary>Builds the corpus overview an agent reads first.</summary>
public static class FolderInspector
{
    /// <summary>Diagnostics listed individually as issues; the rest are counted and reported as omitted.</summary>
    public const int DefaultIssueLimit = 20;

    /// <summary>
    /// <see cref="Inspect"/> after loading the terminology store when <paramref name="withTerminology"/> is set (which can
    /// download the default core package unless the session is offline), so the terminology section is computed.
    /// </summary>
    public static async Task<ToolEnvelope<FolderInspection>> InspectAsync(FolderSession session, int issueLimit, bool buildIndexes, bool withTerminology, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        List<IssueItem> loadIssues = [];
        if (withTerminology && EnvelopeAssembler.FolderProblem(session) is null)
        {
            TerminologyState state = await session.Terminology.GetAsync(cancellationToken);
            loadIssues.AddRange(state.IsUnusable ? state.FailureIssues().Select(issue => issue with { Severity = issue.Severity == "error" ? "warning" : issue.Severity }) : state.LoadIssues);
        }

        ToolEnvelope<FolderInspection> envelope = Inspect(session, issueLimit, buildIndexes, cancellationToken);
        return envelope with { Issues = [.. envelope.Issues, .. loadIssues] };
    }

    /// <summary>
    /// Summarizes the folder from its catalog alone; no index is built unless
    /// <paramref name="buildIndexes"/> is set, which exists to measure indexing cost, or the terminology store is loaded,
    /// when the terminology section is computed from every type's token index entries.
    /// </summary>
    public static ToolEnvelope<FolderInspection> Inspect(FolderSession session, int issueLimit = DefaultIssueLimit, bool buildIndexes = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegative(issueLimit);

        FolderCatalog catalog = session.Catalog;
        List<ResourceTypeCount> resourceTypes =
        [
            .. catalog.Entries
                .GroupBy(entry => entry.ResourceType, StringComparer.Ordinal)
                .Select(group => new ResourceTypeCount(group.Key, group.Count()))
                .OrderByDescending(count => count.Count)
                .ThenBy(count => count.ResourceType, StringComparer.Ordinal),
        ];

        List<CatalogDiagnostic> diagnostics = [.. catalog.Diagnostics];
        IndexFacts? indexFacts = null;
        DateTimeOffset builtAt = catalog.BuiltAt;
        if (buildIndexes)
        {
            List<ResourceTypeIndex> indexes = [.. resourceTypes.Select(count => session.GetIndex(count.ResourceType))];
            diagnostics.AddRange(indexes.SelectMany(index => index.Diagnostics));
            indexFacts = new IndexFacts(
                indexes.Sum(index => index.IndexEntryCount),
                session.IndexerLoadMilliseconds,
                indexes.Sum(index => index.ElapsedMilliseconds),
                session.CacheDirectory,
                [.. indexes.Select(index => new TypeIndexFacts(
                    index.ResourceType,
                    index.Resources.Count,
                    index.IndexEntryCount,
                    index.FromCache ? TypeIndexFacts.CacheSource : TypeIndexFacts.BuiltSource,
                    index.ElapsedMilliseconds,
                    index.CacheWriteMilliseconds))]);
            builtAt = indexes.Select(index => index.BuiltAt).Prepend(builtAt).Max();
        }

        List<OmittedItem> omitted = diagnostics.Count > issueLimit
            ? [new OmittedItem($"{diagnostics.Count - issueLimit} more diagnostics", "limit")]
            : [];

        TermFolderUsage terminology = TermFolderUsage.NotLoaded;
        if (session.Terminology.Current is { IsUnusable: false } state)
        {
            (terminology, OmittedItem? systemsOmitted) = FolderTerminology.Compute(session, state.Store, TermList.FolderUsageLimit);
            if (systemsOmitted is not null)
            {
                omitted.Add(systemsOmitted);
            }

            // The section was answered from every type's index.
            builtAt = session.BuiltIndexTypes.Select(type => session.GetIndex(type).BuiltAt).Prepend(builtAt).Max();
        }

        var facts = new FolderInspection(
            catalog.RootPath,
            session.Schema.Version.ToString(),
            catalog.FilesScanned,
            catalog.FilesSkipped,
            catalog.ContainerBundles,
            catalog.Entries.Count,
            resourceTypes.FirstOrDefault(count => count.ResourceType == "Patient")?.Count ?? 0,
            catalog.Entries.Count(entry => entry.HasSyntheticId),
            resourceTypes,
            diagnostics
                .GroupBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            session.CatalogStats,
            indexFacts,
            FolderCodeSystems.Summary(session, cancellationToken),
            terminology);

        // Structure only: the tool that lists every code system.
        List<NextCall> next = [new NextCall("folder_code_systems", new Dictionary<string, object> { ["offset"] = 0, ["count"] = 10 })];
        return EnvelopeAssembler.Create(session, facts, builtAt, [], omitted, [], diagnostics.Take(issueLimit).Select(EnvelopeAssembler.ToIssue), cancellationToken) with { Next = next };
    }
}
