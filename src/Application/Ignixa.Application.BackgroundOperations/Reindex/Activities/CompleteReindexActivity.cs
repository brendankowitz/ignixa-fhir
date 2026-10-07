using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

public sealed class CompleteReindexActivity(
    IFhirRepositoryFactory repositoryFactory,
    ReindexLifecycleEventWriter lifecycle,
    ReindexJobUpdater jobs,
    TimeProvider timeProvider)
    : AsyncTaskActivity<CompleteReindexInput, CompleteReindexOutput>
{
    protected override async Task<CompleteReindexOutput> ExecuteAsync(
        TaskContext context,
        CompleteReindexInput input)
    {
        var completedAt = timeProvider.GetUtcNow();
        var completions = new List<ReindexTargetCompletion>();
        foreach (var target in input.Targets)
        {
            var errors = new List<string>();
            long resourcesIndexed = 0;
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
                    !await store.HasSearchParameterAsync(target.Canonical, CancellationToken.None))
                {
                    errors.Add(
                        $"Tenant {tenant.TenantId}: no physical dbo.SearchParam catalog id exists for {target.Canonical}.");
                }
            }

            completions.Add(new ReindexTargetCompletion(
                target,
                errors.Count == 0,
                resourcesIndexed,
                TimeSpan.Zero,
                errors.Count == 0 ? null : string.Join(" ", errors)));
        }

        var ignored = await lifecycle.CompleteAsync(
            input.JobId,
            completions,
            CancellationToken.None);
        var success = input.Tenants.All(tenant => tenant.Success) &&
            completions.All(completion => completion.Success);
        var failedResources = input.Tenants
            .SelectMany(tenant => tenant.FailedResources)
            .Take(100)
            .ToArray();
        await jobs.CompleteAsync(
            input.JobId,
            job =>
            {
                job.Status = success ? "Completed" : "Failed";
                job.EndDate = completedAt;
                job.ErrorMessage = success
                    ? null
                    : string.Join(
                        " ",
                        completions.Where(completion => !completion.Success)
                            .Select(completion => completion.ErrorMessage));
                job.Progress = JsonSerializer.SerializeToNode(new
                {
                    phase = "Completing",
                    resourcesSuccessfullyReindexed = input.Tenants.Sum(tenant => tenant.ResourcesReindexed),
                    totalResourcesToReindex = input.Tenants.Sum(tenant => tenant.ResourcesToReindex),
                    conflicts = input.Tenants.Sum(tenant => tenant.Conflicts),
                    tenants = input.Tenants,
                    failedResources,
                    ignoredLifecycleEvents = input.IgnoredLifecycleEvents.Concat(ignored).Distinct()
                });
                job.Result = new JsonObject
                {
                    ["success"] = success
                };
            },
            CancellationToken.None);

        return new CompleteReindexOutput(success, ignored);
    }
}
