// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Mcp.Authorization;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;

namespace Ignixa.Application.Features.Mcp.Tools;

/// <summary>
/// Base class for MCP tools that require tenant context resolution and authorization.
/// Provides three-tier tenant resolution strategy:
/// 1. Explicit tenantId parameter (highest priority)
/// 2. IFhirRequestContext.TenantId from middleware
/// 3. Default to single tenant if only one active tenant exists
///
/// All MCP tools require appropriate role-based authorization (Admin, SystemAdmin, Mcp, or Contributor).
/// </summary>
public abstract class TenantAwareMcpTool
{
    private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly IMcpAuthorizationService? _mcpAuthorizationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantAwareMcpTool"/> class.
    /// </summary>
    /// <param name="fhirRequestContextAccessor">FHIR request context accessor for tenant resolution.</param>
    /// <param name="tenantConfigurationStore">Tenant configuration store.</param>
    /// <param name="mcpAuthorizationService">MCP authorization service (optional for backward compatibility).</param>
    protected TenantAwareMcpTool(
        IFhirRequestContextAccessor fhirRequestContextAccessor,
        ITenantConfigurationStore tenantConfigurationStore,
        IMcpAuthorizationService? mcpAuthorizationService = null)
    {
        _fhirRequestContextAccessor = fhirRequestContextAccessor;
        _tenantConfigurationStore = tenantConfigurationStore;
        _mcpAuthorizationService = mcpAuthorizationService;
    }

    /// <summary>
    /// Gets the MCP authorization service.
    /// </summary>
    protected IMcpAuthorizationService? McpAuthorizationService => _mcpAuthorizationService;

    /// <summary>
    /// Ensures the user has MCP access before executing the tool.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ForbiddenException">
    /// Thrown when the user does not have MCP access.
    /// </exception>
    protected async Task EnsureMcpAccessAsync(CancellationToken cancellationToken)
    {
        if (_mcpAuthorizationService != null)
        {
            await _mcpAuthorizationService.EnsureMcpAccessAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Ensures the user has permission for the specified MCP operation.
    /// </summary>
    /// <param name="operationType">The type of MCP operation being performed.</param>
    /// <param name="resourceType">The FHIR resource type (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ForbiddenException">
    /// Thrown when the user does not have permission for the operation.
    /// </exception>
    protected async Task EnsureOperationAuthorizedAsync(
        McpOperationType operationType,
        string? resourceType = null,
        CancellationToken cancellationToken = default)
    {
        if (_mcpAuthorizationService != null)
        {
            await _mcpAuthorizationService.EnsureOperationAuthorizedAsync(operationType, resourceType, cancellationToken);
        }
    }

    /// <summary>
    /// Resolves the tenant ID using three-tier resolution strategy.
    /// </summary>
    /// <param name="explicitTenantId">Explicit tenant ID parameter (highest priority).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Resolved tenant ID.</returns>
    /// <exception cref="BadRequestException">
    /// Thrown when tenant cannot be resolved or multi-tenant mode requires explicit tenant.
    /// </exception>
    protected async Task<int> ResolveTenantIdAsync(
        int? explicitTenantId,
        CancellationToken cancellationToken)
    {
        // Strategy 1: Explicit parameter (highest priority)
        if (explicitTenantId.HasValue)
        {
            return explicitTenantId.Value;
        }

        // Strategy 2: IFhirRequestContext.TenantId from middleware
        var requestContext = _fhirRequestContextAccessor.RequestContext;
        if (requestContext?.TenantId > 0)
        {
            return requestContext.TenantId;
        }

        // Strategy 3: Default to single tenant if only one active tenant exists
        var allTenants = await _tenantConfigurationStore.GetAllTenantsAsync(cancellationToken);
        var activeTenants = allTenants.Where(t => t.IsActive && !t.IsSystemPartition).ToList();

        if (activeTenants.Count == 0)
        {
            throw new BadRequestException("No active tenants available");
        }

        if (activeTenants.Count == 1)
        {
            return activeTenants[0].TenantId;
        }

        // Multi-tenant mode requires explicit tenant
        throw new BadRequestException(
            "Multi-tenant mode detected. Please specify tenantId parameter or use /tenant/{id}/mcp route");
    }

    /// <summary>
    /// Validates that the tenant exists and is active.
    /// </summary>
    /// <param name="tenantId">Tenant ID to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validated tenant configuration.</returns>
    /// <exception cref="BadRequestException">
    /// Thrown when tenant does not exist or is inactive.
    /// </exception>
    protected async Task<TenantConfiguration> ValidateTenantAccessAsync(
        int tenantId,
        CancellationToken cancellationToken)
    {
        var tenantConfig = await _tenantConfigurationStore.GetTenantConfigurationAsync(
            tenantId,
            cancellationToken);

        if (tenantConfig == null)
        {
            throw new BadRequestException($"Tenant {tenantId} does not exist");
        }

        if (!tenantConfig.IsActive)
        {
            throw new BadRequestException($"Tenant {tenantId} is not active");
        }

        if (tenantConfig.IsSystemPartition)
        {
            throw new BadRequestException($"Tenant {tenantId} is a system partition and cannot be accessed via API");
        }

        return tenantConfig;
    }
}
