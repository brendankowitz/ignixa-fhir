// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Search.Indexing;
using Ignixa.Search.InMemory;

namespace Ignixa.CliOnFhir.Search;

/// <summary>A search source with no resources, for compiling a query without evaluating it over data.</summary>
internal sealed class EmptySearchSource : IInMemorySearchSource
{
    public static EmptySearchSource Instance { get; } = new();

    public IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> Enumerate(string resourceType) => [];

    public (ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)? Resolve(string resourceType, string id) => null;
}
