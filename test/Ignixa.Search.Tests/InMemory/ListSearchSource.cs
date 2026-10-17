// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Indexing;
using Ignixa.Search.InMemory;

namespace Ignixa.Search.Tests.InMemory;

/// <summary>An <see cref="IInMemorySearchSource"/> over resources already indexed, in the order given.</summary>
internal sealed class ListSearchSource(IReadOnlyList<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> resources) : IInMemorySearchSource
{
    public IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> Enumerate(string resourceType) =>
        resources.Where(resource => resource.Location.ResourceType == resourceType);

    public (ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)? Resolve(string resourceType, string id) =>
        resources.FirstOrDefault(resource => resource.Location.ResourceType == resourceType && resource.Location.Id == id) is { Location: not null } found
            ? found
            : null;
}
