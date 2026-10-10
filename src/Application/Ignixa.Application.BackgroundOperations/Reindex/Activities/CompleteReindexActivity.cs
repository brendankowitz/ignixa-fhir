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
    IReindexStoreFactory reindexStoreFactory,
    ReindexLifecycleEventWriter lifecycle,
    IBackgroundJobRepository<ReindexJobDefinition> repository,
    IReindexJobLock jobLock,
    ITenantConfigurationStore tenantConfigurationStore,
    TimeProvider timeProvider,
    ILogger<CompleteReindexActivity> logger)
    : AsyncTaskActivity<CompleteReindexInput, CompleteReindexOutput>
{
    protected override async Task<CompleteReindexOutput> ExecuteAsync(
        TaskContext context,
        CompleteReindexInput input)
    {
        var completions = await EvaluateTargetsAsync(input);
        return await jobLock.ExecuteAsync(
            cancellationToken => CompleteUnderLockAsync(input, completions, cancellationToken),
            CancellationToken.None);
    }

    private async Task<CompleteReindexOutput> CompleteUnderLockAsync(
        CompleteReindexInput input,
        IReadOnlyList<ReindexTargetCompletion> completions,
        CancellationToken cancellationToken)
    {
        var job = await repository.GetAsync(input.JobId, SystemConstants.GlobalTenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Reindex job {input.JobId} does not exist.");
        if (job.IsTerminal())
        {
            logger.LogInformation(
                "Reindex: completion of job {JobId} is superseded by {Status}",
                job.JobId,
                job.Status);
            return new CompleteReindexOutput(job.GetStatus() == ReindexJobStatus.Completed, []);
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
        job.SetStatus(success ? ReindexJobStatus.Completed : ReindexJobStatus.Failed);
        job.EndDate = completedAt;
        job.HeartbeatDate = completedAt;
        job.ErrorMessage = success
            ? null
            : string.Join(
                " ",
                completions.Where(completion => !completion.Success)
                    .Select(completion => completion.ErrorMessage)
                    .Where(message => message is not null));
        job.Progress = BuildProgress(input, ignored);
        job.Result = new JsonObject { ["success"] = success };
        await repository.UpdateAsync(job, SystemConstants.GlobalTenantId, cancellationToken);

        if (job.StartDate.HasValue)
        {
            ReindexMetrics.RecordJobDuration(completedAt - job.StartDate.Value);
        }

        return new CompleteReindexOutput(success, ignored);
    }

    // The outcome of every planned target is derived from the orchestration's input, so a retried activity
    // reaches the same decision. Every parameter the job still owns is completed as well, planned or not: an
    // unplanned owner fails so it returns to Pending instead of staying Reindexing forever.
    private async Task<IReadOnlyList<ReindexTargetCompletion>> EvaluateTargetsAsync(CompleteReindexInput input)
    {
        var unplannedOwners = (await lifecycle.GetOwnedTargetsAsync(CancellationToken.None))
            .Where(owned => owned.JobId == input.JobId)
            .Select(owned => owned.Target)
            .Where(owned => !input.Targets.Any(planned => ReindexLifecycleEventWriter.SameTarget(planned, owned)));
        var tenantVersions = await GetTenantVersionsAsync(CancellationToken.None);
        var completions = new List<ReindexTargetCompletion>(input.Targets.Count);
        foreach (var target in input.Targets)
        {
            completions.Add(await EvaluateTargetAsync(input, target, tenantVersions, unplanned: false));
        }

        foreach (var target in unplannedOwners)
        {
            completions.Add(await EvaluateTargetAsync(input, target, tenantVersions, unplanned: true));
        }

        return completions;
    }

    // A tenant on another FHIR version never indexed this target, so neither its failures nor its catalog
    // decide the target's outcome. A tenant no longer configured is judged in full: nothing says it was exempt.
    private async Task<ReindexTargetCompletion> EvaluateTargetAsync(
        CompleteReindexInput input,
        ReindexParameterDefinition target,
        IReadOnlyDictionary<int, string> tenantVersions,
        bool unplanned)
    {
        var errors = new List<string>();
        if (unplanned)
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
            if (tenantVersions.TryGetValue(tenant.TenantId, out var fhirVersion) && !target.AppliesToTenant(fhirVersion))
            {
                continue;
            }

            resourcesIndexed += tenant.ResourcesReindexed;
            errors.AddRange(await DescribeTenantFailuresAsync(tenant, target));
        }

        return new ReindexTargetCompletion(
            target,
            errors.Count == 0,
            resourcesIndexed,
            TimeSpan.Zero,
            errors.Count == 0 ? null : string.Join(" ", errors));
    }

    private async Task<IReadOnlyList<string>> DescribeTenantFailuresAsync(
        ReindexTenantProgress tenant,
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

        var store = await reindexStoreFactory.GetReindexStoreAsync(tenant.TenantId, CancellationToken.None);
        if (!await store.HasSearchParameterAsync(target.StorageCanonical, CancellationToken.None))
        {
            errors.Add(
                $"Tenant {tenant.TenantId}: the search-parameter catalog has no row for {target.StorageCanonical}, so {target.Canonical} was not indexed.");
        }

        return errors;
    }

    // Only a tenant the job's targets apply to had to be in the job; one on another FHIR version was never
    // part of the plan. A job with no recorded targets is held to every active tenant.
    private async Task<int[]> GetTenantsAddedSinceStartAsync(
        BackgroundJob<ReindexJobDefinition> job,
        CancellationToken cancellationToken)
    {
        var targets = job.Definition.SearchParameters;
        var activeTenantIds = (await tenantConfigurationStore.GetAllTenantsAsync(cancellationToken))
            .Where(tenant =>
                tenant.IsActive &&
                tenant.TenantId != SystemConstants.SystemPartitionId &&
                (targets.Count == 0 || targets.Any(target => target.AppliesToTenant(tenant.FhirVersion))))
            .Select(tenant => tenant.TenantId)
            .Order();
        return activeTenantIds.Except(job.Definition.TenantIds).ToArray();
    }

    private async Task<IReadOnlyDictionary<int, string>> GetTenantVersionsAsync(CancellationToken cancellationToken) =>
        (await tenantConfigurationStore.GetAllTenantsAsync(cancellationToken))
            .ToDictionary(tenant => tenant.TenantId, tenant => tenant.FhirVersion);

    private static JsonNode BuildProgress(CompleteReindexInput input, IReadOnlyList<string> ignored) =>
        new ReindexProgress(ReindexPhase.Completing)
        {
            Tenants = input.Tenants
                .Select(tenant => tenant with
                {
                    Status = tenant.Success ? ReindexTenantStatus.Completed : ReindexTenantStatus.Failed
                })
                .ToArray(),
            IgnoredLifecycleEvents = input.IgnoredLifecycleEvents.Concat(ignored).Distinct().ToArray()
        }.ToJson();

    private static string Join(string? first, string second) =>
        first is null ? second : $"{first} {second}";
}
