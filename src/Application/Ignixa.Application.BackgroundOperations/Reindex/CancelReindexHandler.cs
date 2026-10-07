using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class CancelReindexHandler(
    TaskHubClient taskHubClient,
    IBackgroundJobRepository<ReindexJobDefinition> repository,
    ReindexLifecycleEventWriter lifecycle,
    ReindexJobUpdater jobs)
    : IRequestHandler<CancelReindexCommand, CancelReindexResult>
{
    public async Task<CancelReindexResult> HandleAsync(
        CancelReindexCommand request,
        CancellationToken cancellationToken)
    {
        var job = await repository.GetAsync(request.JobId, 1, cancellationToken);
        if (job is null)
        {
            return new ReindexJobNotFoundResult(request.JobId);
        }

        if (job.Status is "Completed" or "Failed" or "Cancelled")
        {
            return new ReindexJobAlreadyTerminalResult(job.JobId, job.Status);
        }

        await taskHubClient.TerminateInstanceAsync(
            new OrchestrationInstance { InstanceId = job.OrchestrationInstanceId ?? job.JobId },
            request.Reason);
        var targets = job.Definition.SearchParameters.Select(target => new ReindexTarget(
            target.Canonical,
            target.Code,
            target.ResourceType,
            target.SearchParamId,
            target.ActivationEventId,
            target.AffectedResourceTypes)).ToArray();
        var won = await jobs.TryCompleteAsync(
            job.JobId,
            (_, ct) => lifecycle.CompleteAsync(
                job.JobId,
                targets.Select(target => new ReindexTargetCompletion(
                    target,
                    false,
                    0,
                    TimeSpan.Zero,
                    $"Cancelled: {request.Reason}")).ToArray(),
                ct),
            current =>
            {
                current.Status = "Cancelled";
                current.CancelRequested = true;
                current.EndDate = DateTimeOffset.UtcNow;
                current.ErrorMessage = $"Cancelled: {request.Reason}";
                current.Progress ??= new JsonObject();
                current.Progress["cancellationReason"] = request.Reason;
            },
            cancellationToken);

        if (won)
        {
            return new ReindexCancelledResult(job.JobId);
        }

        var terminal = await repository.GetAsync(request.JobId, 1, cancellationToken)
            ?? throw new InvalidOperationException($"Reindex job {request.JobId} disappeared during cancellation.");
        return new ReindexJobAlreadyTerminalResult(terminal.JobId, terminal.Status);
    }
}
