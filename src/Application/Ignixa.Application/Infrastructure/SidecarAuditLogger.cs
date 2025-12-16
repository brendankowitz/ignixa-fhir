// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Ignixa.Domain.Abstractions;
using Ignixa.Sidecar.Audit;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Audit logger implementation that forwards events to sidecar gRPC service.
/// Fail-fast: Throws RpcException if sidecar is unavailable (returns 503 to client).
/// </summary>
public class SidecarAuditLogger(
    AuditService.AuditServiceClient client,
    ILogger<SidecarAuditLogger> logger) : IAuditLogger
{
    public void LogTenantAccess(
        string userId,
        int tenantId,
        string operation,
        string resourceType,
        string? resourceId,
        bool authorized)
    {
        // Fire-and-forget: Queue for async processing
        _ = LogTenantAccessAsync(userId, tenantId, operation, resourceType, resourceId, authorized);
    }

    public void LogHttpRequest(HttpRequestAuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        // Fire-and-forget: Queue for async processing
        _ = LogHttpRequestAsync(auditEvent);
    }

    private async Task LogTenantAccessAsync(
        string userId,
        int tenantId,
        string operation,
        string resourceType,
        string? resourceId,
        bool authorized)
    {
        try
        {
            var request = new AuditEventRequest
            {
                Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
                TenantId = tenantId.ToString(),
                UserId = userId,
                ResourceType = resourceType,
                ResourceId = resourceId ?? string.Empty,
                Operation = ParseAuditOperation(operation),
                Success = authorized,
                HttpStatusCode = authorized ? 200 : 403
            };

            await client.LogAuditEventAsync(request);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            logger.LogError(ex, "Audit sidecar unavailable - audit event lost");
            // Don't rethrow - audit failures should not block requests
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to log tenant access audit event");
        }
    }

    private async Task LogHttpRequestAsync(HttpRequestAuditEvent auditEvent)
    {
        try
        {
            var request = new AuditEventRequest
            {
                Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
                TenantId = auditEvent.TenantId?.ToString() ?? "0",
                UserId = auditEvent.UserId,
                ResourceType = auditEvent.ResourceType ?? string.Empty,
                ResourceId = auditEvent.ResourceId ?? string.Empty,
                Operation = ParseAuditOperation(auditEvent.Method),
                HttpStatusCode = auditEvent.StatusCode,
                Success = auditEvent.Outcome == "0",
                IpAddress = auditEvent.ClientIp,
                CorrelationId = auditEvent.CorrelationId ?? string.Empty
            };

            request.CustomProperties.Add("action", auditEvent.Action);
            request.CustomProperties.Add("outcome", auditEvent.Outcome);
            request.CustomProperties.Add("path", auditEvent.Path);
            request.CustomProperties.Add("durationMs", auditEvent.DurationMs.ToString());

            await client.LogAuditEventAsync(request);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            logger.LogError(ex, "Audit sidecar unavailable - audit event lost");
            // Don't rethrow - audit failures should not block requests
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to log HTTP request audit event");
        }
    }

    private static AuditOperation ParseAuditOperation(string operation)
    {
        // Handle both FHIR operations (read, create, etc.) and HTTP methods (GET, POST, etc.)
        return operation.ToUpperInvariant() switch
        {
            "READ" or "GET" => AuditOperation.Read,
            "VREAD" => AuditOperation.Vread,
            "CREATE" or "POST" => AuditOperation.Create,
            "UPDATE" or "PUT" => AuditOperation.Update,
            "PATCH" => AuditOperation.Patch,
            "DELETE" => AuditOperation.Delete,
            "SEARCH" => AuditOperation.Search,
            "HISTORY" => AuditOperation.History,
            _ => AuditOperation.Unspecified
        };
    }
}
