// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>What one call of the writer did to one table.</summary>
public sealed class FabricSilverTableResult
{
    internal FabricSilverTableResult(
        string tablePath,
        FabricSilverTableOutcome outcome,
        long? committedVersion,
        LatestRowReduction reduction,
        long insertedRows,
        long updatedRows,
        int attempts)
    {
        TablePath = tablePath;
        Outcome = outcome;
        CommittedVersion = committedVersion;
        InsertedRows = insertedRows;
        UpdatedRows = updatedRows;
        UnchangedRows = reduction.Rows.Count - insertedRows - updatedRows;
        DroppedRows = reduction.Dropped;
        CollapsedRows = reduction.Collapsed;
        Attempts = attempts;
    }

    /// <summary>The full path of the table.</summary>
    public string TablePath { get; }

    /// <summary>Whether this call created the table or found and validated an existing one.</summary>
    public FabricSilverTableOutcome Outcome { get; }

    /// <summary>
    /// The table version the merge committed, or <see langword="null"/> when nothing changed: the batch reduced to no
    /// rows, or every row was already present with an equal or newer <c>meta_lastUpdated</c>.
    /// </summary>
    public long? CommittedVersion { get; }

    /// <summary>Rows inserted because their id was new to the table.</summary>
    public long InsertedRows { get; }

    /// <summary>Rows that replaced an older row with the same id.</summary>
    public long UpdatedRows { get; }

    /// <summary>Rows left out because the table already held their id with an equal or newer <c>meta_lastUpdated</c>.</summary>
    public long UnchangedRows { get; }

    /// <summary>Resources left out because they have no <c>meta.lastUpdated</c>.</summary>
    public int DroppedRows { get; }

    /// <summary>Resources left out because a later or newer resource in the same batch has the same Silver id.</summary>
    public int CollapsedRows { get; }

    /// <summary>Merge attempts made; more than one means the merge lost commit races and was retried. Zero when the batch reduced to no rows.</summary>
    public int Attempts { get; }
}
