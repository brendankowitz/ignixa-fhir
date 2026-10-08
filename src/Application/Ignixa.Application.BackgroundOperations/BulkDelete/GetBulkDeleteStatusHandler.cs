// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using DurableTask.Core;
using DurableTask.Core.Serializing;
using Ignixa.Application.BackgroundOperations.BulkDelete.Orchestrations;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.BulkDelete;

/// <summary>
/// Reads a bulk-delete job's status. Terminal job metadata is authoritative; a non-terminal job is first
/// reconciled with its orchestration so a crash between the orchestration finishing and the job row being
/// finalized still surfaces as a terminal outcome.
/// </summary>
/// <exception cref="KeyNotFoundException">No bulk-delete job with this ID exists for the tenant.</exception>
/// <exception cref="JsonException">The job's persisted progress or result is invalid.</exception>
public sealed class GetBulkDeleteStatusHandler(
    TaskHubClient taskHubClient,
    IBackgroundJobRepository<BulkDeleteJobDefinition> jobRepository,
    ILogger<GetBulkDeleteStatusHandler> logger) : IRequestHandler<GetBulkDeleteStatusQuery, GetBulkDeleteStatusResult>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<GetBulkDeleteStatusResult> HandleAsync(GetBulkDeleteStatusQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var job = await FindAsync(request, cancellationToken);
        if (!BulkDeleteJobs.IsTerminal(job.Status))
        {
            try
            {
                await ReconcileAsync(job, request.TenantId, cancellationToken);
            }
            catch (BackgroundJobUpdateConflictException conflict)
            {
                logger.LogInformation(
                    "Bulk delete status refresh for {JobId} was superseded by {Status}; reloading", request.JobId, conflict.CurrentStatus);
            }

            // Reload: an activity may have recorded progress or an outcome since the first read.
            job = await BulkDeleteJobs.FindAsync(jobRepository, request.TenantId, request.JobId, cancellationToken)
                ?? throw new InvalidOperationException($"Bulk delete job {request.JobId} disappeared during status refresh.");
        }

        return ToResult(job);
    }

    private async Task<BackgroundJob<BulkDeleteJobDefinition>> FindAsync(GetBulkDeleteStatusQuery request, CancellationToken cancellationToken) =>
        await BulkDeleteJobs.FindAsync(jobRepository, request.TenantId, request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Bulk delete job '{request.JobId}' not found for tenant {request.TenantId}.");

    private async Task ReconcileAsync(BackgroundJob<BulkDeleteJobDefinition> job, int tenantId, CancellationToken cancellationToken)
    {
        var state = await taskHubClient.GetOrchestrationStateAsync(job.OrchestrationInstanceId ?? job.JobId);
        if (state is null)
        {
            return;
        }

        switch (state.OrchestrationStatus)
        {
            // ContinuedAsNew is the status of an execution that handed over to the next one; the instance
            // is still working.
            case OrchestrationStatus.Running or OrchestrationStatus.ContinuedAsNew when job.Status != "Running":
                job.Status = "Running";
                job.StartDate ??= DateTimeOffset.UtcNow;
                break;

            case OrchestrationStatus.Completed:
                var output = ReadOutput(state);
                if (output.Superseded)
                {
                    // The terminal state that superseded the orchestration is already persisted.
                    return;
                }

                Finish(job, output.Success, output.ResourceDeletedCount,
                    output.Success ? null : output.ErrorMessage ?? "Bulk delete failed.");
                break;

            case OrchestrationStatus.Failed:
                Finish(job, success: false, ReadProgress(job),
                    state.FailureDetails?.ErrorMessage ?? state.Output ?? "Bulk delete orchestration failed.");
                break;

            case OrchestrationStatus.Terminated:
                job.Status = "Cancelled";
                job.EndDate ??= DateTimeOffset.UtcNow;
                break;

            default:
                return;
        }

        await jobRepository.UpdateAsync(job, tenantId, cancellationToken);
    }

    /// <summary>
    /// Reads the orchestration output. DurableTask wrote it with its own converter (Json.NET with
    /// <c>$type</c> metadata, which System.Text.Json cannot read into a dictionary), so it is read back with
    /// the same converter; a malformed output is reported as <see cref="JsonException"/> like any other
    /// invalid persisted record.
    /// </summary>
    private static BulkDeleteOrchestrationOutput ReadOutput(OrchestrationState state)
    {
        try
        {
            return JsonDataConverter.Default.Deserialize<BulkDeleteOrchestrationOutput>(state.Output ?? "null")
                ?? throw new JsonException("Bulk delete orchestration completed without an output.");
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            throw new JsonException("Bulk delete orchestration output is invalid.", ex);
        }
    }

    private static void Finish(
        BackgroundJob<BulkDeleteJobDefinition> job,
        bool success,
        IReadOnlyDictionary<string, long> counts,
        string? errorMessage)
    {
        var resourceDeletedCount = new Dictionary<string, long>(counts, StringComparer.Ordinal);
        job.Status = success ? "Completed" : "Failed";
        job.EndDate ??= DateTimeOffset.UtcNow;
        job.ErrorMessage = errorMessage;
        job.Result = JsonSerializer.SerializeToNode(new BulkDeleteJobResult
        {
            ResourceDeletedCount = resourceDeletedCount,
            Issues = errorMessage is null ? [] : [errorMessage],
        }, SerializerOptions);
    }

    private static GetBulkDeleteStatusResult ToResult(BackgroundJob<BulkDeleteJobDefinition> job)
    {
        var result = job.Result is null
            ? null
            : job.Result.Deserialize<BulkDeleteJobResult>(SerializerOptions)
                ?? throw new JsonException($"Bulk delete job {job.JobId} has a null result.");
        if (result is null && job.Status is "Completed" or "Failed")
        {
            throw new JsonException($"{job.Status} bulk delete job {job.JobId} has no persisted result.");
        }

        // A cancelled job has no result: its counts are the progress recorded by completed batches.
        var counts = result?.ResourceDeletedCount ?? ReadProgress(job);
        if (counts.Values.Any(count => count < 0))
        {
            throw new JsonException($"Bulk delete job {job.JobId} has a negative deleted count.");
        }

        return new GetBulkDeleteStatusResult(
            job.Status,
            counts.Where(count => count.Value > 0).ToDictionary(StringComparer.Ordinal),
            result?.Issues ?? [],
            job.Status == "Failed" ? job.ErrorMessage : null);
    }

    private static IReadOnlyDictionary<string, long> ReadProgress(BackgroundJob<BulkDeleteJobDefinition> job) =>
        job.Progress is null
            ? new Dictionary<string, long>()
            : (job.Progress.Deserialize<BulkDeleteJobProgress>(SerializerOptions)
                ?? throw new JsonException($"Bulk delete job {job.JobId} has a null progress record.")).ResourceDeletedCount;
}
