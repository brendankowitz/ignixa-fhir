// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace Ignixa.SqlOnFhir.Fabric.Delta;

/// <summary>
/// The writer was canceled after it began merging tables. It is an <see cref="OperationCanceledException"/>, so
/// cancellation handling sees it as one, and it also lists the tables committed before the cancellation was
/// observed. A merge the engine had started is never abandoned: it finishes and is listed here if it committed.
/// Replaying the same batch completes the rest.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "The exception exists to carry the committed tables; a constructor without them would lose them.")]
public sealed class FabricSilverWriteCanceledException : OperationCanceledException
{
    internal FabricSilverWriteCanceledException(
        IReadOnlyList<FabricSilverTableResult> completedTables,
        OperationCanceledException innerException,
        CancellationToken cancellationToken)
        : base(
            $"The batch was canceled after {completedTables.Count} table(s) were merged; replaying the batch completes the rest.",
            innerException,
            cancellationToken)
    {
        CompletedTables = completedTables;
    }

    /// <summary>The tables of the batch merged and committed before the cancellation, in batch order.</summary>
    public IReadOnlyList<FabricSilverTableResult> CompletedTables { get; }
}
