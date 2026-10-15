// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Session;

namespace Ignixa.CliOnFhir.Inspection;

/// <summary>Builds the corpus overview an agent reads first.</summary>
public static class FolderInspector
{
    /// <summary>Diagnostics listed individually as issues; the rest are counted and reported as omitted.</summary>
    public const int DefaultIssueLimit = 20;

    /// <summary>
    /// Summarizes the folder from its catalog alone; no index is built unless
    /// <paramref name="buildIndexes"/> is set, which exists to measure indexing cost.
    /// </summary>
    public static ToolEnvelope<FolderInspection> Inspect(FolderSession session, int issueLimit = DefaultIssueLimit, bool buildIndexes = false)
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
            indexFacts);

        List<IssueItem> issues = [.. diagnostics.Take(issueLimit).Select(ToIssue), .. session.CacheIssues];
        List<OmittedItem> omitted = diagnostics.Count > issueLimit
            ? [new OmittedItem($"{diagnostics.Count - issueLimit} more diagnostics", "limit")]
            : [];

        return new ToolEnvelope<FolderInspection>(facts, [], omitted, issues, new FreshnessInfo(builtAt, session.CountStaleFiles()));
    }

    private static IssueItem ToIssue(CatalogDiagnostic diagnostic) => new(
        diagnostic.Code == "duplicate-id" ? "warning" : "error",
        diagnostic.Code,
        diagnostic.Message,
        diagnostic.Line is int line ? $"{diagnostic.FilePath}:{line}" : diagnostic.FilePath);
}
