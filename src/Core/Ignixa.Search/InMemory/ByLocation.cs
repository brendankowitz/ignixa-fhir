// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Ignixa.Search.Indexing;

namespace Ignixa.Search.InMemory;

/// <summary>
/// Compares indexed resources by <see cref="ResourceKey"/> alone, so combining predicate results (AND, OR, NOT) does
/// not depend on a source handing back the same index collection instance on every enumeration.
/// </summary>
internal sealed class ByLocation : IEqualityComparer<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)>
{
    public static ByLocation Instance { get; } = new();

    public bool Equals((ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index) x, (ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index) y) =>
        EqualityComparer<ResourceKey>.Default.Equals(x.Location, y.Location);

    public int GetHashCode((ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index) obj) =>
        obj.Location?.GetHashCode() ?? 0;
}
