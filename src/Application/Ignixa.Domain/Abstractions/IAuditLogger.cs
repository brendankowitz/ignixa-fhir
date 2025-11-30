// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Audit logger for tracking tenant access and security events.
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Logs a tenant access attempt (authorized or unauthorized).
    /// </summary>
    /// <param name="userId">User identifier (from claims)</param>
    /// <param name="tenantId">Tenant identifier being accessed</param>
    /// <param name="operation">Operation type (GET, PUT, DELETE, SEARCH, etc.)</param>
    /// <param name="resourceType">FHIR resource type</param>
    /// <param name="resourceId">Resource ID (if applicable)</param>
    /// <param name="authorized">Whether the access was authorized</param>
    void LogTenantAccess(
        string userId,
        int tenantId,
        string operation,
        string resourceType,
        string? resourceId,
        bool authorized);
}
