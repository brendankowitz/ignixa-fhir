// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkDelete.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.BulkDelete.Activities;

/// <summary>
/// Records a bulk-delete job's terminal outcome: Completed, or Failed with the error as an issue.
/// Returns true when this outcome was persisted and false when the job was already terminal (typically
/// cancelled), whose first terminal state stays authoritative.
/// </summary>
public class CompleteBulkDeleteJobActivity(
    IBackgroundJobRepository<BulkDeleteJobDefinition> jobRepository,
    ILogger<CompleteBulkDeleteJobActivity> logger) : AsyncTaskActivity<CompleteBulkDeleteJobInput, bool>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    protected override async Task<bool> ExecuteAsync(TaskContext context, CompleteBulkDeleteJobInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var job = await BulkDeleteJobs.FindAsync(jobRepository, input.TenantId, input.JobId, CancellationToken.None)
            ?? throw new InvalidOperationException($"Bulk delete job {input.JobId} not found for tenant {input.TenantId}.");
        if (BulkDeleteJobs.IsTerminal(job.Status))
        {
            logger.LogInformation("Bulk delete completion for {JobId} is superseded by {Status}", input.JobId, job.Status);
            return false;
        }

        var counts = new Dictionary<string, long>(input.ResourceDeletedCount, StringComparer.Ordinal);
        job.Status = input.Success ? "Completed" : "Failed";
        job.EndDate = DateTimeOffset.UtcNow;
        job.ErrorMessage = input.Success
            ? null
            : input.ErrorMessage ?? throw new ArgumentException("A failed bulk delete requires an error message.", nameof(input));
        job.Progress = JsonSerializer.SerializeToNode(new BulkDeleteJobProgress { ResourceDeletedCount = counts }, SerializerOptions);
        job.Result = JsonSerializer.SerializeToNode(new BulkDeleteJobResult
        {
            ResourceDeletedCount = counts,
            Issues = input.Success ? [] : [job.ErrorMessage!],
        }, SerializerOptions);

        try
        {
            await jobRepository.UpdateAsync(job, input.TenantId, CancellationToken.None);
        }
        catch (BackgroundJobUpdateConflictException conflict)
        {
            logger.LogInformation(
                "Bulk delete completion for {JobId} lost to {Status}; preserving authoritative metadata",
                input.JobId, conflict.CurrentStatus);
            return false;
        }

        if (input.Success)
        {
            logger.LogInformation(
                "Bulk delete job {JobId} completed: {DeletedCount} resource(s) deleted across {TypeCount} type(s)",
                input.JobId, counts.Values.Sum(), counts.Count);
        }
        else
        {
            logger.LogError("Bulk delete job {JobId} failed: {Error}", input.JobId, job.ErrorMessage);
        }

        return true;
    }
}
