// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Ignixa.Application.Features.Authorization.Smart;
using Ignixa.Application.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Ignixa.Application.Features.Authorization.Models;

/// <summary>
/// Per-request authorization context containing all information needed for authorization decisions.
/// Combines user identity, tenant context, SMART scopes, and request details.
/// </summary>
public record FhirAuthorizationContext
{
    // ========== Request Context ==========

    /// <summary>
    /// The FHIR request context (tenant, version, correlation).
    /// Set by TenantResolutionMiddleware.
    /// </summary>
    public required IFhirRequestContext RequestContext { get; init; }

    // ========== User Identity ==========

    /// <summary>
    /// User ID from authentication (sub claim, oid, etc.).
    /// Null for unauthenticated requests or system accounts.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Tenant ID from route or claims.
    /// For convenience, delegates to RequestContext.TenantId.
    /// </summary>
    public string? TenantId => RequestContext?.TenantId.ToString();

    /// <summary>
    /// Roles assigned to the user (from role claims or database lookup).
    /// </summary>
    public IReadOnlyList<string>? Roles { get; init; }

    // ========== SMART Context ==========

    /// <summary>
    /// SMART authorization context (if authenticated via OAuth with SMART scopes).
    /// Null for non-SMART authentication (e.g., basic auth, API key, system account).
    /// </summary>
    public SmartAuthorizationContext? SmartContext { get; init; }

    // ========== Request Details ==========

    /// <summary>
    /// The FHIR interaction being attempted.
    /// </summary>
    public required FhirInteraction Interaction { get; init; }

    /// <summary>
    /// Resource type from the route (e.g., "Patient", "Observation").
    /// Null for system-level operations (e.g., /$export, /metadata).
    /// </summary>
    public string? ResourceType { get; init; }

    /// <summary>
    /// Resource ID from the route (e.g., "123", "patient-123").
    /// Null for create operations and searches.
    /// </summary>
    public string? ResourceId { get; init; }

    /// <summary>
    /// Operation name for FHIR operations (e.g., "export", "validate").
    /// </summary>
    public string? OperationName { get; init; }

    /// <summary>
    /// HTTP context for accessing request details.
    /// </summary>
    public required HttpContext HttpContext { get; init; }

    /// <summary>
    /// Timestamp of the authorization request.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    // ========== Convenience Properties ==========

    /// <summary>
    /// Indicates whether the user is authenticated.
    /// </summary>
    public bool IsAuthenticated => UserId != null || SmartContext != null;

    /// <summary>
    /// Indicates whether the request has SMART scopes.
    /// </summary>
    public bool HasSmartScopes => SmartContext?.Scopes.Count > 0;

    /// <summary>
    /// Indicates whether the user has any roles assigned.
    /// </summary>
    public bool HasRoles => Roles != null && Roles.Count > 0;

    /// <summary>
    /// Checks if the user has a specific role.
    /// </summary>
    /// <param name="role">The role to check.</param>
    /// <returns>True if the user has the role.</returns>
    public bool IsInRole(string role) =>
        Roles?.Contains(role, StringComparer.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Gets the required permission for this request as a ResourceGrant.
    /// </summary>
    public ResourceGrant RequiredPermission =>
        new(ResourceType ?? "*", Interaction.ToFhirCode());
}
