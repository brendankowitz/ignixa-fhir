// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// The fail-closed ceilings of <see cref="FabricSilverDeltaWriter"/>. A Delta MERGE reads its whole target table into
/// native memory whatever the source size, so the defaults bound one merge to the measured memory budget; raising
/// them is at the caller's risk.
/// </summary>
public sealed record FabricSilverWriterOptions
{
    private static readonly SemaphoreSlim ProcessMergeGate = new(1, 1);

    /// <summary>The most resources one table's batch may hold. Default 50,000; must be positive.</summary>
    public int MaxSourceResourcesPerTable
    {
        get;
        init => field = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxSourceResourcesPerTable), value, "The limit must be positive.");
    } = 50_000;

    /// <summary>
    /// The most rows a target table may hold before a merge: the Phase-2 limit pending the fix of the MERGE memory
    /// growth (B3). Default 50,000, the largest table measured to merge within the memory budget (about 2,800 to
    /// 3,600 MiB at one partition; 100,000 rows took about 5,400 MiB); must be positive. A larger table fails with
    /// <see cref="FabricSilverLimit.TargetRows"/>.
    /// </summary>
    /// <remarks>
    /// The count is read before the merge, so the check is advisory under concurrent writers. One merge can add up to
    /// <see cref="MaxSourceResourcesPerTable"/> rows, so a table can reach about
    /// <c>MaxTargetRows + MaxSourceResourcesPerTable</c> rows and then refuses writes until the limit is raised, which
    /// is at the caller's risk: a merge's peak memory grows with the target's row count.
    /// </remarks>
    public long MaxTargetRows
    {
        get;
        init => field = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxTargetRows), value, "The limit must be positive.");
    } = 50_000;

    /// <summary>
    /// The most data files a target table's current snapshot may hold before a merge. Default 100; must be positive.
    /// Merging never compacts: each merge adds files and rewrites only those it touches, so the count only grows,
    /// and many small files raise a merge's memory. A table over the limit fails with
    /// <see cref="FabricSilverLimit.TargetFiles"/> until it is compacted (OPTIMIZE); the writer never compacts.
    /// Raising the limit is at the caller's risk. Like the row count, the file count is advisory under concurrent writers.
    /// </summary>
    public int MaxTargetFiles
    {
        get;
        init => field = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxTargetFiles), value, "The limit must be positive.");
    } = 100;

    /// <summary>
    /// How many times one table's merge is tried when it loses a commit race to another writer. Default 5; must be
    /// positive. Other failures are never retried.
    /// </summary>
    public int MaxMergeAttempts
    {
        get;
        init => field = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxMergeAttempts), value, "The limit must be positive.");
    } = 5;

    /// <summary>
    /// Serializes merges: one merge's peak memory is the budget, so two at once would exceed it. Process-wide by
    /// default; tests substitute a private gate to race two writers in one process.
    /// </summary>
    internal SemaphoreSlim MergeGate { get; init; } = ProcessMergeGate;
}
