// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>The rows a batch keeps after reduction, in batch order, and how many it discarded and why.</summary>
/// <param name="Rows">One row per id, ordered by the kept row's position in the batch.</param>
/// <param name="Dropped">Rows without <c>meta_lastUpdated</c>.</param>
/// <param name="Collapsed">Rows superseded by a newer row with the same id, or a later one with equal <c>meta_lastUpdated</c>.</param>
internal sealed record LatestRowReduction(IReadOnlyList<FabricSilverRow> Rows, int Dropped, int Collapsed);
