// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Authorization.Handlers;

/// <summary>
/// Authorization handler that enforces tenant isolation.
/// Ensures users can only access resources within their tenant.
/// SystemAdmin role bypasses tenant isolation.
/// Priority: 20 (runs after authentication).
/// </summary>
public class TenantIsolationHandler : IAuthorizationHandler
{
    private readonly IFhirRequestContextAccessor _fhirContextAccessor;
    private readonly ILogger<TenantIsolationHandler> _logger;
    private const string SystemAdminRole = "SystemAdmin";

    public TenantIsolationHandler(
        IFhirRequestContextAccessor fhirContextAccessor,
        ILogger<TenantIsolationHandler> logger)
    {
        _fhirContextAccessor = fhirContextAccessor ?? throw new ArgumentNullException(nameof(fhirContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public int Priority => 20;

    /// <inheritdoc />
    public ValueTask<AuthorizationResult> HandleAsync(
        FhirAuthorizationContext context,
        CancellationToken cancellationToken)
    {
        // System admin can access all tenants
        if (context.IsInRole(SystemAdminRole))
        {
            _logger.LogDebug(
                "Tenant isolation: SystemAdmin role bypasses tenant check for user {UserId}",
                context.UserId);
            return ValueTask.FromResult(AuthorizationResult.Success());
        }

        // User must have a tenant context
        if (string.IsNullOrEmpty(context.TenantId))
        {
            _logger.LogWarning(
                "Tenant isolation: Request denied - user {UserId} has no tenant context",
                context.UserId);
            return ValueTask.FromResult(AuthorizationResult.Denied("No tenant context"));
        }

        // Get tenant from FhirRequestContext (set by TenantResolutionMiddleware)
        var fhirContext = _fhirContextAccessor.RequestContext
            ?? throw new InvalidOperationException(
                "FhirRequestContext not set. Ensure TenantResolutionMiddleware runs before authorization.");

        var requestTenantId = fhirContext.TenantId.ToString();

        // If request targets a specific tenant, validate access
        if (!string.IsNullOrEmpty(requestTenantId) &&
            !string.Equals(requestTenantId, context.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Tenant isolation: Request denied - user {UserId} (tenant {UserTenant}) attempted to access tenant {RequestTenant}",
                context.UserId,
                context.TenantId,
                requestTenantId);
            return ValueTask.FromResult(AuthorizationResult.TenantAccessDenied(requestTenantId));
        }

        _logger.LogDebug(
            "Tenant isolation: User {UserId} authorized for tenant {TenantId}",
            context.UserId,
            context.TenantId);

        return ValueTask.FromResult(AuthorizationResult.Success());
    }
}
