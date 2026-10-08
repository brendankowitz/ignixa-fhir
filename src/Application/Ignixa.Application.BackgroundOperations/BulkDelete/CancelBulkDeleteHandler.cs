// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.BulkDelete;

/// <summary>
/// Cancels a bulk-delete job. The orchestration is terminated first, so no further batch is scheduled;
/// a batch already running finishes its page and then finds the job Cancelled when recording progress.
/// </summary>
public sealed class CancelBulkDeleteHandler(
    TaskHubClient taskHubClient,
    IBackgroundJobRepository<BulkDeleteJobDefinition> jobRepository,
    ILogger<CancelBulkDeleteHandler> logger) : IRequestHandler<CancelBulkDeleteCommand, CancelBulkDeleteOutcome>
{
    public async Task<CancelBulkDeleteOutcome> HandleAsync(CancelBulkDeleteCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var job = await BulkDeleteJobs.FindAsync(jobRepository, request.TenantId, request.JobId, cancellationToken);
        if (job is null)
        {
            return CancelBulkDeleteOutcome.NotFound;
        }

        if (BulkDeleteJobs.IsTerminal(job.Status))
        {
            logger.LogInformation("Bulk delete cancellation for {JobId} is superseded by {Status}", request.JobId, job.Status);
            return CancelBulkDeleteOutcome.AlreadyTerminal;
        }

        // No execution ID: termination targets the instance's latest execution, which after ContinueAsNew is
        // the one doing the work.
        await taskHubClient.TerminateInstanceAsync(
            new OrchestrationInstance { InstanceId = job.OrchestrationInstanceId ?? job.JobId }, "Cancelled by user");

        // Reload after terminating so the cancelled job keeps the progress of every batch that recorded it.
        job = await BulkDeleteJobs.FindAsync(jobRepository, request.TenantId, request.JobId, cancellationToken)
            ?? throw new InvalidOperationException($"Bulk delete job {request.JobId} disappeared during cancellation.");
        if (BulkDeleteJobs.IsTerminal(job.Status))
        {
            return CancelBulkDeleteOutcome.AlreadyTerminal;
        }

        job.Status = "Cancelled";
        job.EndDate = DateTimeOffset.UtcNow;
        try
        {
            await jobRepository.UpdateAsync(job, request.TenantId, cancellationToken);
        }
        catch (BackgroundJobUpdateConflictException conflict)
        {
            logger.LogInformation(
                "Bulk delete cancellation for {JobId} lost to {Status}; preserving authoritative metadata",
                request.JobId, conflict.CurrentStatus);
            return CancelBulkDeleteOutcome.AlreadyTerminal;
        }

        logger.LogInformation("Bulk delete job {JobId} cancelled for tenant {TenantId}", request.JobId, request.TenantId);
        return CancelBulkDeleteOutcome.Accepted;
    }
}
