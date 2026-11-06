// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.BackgroundOperations.Jobs;

/// <summary>
/// Emits the terminal audit event for a background job, attributed to the request that started it.
/// Call only after this writer persisted the terminal status: repositories reject updates to a job that is
/// already terminal, so only the first terminal writer emits. Delivery is best-effort (at most once).
/// </summary>
public sealed class BackgroundJobCompletionAuditor(
    IAuditLogger auditLogger,
    ILogger<BackgroundJobCompletionAuditor> logger)
{
    /// <summary>
    /// User identifier for jobs without persisted audit attribution (created before audit capture).
    /// </summary>
    public const string UnknownUser = "unknown";

    /// <summary>
    /// Emits the completion audit event. Never throws: the terminal status is already committed, so an audit
    /// failure is logged with the job's identity instead of failing the caller's completed work.
    /// </summary>
    public void LogTerminalStatus<T>(BackgroundJob<T> job)
        where T : class, IAuditedJobDefinition
    {
        var jobType = ((BackgroundJobType)job.JobType).ToString();
        var outcome = job.Status switch
        {
            "Completed" => "0",
            "Cancelled" => "4",
            "Failed" => "8",
            _ => null
        };

        if (outcome is null)
        {
            logger.LogError(
                "Completion audit for {JobType} job {JobId} skipped: status {Status} is not terminal",
                jobType, job.JobId, job.Status);
            return;
        }

        var auditContext = job.Definition.AuditContext;
        if (auditContext is null)
        {
            logger.LogWarning(
                "{JobType} job {JobId} has no audit attribution; completion is audited as {UserId}",
                jobType, job.JobId, UnknownUser);
        }

        try
        {
            auditLogger.LogBackgroundJobCompleted(new BackgroundJobAuditEvent
            {
                JobType = jobType,
                JobId = job.JobId,
                TenantId = job.Definition.TenantId,
                Status = job.Status,
                Outcome = outcome,
                UserId = auditContext?.UserId ?? UnknownUser,
                CorrelationId = auditContext?.CorrelationId,
                DurationMs = job.EndDate is { } endDate ? (endDate - job.CreateDate).TotalMilliseconds : null,
                CustomHeaders = auditContext?.CustomHeaders ?? new Dictionary<string, string>()
            });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Completion audit event for {JobType} job {JobId} (tenant {TenantId}, status {Status}) was lost",
                jobType, job.JobId, job.Definition.TenantId, job.Status);
        }
    }
}
