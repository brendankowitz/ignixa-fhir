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

    public static ToolEnvelope<FolderInspection> Inspect(FolderSession session, int issueLimit = DefaultIssueLimit)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegative(issueLimit);

        FolderIndex index = session.Index;
        List<CatalogDiagnostic> diagnostics = [.. index.Catalog.Diagnostics, .. index.Diagnostics];

        var facts = new FolderInspection(
            index.Catalog.RootPath,
            session.Schema.Version.ToString(),
            index.Catalog.FilesScanned,
            index.Catalog.FilesSkipped,
            index.Catalog.ContainerBundles,
            index.ResourceCount,
            index.ByResourceType.TryGetValue("Patient", out IReadOnlyList<IndexedResource>? patients) ? patients.Count : 0,
            index.ByResourceType.Values.Sum(list => list.Count(resource => resource.Entry.HasSyntheticId)),
            index.IndexEntryCount,
            [.. index.ByResourceType
                .Select(pair => new ResourceTypeCount(pair.Key, pair.Value.Count))
                .OrderByDescending(count => count.Count)
                .ThenBy(count => count.ResourceType, StringComparer.Ordinal)],
            diagnostics
                .GroupBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
            session.Stats);

        List<IssueItem> issues = [.. diagnostics.Take(issueLimit).Select(ToIssue)];
        List<OmittedItem> omitted = diagnostics.Count > issueLimit
            ? [new OmittedItem($"{diagnostics.Count - issueLimit} more diagnostics", "limit")]
            : [];

        return new ToolEnvelope<FolderInspection>(facts, [], omitted, issues, new FreshnessInfo(index.BuiltAt, StaleFiles: null));
    }

    private static IssueItem ToIssue(CatalogDiagnostic diagnostic) => new(
        diagnostic.Code == "duplicate-id" ? "warning" : "error",
        diagnostic.Code,
        diagnostic.Message,
        diagnostic.Line is int line ? $"{diagnostic.FilePath}:{line}" : diagnostic.FilePath);
}
