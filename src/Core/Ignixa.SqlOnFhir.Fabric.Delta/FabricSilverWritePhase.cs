// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>The step of <see cref="FabricSilverDeltaWriter.WriteBatchAsync(IReadOnlyList{FabricSilverTableBatch}, FabricSilverIngestion, CancellationToken)"/> that failed.</summary>
public enum FabricSilverWritePhase
{
    /// <summary>
    /// Loading or checking an existing table before any merge: its log, its match with the contract, or its size. This
    /// includes a table another creator won the race to create, when its log cannot be loaded or it does not match the
    /// contract. This call merged no rows; an empty version 0 may have been created, for this table by a creator that
    /// won the race or for an earlier table of the same call.
    /// </summary>
    Validate,

    /// <summary>
    /// Creating a table that had no Delta log, for any failure other than the race winner's table failing to load or
    /// to match the contract (<see cref="Validate"/>). This call merged no rows into it. Tables created earlier in the
    /// same call may hold an empty version 0, which a replay validates and reuses.
    /// </summary>
    Create,

    /// <summary>
    /// Merging a table's rows. Tables listed in <see cref="FabricSilverWriteException.CompletedTables"/> are
    /// completed (committed, or unchanged with no new version) and the tables after the failed one are unchanged. The failed table is unchanged unless
    /// <see cref="FabricSilverWriteException.MayHaveCommitted"/> is set; either way a replay is safe.
    /// </summary>
    Merge,

    /// <summary>
    /// Reading back the merge commit of the failed table to report its counts. That table <b>is</b> committed, like
    /// those in <see cref="FabricSilverWriteException.CompletedTables"/>; the tables after it are unchanged.
    /// </summary>
    ReadCommit,
}
