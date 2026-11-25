// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>The step of <see cref="FabricSilverDeltaWriter.WriteBatchAsync(IReadOnlyList{FabricSilverTableBatch}, FabricSilverIngestion, CancellationToken)"/> that failed.</summary>
public enum FabricSilverWritePhase
{
    /// <summary>
    /// Loading or checking an existing table before anything is written: its log, its match with the contract, or
    /// its size. No table has been changed.
    /// </summary>
    Validate,

    /// <summary>
    /// Creating a table that had no Delta log. Tables created earlier in the same call may hold an empty version 0,
    /// which a replay validates and reuses.
    /// </summary>
    Create,

    /// <summary>
    /// Merging a table's rows. Tables listed in <see cref="FabricSilverWriteException.CompletedTables"/> are
    /// committed and the tables after the failed one are unchanged. The failed table is unchanged unless
    /// <see cref="FabricSilverWriteException.MayHaveCommitted"/> is set; either way a replay is safe.
    /// </summary>
    Merge,

    /// <summary>
    /// Reading back the merge commit of the failed table to report its counts. That table <b>is</b> committed, like
    /// those in <see cref="FabricSilverWriteException.CompletedTables"/>; the tables after it are unchanged.
    /// </summary>
    ReadCommit,
}
