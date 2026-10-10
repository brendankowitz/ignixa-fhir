using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Orchestrations;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Constants;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// Creates a reindex job: under the singleton job lock, an active job wins, the targets are resolved, the
/// row is written and the orchestration is started. Availability is the caller's concern.
/// </summary>
public sealed class CreateReindexJobHandler(
    TaskHubClient taskHubClient,
    IBackgroundJobRepository<ReindexJobDefinition> jobRepository,
    IReindexJobLock jobLock,
    ReindexTargetResolver targetResolver,
    IOptions<ReindexOptions> options,
    TimeProvider timeProvider)
    : IRequestHandler<CreateReindexJobCommand, CreateReindexJobResult>
{
    private readonly TaskHubClient _taskHubClient =
        taskHubClient ?? throw new ArgumentNullException(nameof(taskHubClient));
    private readonly IBackgroundJobRepository<ReindexJobDefinition> _jobRepository =
        jobRepository ?? throw new ArgumentNullException(nameof(jobRepository));
    private readonly IReindexJobLock _jobLock =
        jobLock ?? throw new ArgumentNullException(nameof(jobLock));
    private readonly ReindexTargetResolver _targetResolver =
        targetResolver ?? throw new ArgumentNullException(nameof(targetResolver));
    private readonly ReindexOptions _options =
        options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly TimeProvider _timeProvider =
        timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

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

        return await _jobLock.ExecuteAsync(
            ct => StartUnderLockAsync(request, parameters, ct),
            cancellationToken);
    }

    private async Task<CreateReindexJobResult> StartUnderLockAsync(
        CreateReindexJobCommand request,
        ReindexJobParameters parameters,
        CancellationToken cancellationToken)
    {
        var active = await _jobRepository.GetActiveAsync(
            (int)BackgroundJobType.Reindex,
            cancellationToken);
        if (active is not null)
        {
            return new ActiveReindexJobResult(active.JobId);
        }

        var plan = await _targetResolver.ResolveAsync(cancellationToken);
        if (plan.TenantIds.Count == 0)
        {
            return new NoReindexWorkResult("No active tenant is configured for reindexing.");
        }

        if (!plan.Resolution.HasWork)
        {
            return new NoReindexWorkResult("No resources need reindexing.");
        }

        if (request.IsAutomatic &&
            await GetLatestFinishedAsync(cancellationToken) is { } finished &&
            !ReindexStartRule.RequiresJob(
                plan.Resolution.Targets.Select(target => target.ActivationEventId),
                finished.Definition.TargetEventId))
        {
            return new NoReindexWorkResult(
                $"Pending parameters were already targeted by {finished.Status} job {finished.JobId}; a newer activation or a manual $reindex starts the next job.");
        }

        var jobId = Guid.NewGuid().ToString();
        var definition = new ReindexJobDefinition
        {
            TargetEventId = plan.TargetEventId,
            TenantIds = plan.TenantIds,
            ResourceTypes = plan.Resolution.ResourceTypes,
            SearchParameters = plan.Resolution.Targets,
            MaximumNumberOfResourcesPerQuery = parameters.MaximumNumberOfResourcesPerQuery,
            MaximumNumberOfResourcesPerWrite = parameters.MaximumNumberOfResourcesPerWrite,
            MaximumConcurrency = parameters.MaximumConcurrency,
            QueryDelayIntervalInMilliseconds = parameters.QueryDelayIntervalInMilliseconds,
            Trigger = request.Trigger.ToString()
        };
        var now = _timeProvider.GetUtcNow();
        await _jobRepository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = jobId,
            OrchestrationInstanceId = jobId,
            JobType = (int)BackgroundJobType.Reindex,
            Status = nameof(ReindexJobStatus.Queued),
            Definition = definition,
            Progress = ReindexProgress.Create([]).ToJson(),
            CreateDate = now,
            HeartbeatDate = now
        }, cancellationToken);

        try
        {
            await _taskHubClient.CreateOrchestrationInstanceAsync(
                typeof(ReindexOrchestration),
                jobId,
                new ReindexOrchestrationInput(
                    jobId,
                    plan.TargetEventId,
                    _options.BarrierDelay,
                    definition.TenantIds,
                    definition.ResourceTypes,
                    plan.Resolution.Targets,
                    parameters,
                    _options.DrainWarningAfter,
                    _options.ContinueAsNewThreshold)
                {
                    StaleJobTimeout = _options.StaleJobTimeout
                });
        }
        catch
        {
            // A row without an orchestration must not survive: as the latest finished job it would block
            // the next automatic start, and as an active job it would answer every $reindex with 409.
            await _jobRepository.DeleteAsync(jobId, SystemConstants.GlobalTenantId, CancellationToken.None);
            throw;
        }

        return new ReindexJobCreatedResult(jobId);
    }

    private Task<BackgroundJob<ReindexJobDefinition>?> GetLatestFinishedAsync(CancellationToken cancellationToken) =>
        _jobRepository.GetLatestAsync((int)BackgroundJobType.Reindex, ReindexJobs.FinishedStatuses, cancellationToken);
}
