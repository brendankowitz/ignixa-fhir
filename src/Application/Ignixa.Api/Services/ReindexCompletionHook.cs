using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Models;
using Medino;

namespace Ignixa.Api.Services;

public sealed class ReindexCompletionHook(
    ConformanceRefreshPublisher refreshPublisher,
    ConformanceState conformanceState,
    ReindexAutomationStateStore automationState,
    IMediator mediator,
    ILogger<ReindexCompletionHook> logger)
    : IReindexCompletionHook
{
    public async Task OnCompletedAsync(
        BackgroundJob<ReindexJobDefinition> job,
        CancellationToken cancellationToken)
    {
        await refreshPublisher.RefreshUntilCurrentAsync(cancellationToken);

        var requestedGeneration =
            await automationState.GetRequestedGenerationAsync(cancellationToken);
        var pendingExists = conformanceState.AllSearchParameters.Values.Any(
            parameter => parameter.Status == SearchParameterStatus.Pending);
        if (requestedGeneration <= job.Definition.ConsumedGeneration && !pendingExists)
        {
            return;
        }

        var result = await mediator.SendAsync(
            new CreateReindexJobCommand
            {
                Trigger = "FollowUp",
                LockAlreadyHeld = true,
                ExcludedActiveJobId = job.JobId
            },
            cancellationToken);
        switch (result)
        {
            case ReindexJobCreatedResult created:
                ReindexMetrics.FollowUpStarted();
                logger.LogInformation(
                    "Reindex: follow-up job {FollowUpJobId} started after job {JobId}; consumed generation {ConsumedGeneration}, requested generation {RequestedGeneration}",
                    created.JobId,
                    job.JobId,
                    job.Definition.ConsumedGeneration,
                    requestedGeneration);
                break;
            case ActiveReindexJobResult:
            case NoReindexWorkResult:
                break;
            case ReindexDisabledResult:
                logger.LogWarning(
                    "Reindex: follow-up after job {JobId} could not start because reindex is disabled; parameters remain Pending",
                    job.JobId);
                break;
            case ReindexProviderUnavailableResult unavailable:
                logger.LogWarning(
                    "Reindex: follow-up after job {JobId} could not start because tenant {TenantId} is unavailable; parameters remain Pending",
                    job.JobId,
                    unavailable.TenantId);
                break;
            default:
                throw new InvalidOperationException(
                    $"Reindex follow-up returned unexpected result {result.GetType().Name}.");
        }
    }
}
