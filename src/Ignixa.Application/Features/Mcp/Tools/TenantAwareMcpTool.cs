// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Microsoft.AspNetCore.Http;

namespace Ignixa.Application.Features.Mcp.Tools;

/// <summary>
/// Base class for MCP tools that require tenant context resolution.
/// Provides three-tier tenant resolution strategy:
/// 1. Explicit tenantId parameter (highest priority)
/// 2. HttpContext.Items["TenantContext"] from route /tenant/{id}/mcp
/// 3. Default to single tenant if only one active tenant exists
/// </summary>
public abstract class TenantAwareMcpTool
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantConfigurationStore _tenantConfigurationStore;

    /// <summary>
    /// Gets the HTTP context accessor.
    /// Protected to allow derived tools to access HttpContext for setting tenant context.
    /// </summary>
    protected IHttpContextAccessor HttpContextAccessor => _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantAwareMcpTool"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">HTTP context accessor for route parameters.</param>
    /// <param name="tenantConfigurationStore">Tenant configuration store.</param>
    protected TenantAwareMcpTool(
        IHttpContextAccessor httpContextAccessor,
        ITenantConfigurationStore tenantConfigurationStore)
    {
        _httpContextAccessor = httpContextAccessor;
        _tenantConfigurationStore = tenantConfigurationStore;
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

        // Strategy 2: Route context from /tenant/{id}/mcp
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.Items.TryGetValue("TenantContext", out var tenantContextObj) == true
            && tenantContextObj is int tenantId)
        {
            return tenantId;
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
