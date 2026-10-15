// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Inspection;

/// <summary>Cost and size of the search indexes, reported only when they were explicitly built.</summary>
/// <param name="IndexEntries">Search index entries extracted across all types.</param>
/// <param name="BuildMilliseconds">Wall time spent building the type indexes (they are built one after another).</param>
/// <param name="ResourceTypes">Per-type figures, in the same order as the inspection's resource types.</param>
public sealed record IndexFacts(long IndexEntries, long BuildMilliseconds, IReadOnlyList<TypeIndexFacts> ResourceTypes);
