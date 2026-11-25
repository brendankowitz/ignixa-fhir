// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// The Delta engine or the file system failed while the writer validated, created or merged a table.
/// <see cref="Phase"/> states which tables may have changed; replaying the same batch is safe and repairs a partial
/// write, because the merge only replaces older rows.
/// </summary>
/// <remarks>
/// The message is fixed text naming the table and the phase. The engine's own error is kept as
/// <see cref="Exception.InnerException"/>; engine messages can name data files and row positions, so log the inner
/// exception with the same care as the table's data.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "Every write failure names its table and phase; a parameterless or message-only constructor would create one that does not.")]
public sealed class FabricSilverWriteException : Exception
{
    internal FabricSilverWriteException(
        string tablePath,
        FabricSilverWritePhase phase,
        int attempts,
        IReadOnlyList<FabricSilverTableResult> completedTables,
        Exception innerException,
        bool mayHaveCommitted = false,
        Exception? versionCheckException = null)
        : base(Describe(tablePath, phase, attempts), innerException)
    {
        TablePath = tablePath;
        Phase = phase;
        Attempts = attempts;
        CompletedTables = completedTables;
        MayHaveCommitted = mayHaveCommitted || versionCheckException is not null || phase == FabricSilverWritePhase.ReadCommit;
        VersionCheckException = versionCheckException;
    }

    /// <summary>The full path of the table that failed.</summary>
    public string TablePath { get; }

    /// <summary>The step that failed, which states what may already be written.</summary>
    public FabricSilverWritePhase Phase { get; }

    /// <summary>The merge attempts made on the failed table; zero outside <see cref="FabricSilverWritePhase.Merge"/> and <see cref="FabricSilverWritePhase.ReadCommit"/>.</summary>
    public int Attempts { get; }

    /// <summary>
    /// Whether the failed table may hold this batch's rows. <see langword="false"/> means it is proven unchanged by
    /// this call. In <see cref="FabricSilverWritePhase.Merge"/> it is <see langword="true"/> when the table's version
    /// advanced during the failed merge (an engine step after the commit failed, or another writer committed) or the
    /// version could not be read again (<see cref="VersionCheckException"/> then says why); it is always <see langword="true"/> in
    /// <see cref="FabricSilverWritePhase.ReadCommit"/>. A table that may have committed has no row counts, so it is
    /// not in <see cref="CompletedTables"/>.
    /// </summary>
    public bool MayHaveCommitted { get; }

    /// <summary>
    /// Why the failed table's version could not be read again after a failed merge, or <see langword="null"/> when it
    /// was read or no read was needed. Only <see cref="FabricSilverWritePhase.Merge"/> sets it, and when it is set
    /// <see cref="MayHaveCommitted"/> is <see langword="true"/>, because the table could not be proven unchanged.
    /// <see cref="Exception.InnerException"/> stays the merge's own failure. Like that inner exception, it can name
    /// data files, so log it with the same care as the table's data.
    /// </summary>
    public Exception? VersionCheckException { get; }

    /// <summary>The tables of the batch merged and committed before the failure, in batch order.</summary>
    public IReadOnlyList<FabricSilverTableResult> CompletedTables { get; }

    private static string Describe(string tablePath, FabricSilverWritePhase phase, int attempts) => phase switch
    {
        FabricSilverWritePhase.Validate => $"Table '{tablePath}' could not be validated before writing; no merge ran, but an empty version 0 may have been created.",
        FabricSilverWritePhase.Create => $"Table '{tablePath}' could not be created; no rows were written.",
        FabricSilverWritePhase.Merge => $"The merge into table '{tablePath}' failed after {attempts} attempt(s); the table may or may not be committed (see MayHaveCommitted) and earlier tables of the batch are committed. Replaying the batch is safe.",
        FabricSilverWritePhase.ReadCommit => $"The merge into table '{tablePath}' committed, but its commit could not be read back to report the result.",
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };
}
