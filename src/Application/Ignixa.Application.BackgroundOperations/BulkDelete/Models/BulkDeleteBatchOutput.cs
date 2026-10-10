// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Models;

/// <summary>
/// Output of one <see cref="Activities.BulkDeleteBatchActivity"/> page.
/// </summary>
/// <param name="DeletedCounts">Resources deleted by this batch, keyed by each resource's own type.</param>
/// <param name="HasMore">The search found more matches than the batch size.</param>
/// <param name="NextContinuationToken">Purge-history cursor for the next batch; null in restart mode.</param>
/// <param name="FirstMatchKey">
/// <c>Type/id</c> of the page's first match, or null when the page had none. In restart mode the
/// orchestration fails the job when two consecutive pages start with the same match, because that batch
/// made no progress and the loop would never end.
/// </param>
/// <param name="Superseded">
/// The job was already terminal (cancelled) before deleting or when recording progress. The orchestration
/// stops without finalizing the job.
/// </param>
public record BulkDeleteBatchOutput(
    Dictionary<string, long> DeletedCounts,
    bool HasMore,
    string? NextContinuationToken,
    string? FirstMatchKey,
    bool Superseded)
{
    /// <summary>
    /// Set when this instance's conformance staleness lease was not held, so the batch neither searched nor
    /// deleted: a delete selected with outgoing search-parameter definitions could remove the wrong set. The
    /// orchestration waits and runs the same batch again. A typed output rather than an exception, because
    /// DurableTask cannot rebuild an activity exception's type on the orchestrator side.
    /// </summary>
    public bool ConformanceStale { get; init; }

    public static BulkDeleteBatchOutput WaitForConformance() =>
        new([], HasMore: true, NextContinuationToken: null, FirstMatchKey: null, Superseded: false) { ConformanceStale = true };
}
