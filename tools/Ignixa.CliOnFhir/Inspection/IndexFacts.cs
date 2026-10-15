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
/// <param name="BuildMilliseconds">Wall time spent building the type indexes (they are built one after another), excluding the definitions load.</param>
/// <param name="ResourceTypes">Per-type figures, in the same order as the inspection's resource types.</param>
public sealed record IndexFacts(long IndexEntries, long? DefinitionsLoadMilliseconds, long BuildMilliseconds, IReadOnlyList<TypeIndexFacts> ResourceTypes);
