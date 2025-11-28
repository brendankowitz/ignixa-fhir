// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using EnsureThat;
using Ignixa.Search.Indexing;
using Ignixa.Abstractions;

namespace Ignixa.Search.InMemory;

/// <summary>
/// In-memory index for FHIR search functionality.
/// Stores search index entries grouped by resource type for efficient querying.
/// Ported from microsoft/fhir-server feature/subscription-engine branch.
/// </summary>
public class InMemoryIndex
{
    private readonly ISearchIndexer _searchIndexer;

    public InMemoryIndex(ISearchIndexer searchIndexer)
    {
        _searchIndexer = EnsureArg.IsNotNull(searchIndexer, nameof(searchIndexer));
        Index = new ConcurrentDictionary<string, List<(ResourceKey, IReadOnlyCollection<SearchIndexEntry>)>>();
    }

    /// <summary>
    /// Gets the internal index structure.
    /// Key = ResourceType, Value = List of (ResourceKey, SearchIndexEntries) tuples.
    /// </summary>
    internal ConcurrentDictionary<string, List<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)>> Index { get; }

    /// <summary>
    /// Indexes one or more resources, extracting search parameters and storing them in the index.
    /// </summary>
    /// <param name="resources">The resources to index</param>
    public void IndexResources(params IElement[] resources)
    {
        EnsureArg.IsNotNull(resources, nameof(resources));

        foreach (var resource in resources)
        {
            var indexEntries = _searchIndexer.Extract(resource);
            var resourceKey = ToResourceKey(resource);

            Index.AddOrUpdate(
                GetResourceType(resource),
                key => new List<(ResourceKey, IReadOnlyCollection<SearchIndexEntry>)> { (resourceKey, indexEntries) },
                (key, list) =>
                {
                    list.Add((resourceKey, indexEntries));
                    return list;
                });
        }
    }

    /// <summary>
    /// Gets all resources with their search indices for a specific resource type.
    /// </summary>
    /// <param name="resourceType">The resource type to query</param>
    /// <returns>Collection of resource keys with their search index entries</returns>
    [CLSCompliant(false)]
    public IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> GetResourcesWithIndices(string resourceType)
    {
        EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

        return Index.TryGetValue(resourceType, out var list)
            ? list
            : Enumerable.Empty<(ResourceKey, IReadOnlyCollection<SearchIndexEntry>)>();
    }

    private static ResourceKey ToResourceKey(IElement resource)
    {
        EnsureArg.IsNotNull(resource, nameof(resource));

        // Extract id from resource
        var idChildren = resource.Children("id");
        var idElement = idChildren.Count > 0 ? idChildren[0] : null;
        var id = idElement?.Value?.ToString() ?? throw new InvalidOperationException("Resource must have an id");

        // Extract versionId from meta.versionId if present
        var metaChildren = resource.Children("meta");
        var metaElement = metaChildren.Count > 0 ? metaChildren[0] : null;
        var versionIdChildren = metaElement?.Children("versionId");
        var versionIdElement = versionIdChildren?.Count > 0 ? versionIdChildren[0] : null;
        var versionId = versionIdElement?.Value?.ToString();

        return new ResourceKey(GetResourceType(resource), id, versionId);
    }

    private static string GetResourceType(IElement resource)
    {
        EnsureArg.IsNotNull(resource, nameof(resource));

        // For IElement, the resource type is in the InstanceType property
        return resource.InstanceType;
    }
}
