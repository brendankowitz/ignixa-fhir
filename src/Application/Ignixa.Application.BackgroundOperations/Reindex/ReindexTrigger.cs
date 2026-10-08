using DurableTask.Core.Exceptions;
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

        CreateReindexJobResult result;
        try
        {
            result = await mediator.SendAsync(
                new CreateReindexJobCommand
                {
                    Trigger = "Activation",
                    QueueRequest = true
                },
                cancellationToken);
        }
        catch (Exception exception) when (ReindexTriggerUnavailableException.IsOperational(exception))
        {
            throw new ReindexTriggerUnavailableException(
                "The automatic reindex trigger is temporarily unavailable.",
                exception);
        }
        catch (OrchestrationFrameworkException exception)
        {
            throw new ReindexTriggerUnavailableException(
                "The automatic reindex trigger is temporarily unavailable.",
                exception);
        }

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
