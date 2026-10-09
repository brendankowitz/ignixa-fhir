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
                await ReconcileUnderLockAsync(recoverFreshQueuedJobs: false, ct);
                return true;
            },
            cancellationToken);
    }

    public async Task ReconcileStartupAsync(CancellationToken cancellationToken)
    {
        await jobLock.ExecuteAsync(
            async ct =>
            {
                await ReconcileUnderLockAsync(recoverFreshQueuedJobs: true, ct);
                return true;
            },
            cancellationToken);
    }

    internal Task ReconcileUnderLockAsync(CancellationToken cancellationToken) =>
        ReconcileUnderLockAsync(recoverFreshQueuedJobs: false, cancellationToken);

    private async Task ReconcileUnderLockAsync(
        bool recoverFreshQueuedJobs,
        CancellationToken cancellationToken)
    {
        var active = await repository.GetActiveAsync(
            (int)BackgroundJobType.Reindex,
            cancellationToken);
        var candidates = active is null ? [] : new[] { active };
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
            var isWithinOrphanGrace =
                timeProvider.GetUtcNow() - lastObserved < options.Value.OrphanGrace;
            var shouldRecoverFreshQueuedJob =
                recoverFreshQueuedJobs &&
                job.Status.Equals("Queued", StringComparison.OrdinalIgnoreCase);
            if (isWithinOrphanGrace && !shouldRecoverFreshQueuedJob)
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

        await ResetParametersOwnedByTerminalJobsAsync(cancellationToken);
    }

    private async Task ResetParametersOwnedByTerminalJobsAsync(
        CancellationToken cancellationToken)
    {
        var ownedTargets = await lifecycle.GetOwnedTargetsAsync(cancellationToken);
        foreach (var group in ownedTargets.GroupBy(target => target.JobId, StringComparer.Ordinal))
        {
            var job = await repository.GetAsync(group.Key, 1, cancellationToken);
            if (job is null || !IsTerminal(job.Status))
            {
                continue;
            }

            var reason =
                $"Reindex parameter remained owned by terminal {job.Status} job {job.JobId}.";
            var completions = group.Select(owned => new ReindexTargetCompletion(
                owned.Target,
                false,
                0,
                TimeSpan.Zero,
                reason)).ToArray();
            await lifecycle.CompleteAsync(job.JobId, completions, cancellationToken);
            logger.LogWarning(
                "Reindex: reset {ParameterCount} parameters still owned by terminal {Status} job {JobId}",
                completions.Length,
                job.Status,
                job.JobId);
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
            .Where(target =>
                outcomes.ContainsKey(TargetIdentity(target)) ||
                outcomes.ContainsKey(target.Canonical))
            .Select(target =>
            {
                var outcome = outcomes.GetValueOrDefault(TargetIdentity(target)) ??
                    outcomes[target.Canonical];
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
                        ["resourceType"] = completion.Target.ResourceType,
                        ["code"] = completion.Target.Code,
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

    private static IReadOnlyList<ReindexParameterDefinition> ReconstructTargets(
        BackgroundJob<ReindexJobDefinition> job) =>
        job.Definition.SearchParameters;

    internal static IReadOnlyDictionary<string, PersistedOutcome> ReadPersistedOutcomes(
        JsonNode? progress) =>
        (progress?["terminalOutcomes"] as JsonArray)?
            .OfType<JsonObject>()
            .Where(value => value["canonical"] is not null)
            .GroupBy(
                value => value["resourceType"] is not null && value["code"] is not null
                    ? TargetIdentity(
                        value["canonical"]!.GetValue<string>(),
                        value["resourceType"]!.GetValue<string>(),
                        value["code"]!.GetValue<string>())
                    : value["canonical"]!.GetValue<string>(),
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var value = group.Last();
                    return new PersistedOutcome(
                        value["success"]?.GetValue<bool>() ?? false,
                        value["resourcesIndexed"]?.GetValue<long>() ?? 0,
                        value["errorMessage"]?.GetValue<string>());
                },
                StringComparer.Ordinal)
        ?? new Dictionary<string, PersistedOutcome>(StringComparer.Ordinal);

    internal static string TargetIdentity(ReindexParameterDefinition target) =>
        TargetIdentity(target.Canonical, target.ResourceType, target.Code);

    private static string TargetIdentity(string canonical, string resourceType, string code) =>
        $"{canonical}|{resourceType}|{code}";

    private static bool IsActive(OrchestrationState? state) =>
        state?.OrchestrationStatus is OrchestrationStatus.Pending
            or OrchestrationStatus.Running
            or OrchestrationStatus.ContinuedAsNew;

    private static bool IsTerminal(string status) =>
        status is "Completed" or "Failed" or "Cancelled";

    internal sealed record PersistedOutcome(
        bool Success,
        long ResourcesIndexed,
        string? ErrorMessage);
}
