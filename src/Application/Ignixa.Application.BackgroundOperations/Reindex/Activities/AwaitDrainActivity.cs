using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class AwaitDrainActivity(
    IFhirRepositoryFactory repositoryFactory,
    TimeProvider timeProvider,
    ReindexProgressReporter progress,
    ILogger<AwaitDrainActivity> logger)
    : AsyncTaskActivity<AwaitDrainInput, AwaitDrainOutput>
{
    protected override async Task<AwaitDrainOutput> ExecuteAsync(
        TaskContext context,
        AwaitDrainInput input)
    {
        var repository = await repositoryFactory.GetRepositoryAsync(
            input.TenantId,
            CancellationToken.None);
        if (repository is not IReindexStore store)
        {
            throw new ReindexProviderNotSupportedException(input.TenantId);
        }

        var watermark = await store.GetVisibleWatermarkAsync(CancellationToken.None);
        var isDrained = watermark >= input.CutoffTransactionId;
        if (!isDrained &&
            timeProvider.GetUtcNow() - input.DrainStartedUtc >= input.DrainWarningAfter)
        {
            var oldest = await store.GetOldestIncompleteTransactionAsync(
                input.CutoffTransactionId,
                CancellationToken.None);
            logger.LogWarning(
                "Reindex: drain is still waiting for tenant {TenantId}; visible watermark {VisibleWatermark}, cutoff transaction {CutoffTransactionId}, oldest incomplete transaction {OldestTransactionId}, created {OldestCreateDate}, heartbeat {OldestHeartbeatDate}",
                input.TenantId,
                watermark,
                input.CutoffTransactionId,
                oldest?.TransactionId,
                oldest?.CreateDate,
                oldest?.HeartbeatDate);
        }

        var output = new AwaitDrainOutput(input.TenantId, isDrained, watermark);
        await progress.ReportDrainAsync(input.JobId, output, CancellationToken.None);
        if (isDrained)
        {
            ReindexMetrics.RecordDrainWait(timeProvider.GetUtcNow() - input.DrainStartedUtc);
        }

        return output;
    }
}
