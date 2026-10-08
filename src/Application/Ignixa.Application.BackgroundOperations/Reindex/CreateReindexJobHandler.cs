using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Orchestrations;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Reindex;
using Ignixa.Application.Features.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Medino;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class CreateReindexJobHandler(
    TaskHubClient taskHubClient,
    IBackgroundJobRepository<ReindexJobDefinition> jobRepository,
    ITenantConfigurationStore tenantConfigurationStore,
    IFhirVersionContext fhirVersionContext,
    ConformanceState conformanceState,
    IReindexAvailability availability,
    IReindexJobLock jobLock,
    ReindexJobReconciler reconciler,
    ReindexAutomationStateStore automationState,
    IOptions<ReindexOptions> options)
    : IRequestHandler<CreateReindexJobCommand, CreateReindexJobResult>
{
    private readonly TaskHubClient _taskHubClient =
        taskHubClient ?? throw new ArgumentNullException(nameof(taskHubClient));
    private readonly IBackgroundJobRepository<ReindexJobDefinition> _jobRepository =
        jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
    private readonly ITenantConfigurationStore _tenantConfigurationStore =
        tenantConfigurationStore ?? throw new ArgumentNullException(nameof(tenantConfigurationStore));
    private readonly IFhirVersionContext _fhirVersionContext =
        fhirVersionContext ?? throw new ArgumentNullException(nameof(fhirVersionContext));
    private readonly ConformanceState _conformanceState =
        conformanceState ?? throw new ArgumentNullException(nameof(conformanceState));
    private readonly IReindexAvailability _availability =
        availability ?? throw new ArgumentNullException(nameof(availability));
    private readonly IReindexJobLock _jobLock =
        jobLock ?? throw new ArgumentNullException(nameof(jobLock));
    private readonly ReindexJobReconciler _reconciler =
        reconciler ?? throw new ArgumentNullException(nameof(reconciler));
    private readonly ReindexAutomationStateStore _automationState =
        automationState ?? throw new ArgumentNullException(nameof(automationState));
    private readonly ReindexOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    public async Task<CreateReindexJobResult> HandleAsync(
        CreateReindexJobCommand request,
        CancellationToken cancellationToken)
    {
        var availability = await _availability.GetAvailabilityAsync(cancellationToken);
        if (availability.Status == ReindexAvailabilityStatus.Disabled)
        {
            return new ReindexDisabledResult();
        }

        if (availability.Status == ReindexAvailabilityStatus.Unsupported)
        {
            return new ReindexProviderUnavailableResult(availability.UnsupportedTenantId ?? 0);
        }

        ReindexJobParameters parameters;
        try
        {
            parameters = ReindexJobParameters.Create(
                request.MaximumNumberOfResourcesPerQuery
                    ?? _options.DefaultMaximumNumberOfResourcesPerQuery,
                request.MaximumNumberOfResourcesPerWrite
                    ?? _options.DefaultMaximumNumberOfResourcesPerWrite,
                request.MaximumConcurrency
                    ?? _options.DefaultMaximumConcurrency,
                request.QueryDelayIntervalInMilliseconds ?? 0);
        }
        catch (ReindexValidationException ex)
        {
            return new InvalidReindexRequestResult(ex.Message);
        }

        var tenants = (await _tenantConfigurationStore.GetAllTenantsAsync(cancellationToken))
            .Where(tenant =>
                tenant.IsActive &&
                tenant.TenantId != SystemConstants.SystemPartitionId)
            .OrderBy(tenant => tenant.TenantId)
            .ToArray();
        if (tenants.Length == 0)
        {
            return new NoReindexWorkResult("No active tenant is configured for reindexing.");
        }

        var concreteResourceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var domainResourceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tenant in tenants)
        {
            var version = FhirSpecificationExtensions.FromVersionString(tenant.FhirVersion);
            var schema = _fhirVersionContext.GetSchemaProvider(version, tenant.TenantId);
            foreach (var resourceType in schema.ResourceTypeNames)
            {
                if (schema.GetTypeDefinition(resourceType)?.Info.IsAbstract == false)
                {
                    concreteResourceTypes.Add(resourceType);
                    var definition = schema.GetTypeDefinition(resourceType);
                    if (definition!.Children.Any(child =>
                        child.Info.Name.Equals("text", StringComparison.OrdinalIgnoreCase)))
                    {
                        domainResourceTypes.Add(resourceType);
                    }
                }
            }
        }

        if (request.TargetResourceTypes is { Count: > 0 })
        {
            var unknown = request.TargetResourceTypes
                .Where(type => !concreteResourceTypes.Contains(type))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (unknown.Length > 0)
            {
                return new InvalidReindexRequestResult(
                    $"targetResourceTypes contains unknown or abstract resource types: {string.Join(", ", unknown)}.");
            }
        }

        async Task<CreateReindexJobResult> StartUnderLockAsync(CancellationToken ct)
        {
            async Task<(long TargetEventId, ReindexTargetResolution Resolution)> ResolveAsync()
            {
                using (await _conformanceState.AcquireActivationLockAsync(ct))
                {
                    return (
                        _conformanceState.LastProcessedEventId,
                        ReindexTargetResolver.Resolve(
                            _conformanceState.AllSearchParameters.Values.ToArray(),
                            concreteResourceTypes,
                            request.TargetResourceTypes,
                            new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["Resource"] = concreteResourceTypes,
                                ["DomainResource"] = domainResourceTypes
                            }));
                }
            }

            var requestedGeneration = request.QueueRequest
                ? await _automationState.IncrementRequestedGenerationAsync(ct)
                : await _automationState.GetRequestedGenerationAsync(ct);
            var jobs = await _jobRepository.ListAsync(
                (int)BackgroundJobType.Reindex,
                ct);
            var activeJobs = jobs.Where(job =>
                job.JobId != request.ExcludedActiveJobId &&
                (job.Status.Equals("Queued", StringComparison.OrdinalIgnoreCase) ||
                 job.Status.Equals("Running", StringComparison.OrdinalIgnoreCase) ||
                 job.Status.Equals("Completing", StringComparison.OrdinalIgnoreCase)));
            foreach (var active in activeJobs)
            {
                if (request.QueueRequest &&
                    active.Status.Equals("Queued", StringComparison.OrdinalIgnoreCase) &&
                    active.Definition.Trigger.Equals("Activation", StringComparison.OrdinalIgnoreCase))
                {
                    var queuedResolution = await ResolveAsync();
                    active.Definition = CopyWithResolution(
                        active.Definition,
                        queuedResolution.TargetEventId,
                        queuedResolution.Resolution,
                        requestedGeneration);
                    active.Progress ??= new JsonObject();
                    active.Progress["notCovered"] = new JsonArray(
                        queuedResolution.Resolution.Targets
                            .Where(target => !target.IsFullyCovered)
                            .Select(target => (JsonNode?)JsonValue.Create(target.Canonical))
                            .ToArray());
                    await _jobRepository.UpdateAsync(active, 1, ct);
                    return new ReindexRequestQueuedResult(active.JobId, requestedGeneration);
                }

                if (request.QueueRequest)
                {
                    return new ReindexRequestQueuedResult(active.JobId, requestedGeneration);
                }

                return new ActiveReindexJobResult(active.JobId);
            }

            var (targetEventId, resolution) = await ResolveAsync();
            if (!resolution.HasWork)
            {
                return new NoReindexWorkResult("No resources need reindexing.");
            }

            var jobId = Guid.NewGuid().ToString();
            var definition = new ReindexJobDefinition
            {
                TargetEventId = targetEventId,
                TenantIds = tenants.Select(tenant => tenant.TenantId).ToArray(),
                ResourceTypes = resolution.ResourceTypes,
                SearchParameters = resolution.Targets.Select(target => new ReindexParameterDefinition(
                    target.Canonical,
                    target.Code,
                    target.ResourceType,
                    target.SearchParamId,
                    target.ActivationEventId,
                    target.AffectedResourceTypes)
                {
                    ScheduledResourceTypes = target.ScheduledResourceTypes
                }).ToArray(),
                MaximumNumberOfResourcesPerQuery = parameters.MaximumNumberOfResourcesPerQuery,
                MaximumNumberOfResourcesPerWrite = parameters.MaximumNumberOfResourcesPerWrite,
                MaximumConcurrency = parameters.MaximumConcurrency,
                QueryDelayIntervalInMilliseconds = parameters.QueryDelayIntervalInMilliseconds,
                Trigger = request.Trigger,
                ConsumedGeneration = requestedGeneration
            };
            var now = DateTimeOffset.UtcNow;
            await _jobRepository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
            {
                JobId = jobId,
                OrchestrationInstanceId = jobId,
                JobType = (int)BackgroundJobType.Reindex,
                Status = "Queued",
                Definition = definition,
                Progress = new JsonObject
                {
                    ["phase"] = "BarrierDelay",
                    ["ignoredLifecycleEvents"] = new JsonArray(),
                    ["notCovered"] = new JsonArray(
                        resolution.Targets
                            .Where(target => !target.IsFullyCovered)
                            .Select(target => (JsonNode?)JsonValue.Create(target.Canonical))
                            .ToArray())
                },
                CreateDate = now,
                HeartbeatDate = now
            }, ct);

            try
            {
                await _taskHubClient.CreateOrchestrationInstanceAsync(
                    typeof(ReindexOrchestration),
                    jobId,
                    new ReindexOrchestrationInput(
                        jobId,
                        targetEventId,
                        _options.BarrierDelay,
                        definition.TenantIds,
                        definition.ResourceTypes,
                        resolution.Targets,
                        parameters,
                        _options.DrainWarningAfter,
                        _options.ContinueAsNewThreshold)
                    {
                        HeartbeatInterval = ReindexActivityHeartbeat.GetInterval(_options.StaleJobTimeout),
                        StartDebounce = request.QueueRequest &&
                            request.Trigger.Equals("Activation", StringComparison.OrdinalIgnoreCase)
                            ? _options.StartDebounce
                            : TimeSpan.Zero
                    });
            }
            catch (Exception ex)
            {
                var failed = await _jobRepository.GetAsync(jobId, 1, ct)
                    ?? throw new InvalidOperationException(
                        $"Reindex job {jobId} disappeared after its orchestration failed to start.",
                        ex);
                failed.Status = "Failed";
                failed.EndDate = DateTimeOffset.UtcNow;
                failed.ErrorMessage = $"Failed to start reindex orchestration: {ex.Message}";
                failed.HeartbeatDate = DateTimeOffset.UtcNow;
                await _jobRepository.UpdateAsync(failed, 1, ct);
                throw;
            }

            return new ReindexJobCreatedResult(jobId);
        }

        if (request.LockAlreadyHeld)
        {
            return await StartUnderLockAsync(cancellationToken);
        }

        return await _jobLock.ExecuteAsync<CreateReindexJobResult>(
            async ct =>
            {
                await _reconciler.ReconcileUnderLockAsync(ct);
                return await StartUnderLockAsync(ct);
            },
            cancellationToken);
    }

    private static ReindexJobDefinition CopyWithResolution(
        ReindexJobDefinition definition,
        long targetEventId,
        ReindexTargetResolution resolution,
        long consumedGeneration) =>
        new()
        {
            TenantId = definition.TenantId,
            TargetEventId = targetEventId,
            TenantIds = definition.TenantIds,
            ResourceTypes = resolution.ResourceTypes,
            SearchParameters = resolution.Targets.Select(target => new ReindexParameterDefinition(
                target.Canonical,
                target.Code,
                target.ResourceType,
                target.SearchParamId,
                target.ActivationEventId,
                target.AffectedResourceTypes)
            {
                ScheduledResourceTypes = target.ScheduledResourceTypes
            }).ToArray(),
            MaximumNumberOfResourcesPerQuery = definition.MaximumNumberOfResourcesPerQuery,
            MaximumNumberOfResourcesPerWrite = definition.MaximumNumberOfResourcesPerWrite,
            MaximumConcurrency = definition.MaximumConcurrency,
            QueryDelayIntervalInMilliseconds = definition.QueryDelayIntervalInMilliseconds,
            Trigger = definition.Trigger,
            ConsumedGeneration = consumedGeneration
        };
}
