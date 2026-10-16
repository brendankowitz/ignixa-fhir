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
/// <param name="UnresolvedConditionals">The conditional references left unresolved, with why; absent when there were none.</param>
internal sealed record CachedResource(
    string Id,
    IReadOnlyList<CachedEntry> Entries,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Resolved = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? UnresolvedUrns = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<Indexing.UnresolvedConditional>? UnresolvedConditionals = null)
{
    /// <summary>The resolution as persisted; absent members mean nothing of that kind.</summary>
    public Indexing.ReferenceResolution ToResolution() =>
        Resolved is null && UnresolvedUrns is null && UnresolvedConditionals is null
            ? Indexing.ReferenceResolution.None
            : new Indexing.ReferenceResolution(Resolved ?? new Dictionary<string, string>(StringComparer.Ordinal), UnresolvedUrns ?? [], UnresolvedConditionals ?? []);

    /// <summary>The persisted form of a resource's index and <paramref name="resolution"/>: each resolution member only when it holds something, to keep files small.</summary>
    public static CachedResource From(string id, IReadOnlyList<CachedEntry> entries, Indexing.ReferenceResolution resolution) => new(
        id,
        entries,
        resolution.Resolved.Count == 0 ? null : resolution.Resolved,
        resolution.UnresolvedUrns.Count == 0 ? null : resolution.UnresolvedUrns,
        resolution.UnresolvedConditionals.Count == 0 ? null : resolution.UnresolvedConditionals);
}
