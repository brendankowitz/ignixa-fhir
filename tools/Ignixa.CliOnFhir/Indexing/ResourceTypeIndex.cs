// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>
/// In-memory search index over the catalogued resources of one resource type.
/// </summary>
/// <param name="ResourceType">The indexed resource type.</param>
/// <param name="Resources">Indexed resources, in catalog order. Empty when the folder holds none of this type.</param>
/// <param name="Diagnostics">Resources that could not be parsed, and search parameters that could not be extracted.</param>
/// <param name="BuiltAt">When the index was built; for a cached index, when the process that built it finished.</param>
/// <param name="ElapsedMilliseconds">
/// Wall time this process spent obtaining the index: building it, or loading and rebinding it from the cache.
/// Excludes the one-time indexer load (<see cref="Session.FolderSession.IndexerLoadMilliseconds"/>) and the cache write.
/// </param>
/// <param name="FromCache">True when the index was loaded from the persisted cache rather than built in this process.</param>
/// <param name="CacheWriteMilliseconds">Wall time spent persisting a freshly built index; null when it was loaded from cache or not persisted.</param>
public sealed record ResourceTypeIndex(
    string ResourceType,
    IReadOnlyList<IndexedResource> Resources,
    IReadOnlyList<CatalogDiagnostic> Diagnostics,
    DateTimeOffset BuiltAt,
    long ElapsedMilliseconds,
    bool FromCache,
    long? CacheWriteMilliseconds = null)
{
    public long IndexEntryCount => Resources.Sum(resource => (long)resource.Index.Count);
}
