using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class CompleteReindexActivity(
    IFhirRepositoryFactory repositoryFactory,
    ReindexLifecycleEventWriter lifecycle,
    ReindexJobUpdater jobs,
    ITenantConfigurationStore tenantConfigurationStore,
    TimeProvider timeProvider)
    : AsyncTaskActivity<CompleteReindexInput, CompleteReindexOutput>
{
    private static readonly JsonSerializerOptions ProgressSerializerOptions =
        new(JsonSerializerDefaults.Web);

    protected override async Task<CompleteReindexOutput> ExecuteAsync(
        TaskContext context,
        CompleteReindexInput input)
    {
        var completedAt = timeProvider.GetUtcNow();
        var ownedTargets = (await lifecycle.GetOwnedTargetsAsync(CancellationToken.None))
            .Where(owned => owned.JobId == input.JobId)
            .ToArray();
        var completions = new List<ReindexTargetCompletion>();
        foreach (var owned in ownedTargets)
        {
            var plannedTarget = input.Targets.FirstOrDefault(target =>
                target.Canonical == owned.Target.Canonical &&
                target.ResourceType == owned.Target.ResourceType &&
                target.Code == owned.Target.Code &&
                target.ActivationEventId == owned.Target.ActivationEventId);
            var target = plannedTarget ?? owned.Target;
            var errors = new List<string>();
            long resourcesIndexed = 0;
            if (plannedTarget is not { IsFullyCovered: true })
            {
                errors.Add($"Search parameter {target.Canonical} was not planned by this job.");
            }

            if (input.FailureMessage is not null)
            {
                errors.Add(input.FailureMessage);
            }

            foreach (var tenant in input.Tenants)
            {
                resourcesIndexed += tenant.ResourcesReindexed;
                if (tenant.FailedResourceTypes.Any(type =>
                        target.AffectedResourceTypes.Contains(type, StringComparer.OrdinalIgnoreCase)) ||
                    tenant.FailedResources.Any(failure =>
                        target.AffectedResourceTypes.Contains(
                            failure.ResourceType,
                            StringComparer.OrdinalIgnoreCase)))
                {
                    errors.Add(
                        $"Tenant {tenant.TenantId}: {tenant.ErrorMessage ?? "resource failures occurred"}");
                }

                var repository = await repositoryFactory.GetRepositoryAsync(
                    tenant.TenantId,
                    CancellationToken.None);
                if (repository is not IReindexStore store ||
                    !await store.HasSearchParameterAsync(target.SearchParamId, CancellationToken.None))
                {
                    errors.Add(
                        $"Tenant {tenant.TenantId}: no physical dbo.SearchParam catalog id {target.SearchParamId} exists for {target.Canonical}.");
                }
            }

            completions.Add(new ReindexTargetCompletion(
                target,
                errors.Count == 0,
                resourcesIndexed,
                TimeSpan.Zero,
                errors.Count == 0 ? null : string.Join(" ", errors)));
        }

        var success = input.FailureMessage is null &&
            input.Tenants.All(tenant => tenant.Success) &&
            completions.All(completion => completion.Success);
        var failedResources = input.Tenants
            .SelectMany(tenant => tenant.FailedResources)
            .Take(100)
            .ToArray();
        IReadOnlyList<string> ignored = [];
        var won = await jobs.TryCompleteAsync(
            input.JobId,
            async (job, cancellationToken) =>
            {
                if (job.Status == "Completing")
                {
                    var terminalStatus = job.Progress?["terminalDecision"]?.GetValue<string>()
                        ?? throw new InvalidOperationException(
                            $"Reindex job {job.JobId} is Completing without a persisted terminal decision.");
                    success = terminalStatus == "Completed";
                    return terminalStatus;
                }

                var activeTenantIds = (await tenantConfigurationStore.GetAllTenantsAsync(cancellationToken))
                    .Where(tenant =>
                        tenant.IsActive &&
                        tenant.TenantId != SystemConstants.SystemPartitionId)
                    .Select(tenant => tenant.TenantId)
                    .Order()
                    .ToArray();
                var missingTenantIds = activeTenantIds.Except(job.Definition.TenantIds).ToArray();
                if (missingTenantIds.Length > 0)
                {
                    success = false;
                    var manualReindexMessage =
                        $"Active tenants {string.Join(", ", missingTenantIds)} were added after this job started; a manual $reindex is needed.";
                    completions = completions.Select(completion => completion with
                    {
                        Success = false,
                        ErrorMessage = string.Join(
                            " ",
                            new[] { completion.ErrorMessage, manualReindexMessage }
                                .Where(message => message is not null))
                    })
                        .ToList();
                }

                return success ? "Completed" : "Failed";
            },
            async (_, cancellationToken) =>
            {
                ignored = await lifecycle.CompleteAsync(
                    input.JobId,
                    completions,
                    cancellationToken);
            },
            job =>
            {
                job.Status = success ? "Completed" : "Failed";
                job.EndDate = completedAt;
                job.ErrorMessage = success
                    ? null
                    : string.Join(
                        " ",
                        completions.Where(completion => !completion.Success)
                            .Select(completion => completion.ErrorMessage)
                            .Where(message => message is not null));
                job.Progress = JsonSerializer.SerializeToNode(
                    new
                    {
                        phase = "Completing",
                        resourcesSuccessfullyReindexed = input.Tenants.Sum(tenant => tenant.ResourcesReindexed),
                        totalResourcesToReindex = input.Tenants.Sum(tenant => tenant.ResourcesToReindex),
                        progress = success
                            ? 100
                            : CalculateProgress(input.Tenants),
                        conflicts = input.Tenants.Sum(tenant => tenant.Conflicts),
                        tenants = input.Tenants.Select(tenant => new
                        {
                            tenant.TenantId,
                            tenant.CutoffTransactionId,
                            tenant.CutoffSurrogateId,
                            status = tenant.Success ? "Completed" : "Failed",
                            tenant.ResourcesToReindex,
                            tenant.ResourcesReindexed,
                            tenant.Conflicts,
                            failedResources = tenant.FailedResourceCount,
                            tenant.ErrorMessage
                        }).ToArray(),
                        failedResources,
                        ignoredLifecycleEvents = input.IgnoredLifecycleEvents.Concat(ignored).Distinct()
                            .ToArray(),
                        notCovered = input.Targets
                            .Where(target => !target.IsFullyCovered)
                            .Select(target => target.Canonical)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray(),
                        terminalOutcomes = completions.Select(completion => new
                        {
                            completion.Target.Canonical,
                            completion.Target.ResourceType,
                            completion.Target.Code,
                            completion.Success,
                            completion.ResourcesIndexed,
                            completion.ErrorMessage
                        }).ToArray()
                    },
                    ProgressSerializerOptions);
                job.Result = new JsonObject
                {
                    ["success"] = success
                };
                if (job.StartDate.HasValue)
                {
                    ReindexMetrics.RecordJobDuration(completedAt - job.StartDate.Value);
                }
            },
            CancellationToken.None);

        return new CompleteReindexOutput(won && success, ignored);
    }

    private static double CalculateProgress(IReadOnlyList<ReindexTenantOutput> tenants)
    {
        var total = tenants.Sum(tenant => tenant.ResourcesToReindex);
        return total == 0
            ? 0
            : Math.Min(99.9, tenants.Sum(tenant => tenant.ResourcesReindexed) * 100.0 / total);
    }
}
