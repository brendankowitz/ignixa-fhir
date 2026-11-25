// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// Reduces a batch to one row per Silver id before the MERGE, which requires it: a matched id with two source rows
/// fails the MERGE, and an unmatched one would be inserted twice. The row with the greatest
/// <c>meta_lastUpdated</c> wins, compared at the microsecond value the MERGE compares. POLICY: on a tie the later row
/// in the batch wins; healthcare data solutions leaves ties nondeterministic, so this is an Ignixa choice that makes
/// replays reproducible. A row without <c>meta_lastUpdated</c> is dropped, because the MERGE could neither insert nor
/// update it; that the HDS ordering (nulls last, never inserted) does the same is inferred, not tested.
/// </summary>
internal static class LatestRowReducer
{
    public static LatestRowReduction Reduce(IReadOnlyList<FabricSilverRow> rows, int lastUpdatedOrdinal)
    {
        var winners = new Dictionary<string, (int Position, long LastUpdated)>(StringComparer.Ordinal);
        var dropped = 0;
        for (var position = 0; position < rows.Count; position++)
        {
            var cell = rows[position].Cells[lastUpdatedOrdinal];
            if (cell.ValueKind == System.Text.Json.JsonValueKind.Null)
            {
                dropped++;
                continue;
            }

            var lastUpdated = SilverTransitValues.TimestampMicroseconds(cell, HdsColumns.MetaLastUpdated);
            var id = rows[position].Id;
            if (!winners.TryGetValue(id, out var current) || lastUpdated >= current.LastUpdated)
            {
                winners[id] = (position, lastUpdated);
            }
        }

        var kept = winners.Values.Select(winner => winner.Position).Order().Select(position => rows[position]).ToList();
        return new LatestRowReduction(kept, dropped, rows.Count - dropped - kept.Count);
    }
}
