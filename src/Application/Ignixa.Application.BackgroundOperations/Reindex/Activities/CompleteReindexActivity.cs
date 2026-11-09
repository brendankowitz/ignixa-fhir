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
            return new CompleteReindexOutput(job.GetStatus() == ReindexJobStatus.Completed);
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

        var completedAt = timeProvider.GetUtcNow();
        // A job that never started owns no targets, so its completion appends no event to carry a duration.
        var duration = job.StartDate is { } startDate ? completedAt - startDate : TimeSpan.Zero;
        // An owned parameter the job did not plan cannot be judged, so it fails back to Pending.
        const string unplannedReason = "Search parameter was not planned by this job.";
        var finished = await lifecycle.FinishAsync(input.JobId, completions, unplannedReason, duration, cancellationToken);

        var errors = completions
            .Where(completion => !completion.Success)
            .Select(completion => completion.ErrorMessage)
            .OfType<string>()
            .Concat(finished.UnplannedTargets.Select(target => $"{target}: {unplannedReason}"))
            .ToArray();
        var success = errors.Length == 0 &&
            input.FailureMessage is null &&
            missingTenantIds.Length == 0 &&
            input.Tenants.All(tenant => tenant.Success);
        job.Close(
            success ? ReindexJobStatus.Completed : ReindexJobStatus.Failed,
            success ? null : string.Join(" ", errors),
            completedAt);
        job.Progress = BuildProgress(input, finished.IgnoredLifecycleEvents);
        await repository.UpdateAsync(job, SystemConstants.GlobalTenantId, cancellationToken);

        if (job.StartDate.HasValue)
        {
            ReindexMetrics.RecordJobDuration(duration);
        }

        return new CompleteReindexOutput(success);
    }

    // The outcome of every planned target is derived from the orchestration's input, so a retried activity
    // reaches the same decision.
    private async Task<IReadOnlyList<ReindexTargetCompletion>> EvaluateTargetsAsync(CompleteReindexInput input)
    {
        var tenantVersions = await GetTenantVersionsAsync(CancellationToken.None);
        var completions = new List<ReindexTargetCompletion>(input.Targets.Count);
        foreach (var target in input.Targets)
        {
            completions.Add(await EvaluateTargetAsync(input, target, tenantVersions));
        }

        return completions;
    }

    // A tenant on another FHIR version never indexed this target, so neither its failures nor its catalog
    // decide the target's outcome. A tenant no longer configured is judged in full: nothing says it was exempt.
    private async Task<ReindexTargetCompletion> EvaluateTargetAsync(
        CompleteReindexInput input,
        ReindexParameterDefinition target,
        IReadOnlyDictionary<int, string> tenantVersions)
    {
        var errors = new List<string>();
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
