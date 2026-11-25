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
        Exception innerException)
        : base(Describe(tablePath, phase, attempts), innerException)
    {
        TablePath = tablePath;
        Phase = phase;
        Attempts = attempts;
        CompletedTables = completedTables;
    }

    /// <summary>The full path of the table that failed.</summary>
    public string TablePath { get; }

    /// <summary>The step that failed, which states what may already be written.</summary>
    public FabricSilverWritePhase Phase { get; }

    /// <summary>The merge attempts made on the failed table; zero outside <see cref="FabricSilverWritePhase.Merge"/> and <see cref="FabricSilverWritePhase.ReadCommit"/>.</summary>
    public int Attempts { get; }

    /// <summary>The tables of the batch merged and committed before the failure, in batch order.</summary>
    public IReadOnlyList<FabricSilverTableResult> CompletedTables { get; }

    private static string Describe(string tablePath, FabricSilverWritePhase phase, int attempts) => phase switch
    {
        FabricSilverWritePhase.Validate => $"Table '{tablePath}' could not be validated before writing; no table was changed.",
        FabricSilverWritePhase.Create => $"Table '{tablePath}' could not be created; no rows were written.",
        FabricSilverWritePhase.Merge => $"The merge into table '{tablePath}' failed after {attempts} attempt(s); it is unchanged, earlier tables of the batch are committed.",
        FabricSilverWritePhase.ReadCommit => $"The merge into table '{tablePath}' committed, but its commit could not be read back to report the result.",
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };
}
