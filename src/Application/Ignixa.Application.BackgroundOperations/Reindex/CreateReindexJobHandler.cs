using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Orchestrations;
using Ignixa.Application.Features.Conformance;
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
    IFhirRepositoryFactory repositoryFactory,
    IReindexJobLock jobLock,
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
    private readonly IFhirRepositoryFactory _repositoryFactory =
        repositoryFactory ?? throw new ArgumentNullException(nameof(repositoryFactory));
    private readonly IReindexJobLock _jobLock =
        jobLock ?? throw new ArgumentNullException(nameof(jobLock));
    private readonly ReindexOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));

    public async Task<CreateReindexJobResult> HandleAsync(
        CreateReindexJobCommand request,
        CancellationToken cancellationToken)
    {
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
            var repository = await _repositoryFactory.GetRepositoryAsync(
                tenant.TenantId,
                cancellationToken);
            if (repository is not IReindexStore)
            {
                return new ReindexProviderUnavailableResult(tenant.TenantId);
            }

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

        return await _jobLock.ExecuteAsync<CreateReindexJobResult>(
            async ct =>
            {
                var jobs = await _jobRepository.ListAsync(
                    (int)BackgroundJobType.Reindex,
                    ct);
                var activeJobs = jobs.Where(job =>
                    job.Status.Equals("Queued", StringComparison.OrdinalIgnoreCase) ||
                    job.Status.Equals("Running", StringComparison.OrdinalIgnoreCase) ||
                    job.Status.Equals("Completing", StringComparison.OrdinalIgnoreCase));
                foreach (var active in activeJobs)
                {
                    if (active.Status.Equals("Completing", StringComparison.OrdinalIgnoreCase))
                    {
                        return new ActiveReindexJobResult(active.JobId);
                    }

                    var state = await _taskHubClient.GetOrchestrationStateAsync(
                        active.OrchestrationInstanceId ?? active.JobId);
                    if (state?.OrchestrationStatus is OrchestrationStatus.Pending
                        or OrchestrationStatus.Running
                        or OrchestrationStatus.ContinuedAsNew)
                    {
                        return new ActiveReindexJobResult(active.JobId);
                    }

                    active.Status = "Failed";
                    active.EndDate = DateTimeOffset.UtcNow;
                    active.HeartbeatDate = DateTimeOffset.UtcNow;
                    active.ErrorMessage = state is null
                        ? "Reindex orchestration instance is missing."
                        : $"Reindex orchestration ended as {state.OrchestrationStatus} before the job was finalized.";
                    await _jobRepository.UpdateAsync(active, 1, ct);
                }

                long targetEventId;
                ReindexTargetResolution resolution;
                using (await _conformanceState.AcquireActivationLockAsync(ct))
                {
                    targetEventId = _conformanceState.LastProcessedEventId;
                    resolution = ReindexTargetResolver.Resolve(
                        _conformanceState.AllSearchParameters.Values.ToArray(),
                        concreteResourceTypes,
                        request.TargetResourceTypes,
                        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Resource"] = concreteResourceTypes,
                            ["DomainResource"] = domainResourceTypes
                        });
                }
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
                    Trigger = request.Trigger
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
                            _options.ContinueAsNewThreshold));
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
            },
            cancellationToken);
    }
}
