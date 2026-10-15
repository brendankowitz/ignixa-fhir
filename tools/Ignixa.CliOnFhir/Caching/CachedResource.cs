// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Caching;

/// <summary>One indexed resource: its catalog id (the type is the file's) and its search index entries, in extraction order.</summary>
/// <param name="Id">The catalog id.</param>
/// <param name="Entries">The search index entries.</param>
/// <param name="Resolved">The Bundle references resolved before extraction (see <see cref="Indexing.ReferenceResolution"/>); absent when none were.</param>
/// <param name="UnresolvedUrns">The <c>urn:</c> references that named no entry; absent when there were none.</param>
internal sealed record CachedResource(
    string Id,
    IReadOnlyList<CachedEntry> Entries,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Resolved = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? UnresolvedUrns = null)
{
    /// <summary>The resolution as persisted; absent members mean nothing of that kind.</summary>
    public Indexing.ReferenceResolution ToResolution() =>
        Resolved is null && UnresolvedUrns is null
            ? Indexing.ReferenceResolution.None
            : new Indexing.ReferenceResolution(Resolved ?? new Dictionary<string, string>(StringComparer.Ordinal), UnresolvedUrns ?? []);

    /// <summary>The persisted form of <paramref name="resolution"/>: each member only when it holds something, to keep files small.</summary>
    public static (IReadOnlyDictionary<string, string>? Resolved, IReadOnlyList<string>? UnresolvedUrns) From(Indexing.ReferenceResolution resolution) =>
        (resolution.Resolved.Count == 0 ? null : resolution.Resolved, resolution.UnresolvedUrns.Count == 0 ? null : resolution.UnresolvedUrns);
}
