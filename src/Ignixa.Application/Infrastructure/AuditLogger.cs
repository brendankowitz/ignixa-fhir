// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Ignixa.Domain.Abstractions;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Audit logger for tracking tenant access and security events.
/// Uses structured logging to enable querying and alerting on security events.
/// </summary>
public class AuditLogger : IAuditLogger
{
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(ILogger<AuditLogger> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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
            _logger.LogInformation(
                "AUDIT: Tenant access AUTHORIZED - User={UserId}, Tenant={TenantId}, " +
                "Operation={Operation}, Resource={ResourceType}/{ResourceId}",
                userId,
                tenantId,
                operation,
                resourceType,
                resourceId ?? "(none)");
        }
        else
        {
            _logger.LogWarning(
                "AUDIT: Tenant access DENIED - User={UserId}, Tenant={TenantId}, " +
                "Operation={Operation}, Resource={ResourceType}/{ResourceId}",
                userId,
                tenantId,
                operation,
                resourceType,
                resourceId ?? "(none)");
        }
    }
}
