using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class AwaitDrainActivity(
    IFhirRepositoryFactory repositoryFactory,
    TimeProvider timeProvider,
    ReindexActivityHeartbeat heartbeat,
    ILogger<AwaitDrainActivity> logger)
    : AsyncTaskActivity<AwaitDrainInput, AwaitDrainOutput>
{
    protected override Task<AwaitDrainOutput> ExecuteAsync(
        TaskContext context,
        AwaitDrainInput input) =>
        heartbeat.RunAsync(input.JobId, cancellationToken => DrainAsync(input, cancellationToken), CancellationToken.None);

    private async Task<AwaitDrainOutput> DrainAsync(AwaitDrainInput input, CancellationToken cancellationToken)
    {
        var repository = await repositoryFactory.GetRepositoryAsync(
            input.TenantId,
            cancellationToken);
        if (repository is not IReindexStore store)
        {
            throw new ReindexProviderNotSupportedException(input.TenantId);
        }

        var watermark = await store.GetVisibleWatermarkAsync(cancellationToken);
        var oldest = await store.GetOldestIncompleteTransactionAsync(
            input.CutoffTransactionId,
            cancellationToken);
        var isDrained = oldest is null;
        if (oldest is not null &&
            timeProvider.GetUtcNow() - input.DrainStartedUtc >= input.DrainWarningAfter)
        {
            logger.LogWarning(
                "Reindex: drain is still waiting for tenant {TenantId}; visible watermark {VisibleWatermark}, cutoff transaction {CutoffTransactionId}, oldest incomplete transaction {OldestTransactionId}, created {OldestCreateDate}, heartbeat {OldestHeartbeatDate}",
                input.TenantId,
                watermark,
                input.CutoffTransactionId,
                oldest.Value.TransactionId,
                oldest.Value.CreateDate,
                oldest.Value.HeartbeatDate);
        }

        if (oldest is not null &&
            input.StaleJobTimeout > TimeSpan.Zero &&
            input.DrainElapsed >= input.StaleJobTimeout)
        {
            logger.LogError(
                "Reindex: drain timed out for tenant {TenantId}; cutoff transaction {CutoffTransactionId}, oldest incomplete transaction {OldestTransactionId}",
                input.TenantId,
                input.CutoffTransactionId,
                oldest.Value.TransactionId);
        }

        var output = new AwaitDrainOutput(input.TenantId, isDrained, watermark);
        if (isDrained)
        {
            ReindexMetrics.RecordDrainWait(timeProvider.GetUtcNow() - input.DrainStartedUtc);
        }

        return output;
    }
}
