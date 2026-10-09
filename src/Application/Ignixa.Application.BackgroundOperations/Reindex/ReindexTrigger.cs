using Ignixa.Application.Features.Conformance;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class ReindexTrigger(
    IMediator mediator,
    IOptions<ReindexOptions> options,
    ILogger<ReindexTrigger> logger) : IReindexTrigger
{
    public async Task<ReindexTriggerResult> RequestReindexAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        if (!options.Value.AutoStart)
        {
            logger.LogInformation(
                "Reindex: automatic trigger ignored because AutoStart is false; reason {Reason}",
                reason);
            return new ReindexTriggerResult(
                null,
                false,
                "Automatic reindex is disabled; parameters remain Pending.");
        }

        var result = await SendAsync(
            new CreateReindexJobCommand
            {
                Trigger = "Activation",
                QueueRequest = true
            },
            "The automatic reindex trigger is temporarily unavailable.",
            cancellationToken);

        return result switch
        {
            ReindexJobCreatedResult created => RecordStarted(created.JobId, reason),
            ReindexRequestQueuedResult queued => RecordQueued(queued, reason),
            NoReindexWorkResult noWork => new ReindexTriggerResult(null, false, noWork.ErrorMessage),
            ReindexDisabledResult => new ReindexTriggerResult(
                null,
                false,
                "Reindex is unavailable because the feature is disabled; parameters remain Pending."),
            ReindexProviderUnavailableResult unavailable => new ReindexTriggerResult(
                null,
                false,
                $"Reindex is unavailable for tenant {unavailable.TenantId}; parameters remain Pending."),
            InvalidReindexRequestResult invalid => throw new InvalidOperationException(invalid.ErrorMessage),
            ActiveReindexJobResult active => new ReindexTriggerResult(active.ActiveJobId, true, null),
            _ => throw new InvalidOperationException(
                $"Unsupported automatic reindex result {result.GetType().Name}.")
        };
    }

    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.AutoStart)
        {
            logger.LogInformation(
                "Reindex: startup reconciliation skipped because AutoStart is false");
            return;
        }

        var result = await SendAsync(
            new CreateReindexJobCommand { Trigger = "Reconciliation" },
            "Reindex reconciliation is temporarily unavailable.",
            cancellationToken);
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

    private async Task<CreateReindexJobResult> SendAsync(
        CreateReindexJobCommand command,
        string unavailableMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            return await mediator.SendAsync(command, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ReindexTriggerUnavailableException(unavailableMessage, exception);
        }
    }

    private ReindexTriggerResult RecordStarted(string jobId, string reason)
    {
        ReindexMetrics.TriggerStarted("Activation");
        logger.LogInformation(
            "Reindex: activation trigger started job {JobId}; reason {Reason}",
            jobId,
            reason);
        return new ReindexTriggerResult(jobId, false, null);
    }

    private ReindexTriggerResult RecordQueued(
        ReindexRequestQueuedResult queued,
        string reason)
    {
        ReindexMetrics.GenerationQueued(queued.RequestedGeneration);
        logger.LogInformation(
            "Reindex: activation trigger queued generation {Generation} behind job {JobId}; reason {Reason}",
            queued.RequestedGeneration,
            queued.ActiveJobId,
            reason);
        return new ReindexTriggerResult(queued.ActiveJobId, true, null);
    }
}
