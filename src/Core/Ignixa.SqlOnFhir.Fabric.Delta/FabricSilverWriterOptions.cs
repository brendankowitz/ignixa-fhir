// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// The fail-closed ceilings of <see cref="FabricSilverDeltaWriter"/>. A merge's peak memory was measured to grow with
/// the target table; that the engine reads the whole target into native memory whatever the source size is inferred,
/// not traced. The defaults bound one merge to the measured memory budget; raising them is at the caller's risk.
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
    /// growth (ADR 2611 ruling B3). Default 50,000, the largest table measured to merge within the memory budget: a 5,000-row merge
    /// into a 50,000-row table of realistic rows, in up to 100 files, peaked at 2,431 MiB of private bytes (commit
    /// charge) against a 4.5 GiB budget, measured on a single Windows host. A full 50,000-resource source (a result of about
    /// 100,000 rows) is permitted but unmeasured. Must be positive. A larger table fails with
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
    /// Merging never compacts: each merge adds files and rewrites only those it touches, so without compaction the
    /// file count tends to grow; that many small files raise a merge's memory is inferred, not measured. A table over
    /// the limit fails with
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

    /// <summary>Test seam: runs with the table path just before the engine merge starts, after the last cancellation check.</summary>
    internal Action<string>? BeforeEngineMerge { get; init; }

    /// <summary>Test seam: runs with the table path as soon as the engine merge returns; a throw stands for a failure after the commit.</summary>
    internal Action<string>? AfterEngineMerge { get; init; }

    /// <summary>Test seam: awaited with the table path just before the writer creates a table that had no Delta log.</summary>
    internal Func<string, Task>? BeforeCreateTable { get; init; }
}
