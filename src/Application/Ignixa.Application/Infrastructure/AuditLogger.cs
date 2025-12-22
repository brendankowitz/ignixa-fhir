// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Default audit logger using structured logging.
/// Enables querying and alerting on security events via log aggregation.
/// Replace with custom implementation for SIEM, AuditEvent creation, etc.
/// </summary>
public partial class AuditLogger(ILogger<AuditLogger> logger) : IAuditLogger
{
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
                auditEvent.DurationMs);
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
                auditEvent.DurationMs);
        }
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
        Message = "AUDIT: Action={Action}, Outcome={Outcome}, User={UserId}, Client={ClientIp}, Method={Method}, Path={Path}, Status={StatusCode}, Duration={DurationMs}ms")]
    private static partial void LogHttpRequestSuccess(
        ILogger logger, string action, string outcome, string userId, string clientIp, string method, string path, int statusCode, double durationMs);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "AUDIT: Action={Action}, Outcome={Outcome}, User={UserId}, Client={ClientIp}, Method={Method}, Path={Path}, Status={StatusCode}, Duration={DurationMs}ms")]
    private static partial void LogHttpRequestFailure(
        ILogger logger, string action, string outcome, string userId, string clientIp, string method, string path, int statusCode, double durationMs);

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
