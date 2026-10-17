// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Session;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.CliOnFhir.Reading;

/// <summary>
/// The versions of a document: the folder's DocumentReferences joined to it through <c>relatesTo</c> (replaces, appends,
/// transforms, signs), in either direction and transitively, in <c>date</c> order. A shell reading one note does not see that
/// a later one replaced it; this puts the whole chain beside the one read.
/// </summary>
internal static class DocumentVersions
{
    /// <summary>Most documents listed in one chain; a longer chain lists those nearest the one read.</summary>
    public const int MaxVersions = 10;

    /// <summary>Most documents walked in one group; a group is rarely more than a handful.</summary>
    private const int MaxGroup = 1000;

    private const string DocumentReference = "DocumentReference";

    /// <summary>
    /// The chain of each DocumentReference in <paramref name="read"/> that relates to at least one other, each chain once, in
    /// the order of the first document read from it.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<DocumentVersion>> Chains(FolderSession session, IEnumerable<CatalogEntry> read)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(read);

        List<CatalogEntry> documents = [.. read.Where(entry => entry.ResourceType == DocumentReference)];
        if (documents.Count == 0)
        {
            return [];
        }

        Dictionary<string, List<string>> relatedFrom = RelatedFrom(session.GetIndex(DocumentReference));
        Dictionary<string, DocumentVersion?> versions = new(StringComparer.Ordinal);
        HashSet<string> listed = new(StringComparer.Ordinal);
        List<IReadOnlyList<DocumentVersion>> chains = [];
        foreach (CatalogEntry document in documents)
        {
            if (listed.Contains(document.Id))
            {
                continue;
            }

            (List<DocumentVersion> chain, IReadOnlySet<string> group) = Walk(session, document.Id, relatedFrom, versions);
            listed.UnionWith(group);
            if (chain.Count > 1)
            {
                chains.Add([.. chain.OrderBy(version => version.Instant ?? DateTimeOffset.MaxValue).ThenBy(version => version.Id, StringComparer.Ordinal)]);
            }
        }

        return chains;
    }

    /// <summary>
    /// The documents reachable from <paramref name="start"/> by relatesTo either way, nearest first, at most
    /// <see cref="MaxVersions"/>, and the ids of the whole group (at most <see cref="MaxGroup"/>), so a document of a group already
    /// listed is not listed again from another starting point.
    /// </summary>
    private static (List<DocumentVersion> Chain, IReadOnlySet<string> Group) Walk(FolderSession session, string start, Dictionary<string, List<string>> relatedFrom, Dictionary<string, DocumentVersion?> versions)
    {
        List<DocumentVersion> chain = [];
        HashSet<string> seen = new(StringComparer.Ordinal) { start };
        Queue<string> next = new([start]);
        while (next.Count > 0 && seen.Count <= MaxGroup)
        {
            string id = next.Dequeue();
            if (Version(session, id, versions) is not { } version)
            {
                continue;
            }

            if (chain.Count < MaxVersions)
            {
                chain.Add(version);
            }

            IEnumerable<string> neighbours = version.Targets.Concat(relatedFrom.GetValueOrDefault(id) ?? []);
            foreach (string neighbour in neighbours.Where(seen.Add))
            {
                next.Enqueue(neighbour);
            }
        }

        return (chain, seen);
    }

    private static DocumentVersion? Version(FolderSession session, string id, Dictionary<string, DocumentVersion?> versions)
    {
        if (!versions.TryGetValue(id, out DocumentVersion? version))
        {
            version = session.FindEntry(DocumentReference, id) is { } entry && ResourceReader.Read(session.RootPath, entry).Resource is { } resource
                ? DocumentVersion.From(id, resource)
                : null;
            versions[id] = version;
        }

        return version;
    }

    /// <summary>For each DocumentReference id, the ids of the DocumentReferences whose relatesTo targets it, by the index's <c>relatesto</c> entries.</summary>
    private static Dictionary<string, List<string>> RelatedFrom(ResourceTypeIndex index)
    {
        Dictionary<string, List<string>> relatedFrom = new(StringComparer.Ordinal);
        foreach (IndexedResource resource in index.Resources)
        {
            foreach (SearchIndexEntry entry in resource.Index)
            {
                if (entry.SearchParameter.Code == "relatesto"
                    && entry.Value is ReferenceSearchValue { BaseUri: null, ResourceType: DocumentReference } target
                    && target.ResourceId != resource.Key.Id)
                {
                    if (!relatedFrom.TryGetValue(target.ResourceId, out List<string>? sources))
                    {
                        relatedFrom[target.ResourceId] = sources = [];
                    }

                    sources.Add(resource.Key.Id);
                }
            }
        }

        return relatedFrom;
    }
}
