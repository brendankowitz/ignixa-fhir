// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Ignixa.Application.Infrastructure.Audit;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Default audit logger using structured logging.
/// Enables querying and alerting on security events via log aggregation.
/// Replace with custom implementation for SIEM, AuditEvent creation, etc.
/// </summary>
public partial class AuditLogger(ILogger<AuditLogger> logger) : IAuditLogger
{
    private const string HttpRequestTemplate =
        "AUDIT: Action={Action}, Outcome={Outcome}, User={UserId}, Client={ClientIp}, Method={Method}, Path={Path}, Status={StatusCode}, Duration={DurationMs}ms, CustomHeaders={CustomHeaders}";

    public void LogTenantAccess(
        string userId,
        int tenantId,
        string operation,
        string resourceType,
        string? resourceId,
        bool authorized)
    {
        if (authorized)
        {
            LogTenantAccessAuthorized(logger, userId, tenantId, operation, resourceType, resourceId ?? "(none)");
        }
        else
        {
            LogTenantAccessDenied(logger, userId, tenantId, operation, resourceType, resourceId ?? "(none)");
        }
    }

    public void LogHttpRequest(HttpRequestAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var customHeaders = CustomAuditHeaders.Format(auditEvent.CustomHeaders);

        if (auditEvent.Outcome == "0")
        {
            LogHttpRequestSuccess(
                logger,
                auditEvent.Action,
                auditEvent.Outcome,
                auditEvent.UserId,
                auditEvent.ClientIp,
                auditEvent.Method,
                auditEvent.Path,
                auditEvent.StatusCode,
                auditEvent.DurationMs,
                customHeaders);
        }
        else
        {
            LogHttpRequestFailure(
                logger,
                auditEvent.Action,
                auditEvent.Outcome,
                auditEvent.UserId,
                auditEvent.ClientIp,
                auditEvent.Method,
                auditEvent.Path,
                auditEvent.StatusCode,
                auditEvent.DurationMs,
                customHeaders);
        }
    }

    public void LogBackgroundJobCompleted(BackgroundJobAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        LogBackgroundJob(
            logger,
            auditEvent.Outcome == "0" ? LogLevel.Information : LogLevel.Warning,
            auditEvent.JobType,
            auditEvent.JobId,
            auditEvent.TenantId,
            auditEvent.Status,
            auditEvent.Outcome,
            auditEvent.UserId,
            auditEvent.CorrelationId ?? string.Empty,
            CustomAuditHeaders.Format(auditEvent.CustomHeaders));
    }

    public void LogTtlDeletion(
        int tenantId,
        string resourceType,
        string resourceId,
        DateTimeOffset expiresAt,
        bool success)
    {
        if (success)
        {
            LogTtlDeletionSuccess(logger, tenantId, resourceType, resourceId, expiresAt);
        }
        else
        {
            LogTtlDeletionFailure(logger, tenantId, resourceType, resourceId, expiresAt);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "AUDIT: Tenant access AUTHORIZED - User={UserId}, Tenant={TenantId}, Operation={Operation}, Resource={ResourceType}/{ResourceId}")]
    private static partial void LogTenantAccessAuthorized(
        ILogger logger, string userId, int tenantId, string operation, string resourceType, string resourceId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "AUDIT: Tenant access DENIED - User={UserId}, Tenant={TenantId}, Operation={Operation}, Resource={ResourceType}/{ResourceId}")]
    private static partial void LogTenantAccessDenied(
        ILogger logger, string userId, int tenantId, string operation, string resourceType, string resourceId);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = HttpRequestTemplate)]
    private static partial void LogHttpRequestSuccess(
        ILogger logger, string action, string outcome, string userId, string clientIp, string method, string path, int statusCode, double durationMs, string customHeaders);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = HttpRequestTemplate)]
    private static partial void LogHttpRequestFailure(
        ILogger logger, string action, string outcome, string userId, string clientIp, string method, string path, int statusCode, double durationMs, string customHeaders);

    [LoggerMessage(
        Message = "AUDIT: Background job {JobType} {JobId} finished - Tenant={TenantId}, Status={Status}, Outcome={Outcome}, User={UserId}, CorrelationId={CorrelationId}, CustomHeaders={CustomHeaders}")]
    private static partial void LogBackgroundJob(
        ILogger logger, LogLevel level, string jobType, string jobId, int tenantId, string status, string outcome, string userId, string correlationId, string customHeaders);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "AUDIT: TTL DELETION SUCCESS - Tenant={TenantId}, Resource={ResourceType}/{ResourceId}, ExpiredAt={ExpiresAt}, Reason=TTL_EXPIRATION")]
    private static partial void LogTtlDeletionSuccess(
        ILogger logger, int tenantId, string resourceType, string resourceId, DateTimeOffset expiresAt);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "AUDIT: TTL DELETION FAILURE - Tenant={TenantId}, Resource={ResourceType}/{ResourceId}, ExpiredAt={ExpiresAt}, Reason=TTL_EXPIRATION")]
    private static partial void LogTtlDeletionFailure(
        ILogger logger, int tenantId, string resourceType, string resourceId, DateTimeOffset expiresAt);
}
