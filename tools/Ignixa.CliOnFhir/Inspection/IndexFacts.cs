// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Inspection;

/// <summary>Cost and size of the search indexes, reported only when they were explicitly built.</summary>
/// <param name="IndexEntries">Search index entries extracted across all types.</param>
/// <param name="DefinitionsLoadMilliseconds">
/// One-time cost of loading the search parameter definitions and creating the indexer; null when no
/// index was built. It is not part of any per-type or total build time.
/// </param>
/// <param name="ElapsedMilliseconds">
/// Wall time spent obtaining the type indexes (one after another), whether built or loaded from the cache,
/// excluding the definitions load and any cache writes.
/// </param>
/// <param name="CacheDirectory">This folder's persisted-index directory; null when the cache is off.</param>
/// <param name="ResourceTypes">Per-type figures, in the same order as the inspection's resource types.</param>
public sealed record IndexFacts(long IndexEntries, long? DefinitionsLoadMilliseconds, long ElapsedMilliseconds, string? CacheDirectory, IReadOnlyList<TypeIndexFacts> ResourceTypes);
