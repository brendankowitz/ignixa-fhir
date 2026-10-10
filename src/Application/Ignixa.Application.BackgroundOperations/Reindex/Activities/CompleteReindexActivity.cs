using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

/// <summary>
/// Finishes a reindex job the way <c>$export</c> does: under the singleton job lock it appends the guarded
/// lifecycle events for every parameter the job still owns (idempotent on retry) and then writes the final
/// status once. A job that is already terminal is left alone.
/// </summary>
public sealed class CompleteReindexActivity(
    IFhirRepositoryFactory repositoryFactory,
    ReindexLifecycleEventWriter lifecycle,
    IBackgroundJobRepository<ReindexJobDefinition> repository,
    IReindexJobLock jobLock,
    ITenantConfigurationStore tenantConfigurationStore,
    TimeProvider timeProvider,
    ILogger<CompleteReindexActivity> logger)
    : AsyncTaskActivity<CompleteReindexInput, CompleteReindexOutput>
{
    private static readonly JsonSerializerOptions ProgressSerializerOptions =
        new(JsonSerializerDefaults.Web);

    protected override async Task<CompleteReindexOutput> ExecuteAsync(
        TaskContext context,
        CompleteReindexInput input)
    {
        var completions = await EvaluateOwnedTargetsAsync(input);
        return await jobLock.ExecuteAsync(
            cancellationToken => CompleteUnderLockAsync(input, completions, cancellationToken),
            CancellationToken.None);
    }

    private async Task<CompleteReindexOutput> CompleteUnderLockAsync(
        CompleteReindexInput input,
        IReadOnlyList<ReindexTargetCompletion> completions,
        CancellationToken cancellationToken)
    {
        var job = await repository.GetAsync(input.JobId, ReindexJobs.GlobalTenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Reindex job {input.JobId} does not exist.");
        if (ReindexJobs.IsTerminal(job.Status))
        {
            logger.LogInformation(
                "Reindex: completion of job {JobId} is superseded by {Status}",
                job.JobId,
                job.Status);
            return new CompleteReindexOutput(job.Status == "Completed", []);
        }

        var missingTenantIds = await GetTenantsAddedSinceStartAsync(job, cancellationToken);
        if (missingTenantIds.Length > 0)
        {
            var manualReindexMessage =
                $"Active tenants {string.Join(", ", missingTenantIds)} were added after this job started; a manual $reindex is needed.";
            completions = completions.Select(completion => completion with
            {
                Success = false,
                ErrorMessage = Join(completion.ErrorMessage, manualReindexMessage)
            }).ToArray();
        }

        var success = input.FailureMessage is null &&
            missingTenantIds.Length == 0 &&
            input.Tenants.All(tenant => tenant.Success) &&
            completions.All(completion => completion.Success);
        var ignored = await lifecycle.CompleteAsync(input.JobId, completions, cancellationToken);

        var completedAt = timeProvider.GetUtcNow();
        job.Status = success ? "Completed" : "Failed";
        job.EndDate = completedAt;
        job.HeartbeatDate = completedAt;
        job.ErrorMessage = success
            ? null
            : string.Join(
                " ",
                completions.Where(completion => !completion.Success)
                    .Select(completion => completion.ErrorMessage)
                    .Where(message => message is not null));
        job.Progress = BuildProgress(input, success, ignored);
        job.Result = new JsonObject { ["success"] = success };
        await repository.UpdateAsync(job, ReindexJobs.GlobalTenantId, cancellationToken);

        if (job.StartDate.HasValue)
        {
            ReindexMetrics.RecordJobDuration(completedAt - job.StartDate.Value);
        }

        return new CompleteReindexOutput(success, ignored);
    }

    // Every parameter the job owns is completed, planned or not: an unplanned owner fails so it returns to
    // Pending instead of staying Reindexing forever.
    private async Task<IReadOnlyList<ReindexTargetCompletion>> EvaluateOwnedTargetsAsync(CompleteReindexInput input)
    {
        var ownedTargets = (await lifecycle.GetOwnedTargetsAsync(CancellationToken.None))
            .Where(owned => owned.JobId == input.JobId)
            .ToArray();
        var completions = new List<ReindexTargetCompletion>(ownedTargets.Length);
        foreach (var owned in ownedTargets)
        {
            var plannedTarget = input.Targets.FirstOrDefault(target =>
                target.Canonical == owned.Target.Canonical &&
                target.ResourceType == owned.Target.ResourceType &&
                target.Code == owned.Target.Code &&
                target.ActivationEventId == owned.Target.ActivationEventId);
            var target = plannedTarget ?? owned.Target;
            var errors = new List<string>();
            if (plannedTarget is null)
            {
                errors.Add($"Search parameter {target.Canonical} was not planned by this job.");
            }

            if (input.FailureMessage is not null)
            {
                errors.Add(input.FailureMessage);
            }

            long resourcesIndexed = 0;
            foreach (var tenant in input.Tenants)
            {
                resourcesIndexed += tenant.ResourcesReindexed;
                errors.AddRange(await DescribeTenantFailuresAsync(tenant, target));
            }

            completions.Add(new ReindexTargetCompletion(
                target,
                errors.Count == 0,
                resourcesIndexed,
                TimeSpan.Zero,
                errors.Count == 0 ? null : string.Join(" ", errors)));
        }

        return completions;
    }

    private async Task<IReadOnlyList<string>> DescribeTenantFailuresAsync(
        ReindexTenantOutput tenant,
        ReindexParameterDefinition target)
    {
        var errors = new List<string>();
        if (tenant.FailedResourceTypes.Any(type =>
                target.AffectedResourceTypes.Contains(type, StringComparer.OrdinalIgnoreCase)) ||
            tenant.FailedResources.Any(failure =>
                target.AffectedResourceTypes.Contains(failure.ResourceType, StringComparer.OrdinalIgnoreCase)))
        {
            errors.Add($"Tenant {tenant.TenantId}: {tenant.ErrorMessage ?? "resource failures occurred"}");
        }

        var store = await repositoryFactory.GetReindexStoreAsync(tenant.TenantId, CancellationToken.None);
        if (!await store.HasSearchParameterAsync(target.SearchParamId, CancellationToken.None))
        {
            errors.Add(
                $"Tenant {tenant.TenantId}: no physical dbo.SearchParam catalog id {target.SearchParamId} exists for {target.Canonical}.");
        }

        return errors;
    }

    private async Task<int[]> GetTenantsAddedSinceStartAsync(
        BackgroundJob<ReindexJobDefinition> job,
        CancellationToken cancellationToken)
    {
        var activeTenantIds = (await tenantConfigurationStore.GetAllTenantsAsync(cancellationToken))
            .Where(tenant =>
                tenant.IsActive &&
                tenant.TenantId != SystemConstants.SystemPartitionId)
            .Select(tenant => tenant.TenantId)
            .Order();
        return activeTenantIds.Except(job.Definition.TenantIds).ToArray();
    }

    private static JsonNode? BuildProgress(
        CompleteReindexInput input,
        bool success,
        IReadOnlyList<string> ignored) =>
        JsonSerializer.SerializeToNode(
            new
            {
                phase = "Completing",
                resourcesSuccessfullyReindexed = input.Tenants.Sum(tenant => tenant.ResourcesReindexed),
                totalResourcesToReindex = input.Tenants.Sum(tenant => tenant.ResourcesToReindex),
                progress = success ? 100 : CalculateProgress(input.Tenants),
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
                failedResources = input.Tenants
                    .SelectMany(tenant => tenant.FailedResources)
                    .Take(100)
                    .ToArray(),
                ignoredLifecycleEvents = input.IgnoredLifecycleEvents.Concat(ignored).Distinct().ToArray()
            },
            ProgressSerializerOptions);

    private static string Join(string? first, string second) =>
        first is null ? second : $"{first} {second}";

    private static double CalculateProgress(IReadOnlyList<ReindexTenantOutput> tenants)
    {
        var total = tenants.Sum(tenant => tenant.ResourcesToReindex);
        return total == 0
            ? 0
            : Math.Min(99.9, tenants.Sum(tenant => tenant.ResourcesReindexed) * 100.0 / total);
    }
}
