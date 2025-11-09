// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Ignixa.Api.Tests.Mcp;

/// <summary>
/// Base class for MCP tool tests providing common mocking infrastructure.
/// Sets up HttpContext, TenantConfigurationStore, and helper methods for tenant setup.
/// </summary>
public abstract class McpTestBase
{
    /// <summary>
    /// Mocked HTTP context accessor for accessing current HTTP request context.
    /// </summary>
    protected IHttpContextAccessor HttpContextAccessor { get; }

    /// <summary>
    /// Mocked tenant configuration store for tenant lookup.
    /// </summary>
    protected ITenantConfigurationStore TenantConfigurationStore { get; }

    /// <summary>
    /// Mocked HTTP context instance.
    /// </summary>
    protected HttpContext HttpContext { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpTestBase"/> class.
    /// Sets up common mocking infrastructure for all MCP tool tests.
    /// </summary>
    protected McpTestBase()
    {
        HttpContextAccessor = Substitute.For<IHttpContextAccessor>();
        TenantConfigurationStore = Substitute.For<ITenantConfigurationStore>();
        HttpContext = new DefaultHttpContext();

        HttpContextAccessor.HttpContext.Returns(HttpContext);
    }

    /// <summary>
    /// Sets up a tenant in the mocked tenant configuration store.
    /// </summary>
    /// <param name="tenantId">Tenant ID.</param>
    /// <param name="displayName">Tenant display name.</param>
    /// <param name="isActive">Whether the tenant is active.</param>
    /// <param name="isSystemPartition">Whether this is a system partition (default: false).</param>
    /// <returns>The configured tenant configuration.</returns>
    protected TenantConfiguration SetupTenant(
        int tenantId,
        string displayName,
        bool isActive = true,
        bool isSystemPartition = false)
    {
        var tenantConfig = new TenantConfiguration
        {
            TenantId = tenantId,
            DisplayName = displayName,
            FhirVersion = "4.0",
            IsActive = isActive,
            IsSystemPartition = isSystemPartition
        };

        TenantConfigurationStore
            .GetTenantConfigurationAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(tenantConfig);

        return tenantConfig;
    }

    /// <summary>
    /// Simulates middleware setting the tenant context in HttpContext.Items.
    /// This mimics the behavior of TenantResolutionMiddleware when processing /tenant/{id}/mcp routes.
    /// </summary>
    /// <param name="tenantId">Tenant ID to set in context.</param>
    protected void SetTenantContext(int tenantId)
    {
        HttpContext.Items["TenantContext"] = tenantId;
    }

    /// <summary>
    /// Sets up multiple tenants in the GetAllTenantsAsync mock.
    /// Used for testing single-tenant vs multi-tenant resolution logic.
    /// </summary>
    /// <param name="tenants">Collection of tenant configurations.</param>
    protected void SetupAllTenants(params TenantConfiguration[] tenants)
    {
        TenantConfigurationStore
            .GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(tenants.ToList().AsReadOnly());
    }
}
