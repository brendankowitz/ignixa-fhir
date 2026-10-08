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

        if (job.Status is "Completed" or "Failed" or "Cancelled" or "Completing")
        {
            var currentDecision = job.Status == "Completing"
                ? job.Progress?["terminalDecision"]?.GetValue<string>() ?? job.Status
                : job.Status;
            return new ReindexJobAlreadyTerminalResult(job.JobId, currentDecision);
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
            target.AffectedResourceTypes)
        {
            ScheduledResourceTypes = target.ScheduledResourceTypes
        }).ToArray();
        var won = await jobs.TryCompleteAsync(
            job.JobId,
            "Cancelled",
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
                current.Progress["terminalOutcomes"] = new JsonArray(
                    targets
                        .Where(target => target.IsFullyCovered)
                        .Select(target => (JsonNode?)new JsonObject
                        {
                            ["canonical"] = target.Canonical,
                            ["resourceType"] = target.ResourceType,
                            ["code"] = target.Code,
                            ["success"] = false,
                            ["resourcesIndexed"] = 0,
                            ["errorMessage"] = $"Cancelled: {request.Reason}"
                        })
                        .ToArray());
            },
            cancellationToken);

        if (won)
        {
            return new ReindexCancelledResult(job.JobId);
        }

        var terminal = await repository.GetAsync(request.JobId, 1, cancellationToken)
            ?? throw new InvalidOperationException($"Reindex job {request.JobId} disappeared during cancellation.");
        var decidedStatus = terminal.Status == "Completing"
            ? terminal.Progress?["terminalDecision"]?.GetValue<string>() ?? terminal.Status
            : terminal.Status;
        return new ReindexJobAlreadyTerminalResult(terminal.JobId, decidedStatus);
    }
}
