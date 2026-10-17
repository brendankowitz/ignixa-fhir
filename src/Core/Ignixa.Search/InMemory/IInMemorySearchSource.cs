// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Diagnostics.CodeAnalysis;
using Ignixa.Abstractions;
using Ignixa.Search.Indexing;

namespace Ignixa.Search.InMemory;

/// <summary>
/// The indexed resources an in-memory search reads, implemented by whoever holds them (a folder catalog, a
/// cached snapshot, an in-memory store). Chains, <c>_has</c> and <c>_revinclude</c> enumerate other types;
/// <c>_include</c> resolves the resources a match references.
/// </summary>
/// <remarks>
/// Implementations must return the same resources, in the same order, for as long as one search runs: search
/// results, sort ties and included resources follow that order, so it is what makes them repeatable. Index collections
/// may be fresh instances on each call; resources are told apart by <see cref="ResourceKey"/>.
/// Experimental (<c>IGNIXAEXP001</c>).
/// </remarks>
[CLSCompliant(false)]
[Experimental(InMemoryExperimental.DiagnosticId)]
public interface IInMemorySearchSource
{
    /// <summary>Every indexed resource of <paramref name="resourceType"/>; empty when there are none.</summary>
    IEnumerable<(ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)> Enumerate(string resourceType);

    /// <summary>The indexed resource <paramref name="resourceType"/>/<paramref name="id"/>, or null when the source holds none.</summary>
    (ResourceKey Location, IReadOnlyCollection<SearchIndexEntry> Index)? Resolve(string resourceType, string id);
}
