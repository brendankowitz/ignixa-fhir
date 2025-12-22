// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Audit logger for tracking FHIR operations and security events.
/// Implementations can log to structured logging, create AuditEvent resources,
/// or forward to external audit systems (SIEM, Azure Monitor, etc.).
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

    /// <summary>
    /// Logs an HTTP request audit event.
    /// Maps to FHIR AuditEvent resource structure.
    /// </summary>
    /// <param name="auditEvent">The audit event details</param>
    void LogHttpRequest(HttpRequestAuditEvent auditEvent);

    /// <summary>
    /// Logs a resource deletion due to TTL expiration.
    /// Used by background cleanup jobs to track automatic deletions.
    /// </summary>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="resourceType">FHIR resource type</param>
    /// <param name="resourceId">Resource ID</param>
    /// <param name="expiresAt">Original expiration timestamp</param>
    /// <param name="success">Whether the deletion succeeded</param>
    void LogTtlDeletion(
        int tenantId,
        string resourceType,
        string resourceId,
        DateTimeOffset expiresAt,
        bool success);
}

/// <summary>
/// Represents an HTTP request audit event with FHIR AuditEvent-compatible fields.
/// </summary>
public sealed record HttpRequestAuditEvent
{
    /// <summary>
    /// FHIR AuditEvent.action (C=Create, R=Read, U=Update, D=Delete, E=Execute).
    /// </summary>
    public required string Action { get; init; }

    /// <summary>
    /// FHIR AuditEvent.outcome (0=Success, 4=Minor failure, 8=Serious failure, 12=Major failure).
    /// </summary>
    public required string Outcome { get; init; }

    /// <summary>
    /// User identifier (from JWT claims: sub, oid, or name_id).
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Client IP address.
    /// </summary>
    public required string ClientIp { get; init; }

    /// <summary>
    /// HTTP method (GET, POST, PUT, PATCH, DELETE).
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// Request path.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// HTTP response status code.
    /// </summary>
    public required int StatusCode { get; init; }

    /// <summary>
    /// Request duration in milliseconds.
    /// </summary>
    public required double DurationMs { get; init; }

    /// <summary>
    /// Tenant ID (if applicable).
    /// </summary>
    public int? TenantId { get; init; }

    /// <summary>
    /// FHIR resource type (if applicable).
    /// </summary>
    public string? ResourceType { get; init; }

    /// <summary>
    /// FHIR resource ID (if applicable).
    /// </summary>
    public string? ResourceId { get; init; }

    /// <summary>
    /// Correlation ID for request tracing.
    /// </summary>
    public string? CorrelationId { get; init; }
}
