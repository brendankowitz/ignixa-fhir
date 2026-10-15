using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

/// <summary>
/// One drain poll: returns whether no incomplete transaction remains at or below the tenant's cutoff. The
/// orchestration owns the wait and its timeout; this only warns once the drain has waited past
/// <see cref="AwaitDrainInput.DrainWarningAfter"/>.
/// </summary>
public sealed class AwaitDrainActivity(
    IReindexStoreFactory reindexStoreFactory,
    TimeProvider timeProvider,
    ReindexProgressReporter progress,
    ILogger<AwaitDrainActivity> logger)
    : AsyncTaskActivity<AwaitDrainInput, bool>
{
    protected override Task<bool> ExecuteAsync(
        TaskContext context,
        AwaitDrainInput input) =>
        progress.RunWithHeartbeatAsync(
            input.JobId, cancellationToken => DrainAsync(input, cancellationToken), CancellationToken.None);

    private async Task<bool> DrainAsync(AwaitDrainInput input, CancellationToken cancellationToken)
    {
        var store = await reindexStoreFactory.GetReindexStoreAsync(
            input.TenantId,
            cancellationToken);

        var oldest = await store.GetOldestIncompleteTransactionAsync(
            input.CutoffTransactionId,
            cancellationToken);
        var waited = timeProvider.GetUtcNow() - input.DrainStartedUtc;
        if (oldest is null)
        {
            ReindexMetrics.RecordDrainWait(waited);
            return true;
        }

        if (waited >= input.DrainWarningAfter)
        {
            // The watermark is diagnostic only; the drain decision is the oldest incomplete transaction.
            var watermark = await store.GetVisibleWatermarkAsync(cancellationToken);
            logger.LogWarning(
                "Reindex: drain is still waiting for tenant {TenantId}; visible watermark {VisibleWatermark}, cutoff transaction {CutoffTransactionId}, oldest incomplete transaction {OldestTransactionId}, created {OldestCreateDate}, heartbeat {OldestHeartbeatDate}",
                input.TenantId,
                watermark,
                input.CutoffTransactionId,
                oldest.TransactionId,
                oldest.CreateDate,
                oldest.HeartbeatDate);
        }

        return false;
    }
}
