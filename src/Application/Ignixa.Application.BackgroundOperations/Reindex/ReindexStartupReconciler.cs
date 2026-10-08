using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class ReindexStartupReconciler(
    IMediator mediator,
    IBackgroundJobRepository<ReindexJobDefinition> repository,
    ReindexAutomationStateStore automationState,
    IOptions<ReindexOptions> options,
    ILogger<ReindexStartupReconciler> logger)
{
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.AutoStart)
        {
            logger.LogInformation(
                "Reindex: startup reconciliation skipped because AutoStart is false");
            return;
        }

        var requestedGeneration = await automationState.GetRequestedGenerationAsync(cancellationToken);
        var lastFailedOrCancelled = (await repository.ListAsync(
                (int)BackgroundJobType.Reindex,
                cancellationToken))
            .Where(job => job.Status is "Failed" or "Cancelled")
            .OrderByDescending(job => job.EndDate ?? job.CreateDate)
            .FirstOrDefault();
        if (lastFailedOrCancelled is not null &&
            requestedGeneration <= lastFailedOrCancelled.Definition.ConsumedGeneration)
        {
            logger.LogInformation(
                "Reindex: reconciliation skipped after {Status} job {JobId}; a new request generation is required",
                lastFailedOrCancelled.Status,
                lastFailedOrCancelled.JobId);
            return;
        }

        CreateReindexJobResult result;
        try
        {
            result = await mediator.SendAsync(
                new CreateReindexJobCommand { Trigger = "Reconciliation" },
                cancellationToken);
        }
        catch (Exception exception) when (ReindexTriggerUnavailableException.IsOperational(exception))
        {
            throw new ReindexTriggerUnavailableException(
                "Reindex reconciliation is temporarily unavailable.",
                exception);
        }
        switch (result)
        {
            case ReindexJobCreatedResult created:
                ReindexMetrics.TriggerStarted("Reconciliation");
                logger.LogInformation(
                    "Reindex: startup reconciliation started job {JobId}",
                    created.JobId);
                break;
            case ActiveReindexJobResult:
            case NoReindexWorkResult:
            case ReindexDisabledResult:
            case ReindexProviderUnavailableResult:
                break;
            default:
                throw new InvalidOperationException(
                    $"Reindex startup reconciliation returned unexpected result {result.GetType().Name}.");
        }
    }
}
