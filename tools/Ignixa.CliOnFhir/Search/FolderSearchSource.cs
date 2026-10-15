// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Session;
using Ignixa.Search.Indexing;
using Ignixa.Search.InMemory;

namespace Ignixa.CliOnFhir.Search;

/// <summary>
/// The folder's per-type indexes as the source of an in-memory search. A type's index is built (or loaded from the
/// cache) only when the search first reads that type, and a reference is resolved through the catalog first, so a
/// reference to a resource the folder does not hold never builds an index. Created for one search; not thread-safe.
/// </summary>
internal sealed class FolderSearchSource(FolderSession session) : IInMemorySearchSource
{
    private readonly Dictionary<string, (ResourceTypeIndex Index, Dictionary<string, IndexedResource> ById)> _types = new(StringComparer.Ordinal);

    private readonly List<ResourceTypeIndex> _read = [];

    /// <summary>The indexes this search read, in the order it first read them.</summary>
    public IReadOnlyList<ResourceTypeIndex> IndexesRead => _read;

    public IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> Enumerate(string resourceType) =>
        Type(resourceType).Index.Resources.Select(resource => (resource.Key, resource.Index));

    public (ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)? Resolve(string resourceType, string id)
    {
        if (!session.Schema.ResourceTypeNames.Contains(resourceType) || session.FindEntry(resourceType, id) is null)
        {
            return null;
        }

        return Type(resourceType).ById.TryGetValue(id, out IndexedResource? resource) ? (resource.Key, resource.Index) : null;
    }

    /// <summary>The indexed resource behind a location this source returned.</summary>
    public IndexedResource Indexed(ResourceKey location) => Type(location.ResourceType).ById[location.Id];

    /// <summary>The type's index, built or loaded on first use, with its resources by id (the catalog holds one per type and id).</summary>
    private (ResourceTypeIndex Index, Dictionary<string, IndexedResource> ById) Type(string resourceType)
    {
        if (!_types.TryGetValue(resourceType, out (ResourceTypeIndex Index, Dictionary<string, IndexedResource> ById) type))
        {
            ResourceTypeIndex index = session.GetIndex(resourceType);
            type = (index, index.Resources.ToDictionary(resource => resource.Key.Id, StringComparer.Ordinal));
            _types.Add(resourceType, type);
            _read.Add(index);
        }

        return type;
    }
}
