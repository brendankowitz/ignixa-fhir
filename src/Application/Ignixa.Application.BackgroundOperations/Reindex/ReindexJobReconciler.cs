using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class ReindexJobReconciler(
    TaskHubClient taskHubClient,
    IBackgroundJobRepository<ReindexJobDefinition> repository,
    ReindexLifecycleEventWriter lifecycle,
    ReindexJobUpdater jobs,
    IReindexJobLock jobLock,
    IOptions<ReindexOptions> options,
    TimeProvider timeProvider,
    ILogger<ReindexJobReconciler> logger)
{
    public async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        await jobLock.ExecuteAsync(
            async ct =>
            {
                await ReconcileUnderLockAsync(ct);
                return true;
            },
            cancellationToken);
    }

    internal async Task ReconcileUnderLockAsync(CancellationToken cancellationToken)
    {
        var candidates = (await repository.ListAsync(
                (int)BackgroundJobType.Reindex,
                cancellationToken))
            .Where(job => !IsTerminal(job.Status))
            .ToArray();
        foreach (var job in candidates)
        {
            if (job.Status.Equals("Completing", StringComparison.OrdinalIgnoreCase))
            {
                await ResumePersistedDecisionAsync(job, cancellationToken);
                continue;
            }

            var lastObserved = job.HeartbeatDate > job.CreateDate
                ? job.HeartbeatDate
                : job.CreateDate;
            if (timeProvider.GetUtcNow() - lastObserved < options.Value.OrphanGrace)
            {
                continue;
            }

            var instanceId = job.OrchestrationInstanceId ?? job.JobId;
            var first = await taskHubClient.GetOrchestrationStateAsync(instanceId);
            if (IsActive(first))
            {
                continue;
            }

            var second = await taskHubClient.GetOrchestrationStateAsync(instanceId);
            if (IsActive(second))
            {
                continue;
            }

            await FinalizeOrphanAsync(job, second ?? first, cancellationToken);
        }
    }

    private async Task ResumePersistedDecisionAsync(
        BackgroundJob<ReindexJobDefinition> job,
        CancellationToken cancellationToken)
    {
        var decision = job.Progress?["terminalDecision"]?.GetValue<string>();
        if (decision is not ("Completed" or "Failed" or "Cancelled"))
        {
            logger.LogError(
                "Reindex: job {JobId} is Completing without a valid persisted terminal decision",
                job.JobId);
            return;
        }

        var outcomes = ReadPersistedOutcomes(job.Progress);
        var completions = ReconstructTargets(job)
            .Where(target => target.IsFullyCovered)
            .Where(target => outcomes.ContainsKey(target.Canonical))
            .Select(target =>
            {
                var outcome = outcomes[target.Canonical];
                return new ReindexTargetCompletion(
                    target,
                    outcome.Success,
                    outcome.ResourcesIndexed,
                    TimeSpan.Zero,
                    outcome.ErrorMessage);
            })
            .ToArray();

        await jobs.TryCompleteUnderLockAsync(
            job.JobId,
            decision,
            (_, ct) => lifecycle.CompleteAsync(job.JobId, completions, ct),
            current =>
            {
                current.Status = decision;
                current.EndDate ??= timeProvider.GetUtcNow();
            },
            cancellationToken);

        logger.LogInformation(
            "Reindex: reconciled persisted {Decision} decision for job {JobId}",
            decision,
            job.JobId);
    }

    private async Task FinalizeOrphanAsync(
        BackgroundJob<ReindexJobDefinition> job,
        OrchestrationState? state,
        CancellationToken cancellationToken)
    {
        var reason = state is null
            ? "Reindex orchestration instance is missing."
            : $"Reindex orchestration ended as {state.OrchestrationStatus} before the job was finalized.";
        var targets = ReconstructTargets(job)
            .Where(target => target.IsFullyCovered)
            .ToArray();
        var completions = targets.Select(target => new ReindexTargetCompletion(
            target,
            false,
            0,
            TimeSpan.Zero,
            reason)).ToArray();

        await jobs.TryCompleteUnderLockAsync(
            job.JobId,
            "Failed",
            (_, ct) => lifecycle.CompleteAsync(job.JobId, completions, ct),
            current =>
            {
                current.Status = "Failed";
                current.EndDate = timeProvider.GetUtcNow();
                current.ErrorMessage = reason;
                current.Progress ??= new JsonObject();
                current.Progress["terminalOutcomes"] = new JsonArray(
                    completions.Select(completion => (JsonNode?)new JsonObject
                    {
                        ["canonical"] = completion.Target.Canonical,
                        ["success"] = false,
                        ["resourcesIndexed"] = 0,
                        ["errorMessage"] = reason
                    }).ToArray());
                current.Result = new JsonObject
                {
                    ["success"] = false
                };
            },
            cancellationToken);

        logger.LogError(
            "Reindex: finalized orphaned job {JobId}: {Reason}",
            job.JobId,
            reason);
    }

    private static IReadOnlyList<ReindexTarget> ReconstructTargets(
        BackgroundJob<ReindexJobDefinition> job) =>
        job.Definition.SearchParameters.Select(target => new ReindexTarget(
            target.Canonical,
            target.Code,
            target.ResourceType,
            target.SearchParamId,
            target.ActivationEventId,
            target.AffectedResourceTypes)
        {
            ScheduledResourceTypes = target.ScheduledResourceTypes
        }).ToArray();

    private static IReadOnlyDictionary<string, PersistedOutcome> ReadPersistedOutcomes(
        JsonNode? progress) =>
        (progress?["terminalOutcomes"] as JsonArray)?
            .OfType<JsonObject>()
            .Where(value => value["canonical"] is not null)
            .ToDictionary(
                value => value["canonical"]!.GetValue<string>(),
                value => new PersistedOutcome(
                    value["success"]?.GetValue<bool>() ?? false,
                    value["resourcesIndexed"]?.GetValue<long>() ?? 0,
                    value["errorMessage"]?.GetValue<string>()),
                StringComparer.Ordinal)
        ?? new Dictionary<string, PersistedOutcome>(StringComparer.Ordinal);

    private static bool IsActive(OrchestrationState? state) =>
        state?.OrchestrationStatus is OrchestrationStatus.Pending
            or OrchestrationStatus.Running
            or OrchestrationStatus.ContinuedAsNew;

    private static bool IsTerminal(string status) =>
        status is "Completed" or "Failed" or "Cancelled";

    private sealed record PersistedOutcome(
        bool Success,
        long ResourcesIndexed,
        string? ErrorMessage);
}
