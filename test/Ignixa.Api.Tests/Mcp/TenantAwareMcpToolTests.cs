// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Application.Features.Mcp.Tools;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Ignixa.Api.Tests.Mcp;

/// <summary>
/// Unit tests for TenantAwareMcpTool base class.
/// Tests the three-tier tenant resolution strategy and validation logic.
/// </summary>
public class TenantAwareMcpToolTests : McpTestBase
{
    #region Tenant Resolution Tests

    [Fact]
    public async Task GivenExplicitTenantId_WhenResolvingTenant_ThenReturnsExplicitTenantId()
    {
        // Arrange
        var tool = new TestTenantAwareTool(HttpContextAccessor, TenantConfigurationStore);
        var explicitTenantId = 5;

        // Act
        var result = await tool.TestResolveTenantIdAsync(explicitTenantId, CancellationToken.None);

        // Assert
        result.Should().Be(explicitTenantId);
    }

    [Fact]
    public async Task GivenRouteContext_WhenResolvingTenantWithoutExplicit_ThenReturnsRouteContextTenantId()
    {
        // Arrange
        var tool = new TestTenantAwareTool(HttpContextAccessor, TenantConfigurationStore);
        var routeTenantId = 3;
        SetTenantContext(routeTenantId);

        // Act
        var result = await tool.TestResolveTenantIdAsync(null, CancellationToken.None);

        // Assert
        result.Should().Be(routeTenantId);
    }

    [Fact]
    public async Task GivenSingleActiveTenant_WhenResolvingTenantWithoutExplicitOrRoute_ThenReturnsDefaultTenant()
    {
        // Arrange
        var tool = new TestTenantAwareTool(HttpContextAccessor, TenantConfigurationStore);
        var tenant = SetupTenant(1, "Default Tenant", isActive: true);
        SetupAllTenants(tenant);

        // Act
        var result = await tool.TestResolveTenantIdAsync(null, CancellationToken.None);

        // Assert
        result.Should().Be(1);
    }

    [Fact]
    public async Task GivenMultipleTenants_WhenResolvingTenantWithoutExplicitOrRoute_ThenThrowsBadRequestException()
    {
        // Arrange
        var tool = new TestTenantAwareTool(HttpContextAccessor, TenantConfigurationStore);
        var tenant1 = SetupTenant(1, "Tenant 1", isActive: true);
        var tenant2 = SetupTenant(2, "Tenant 2", isActive: true);
        SetupAllTenants(tenant1, tenant2);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(
            async () => await tool.TestResolveTenantIdAsync(null, CancellationToken.None));

        ex.Message.Should().Contain("Multi-tenant mode detected");
        ex.Message.Should().Contain("Please specify tenantId parameter");
    }

    [Fact]
    public async Task GivenNoActiveTenants_WhenResolvingTenant_ThenThrowsBadRequestException()
    {
        // Arrange
        var tool = new TestTenantAwareTool(HttpContextAccessor, TenantConfigurationStore);
        SetupAllTenants(); // No tenants

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(
            async () => await tool.TestResolveTenantIdAsync(null, CancellationToken.None));

        ex.Message.Should().Contain("No active tenants available");
    }

    [Fact]
    public async Task GivenSystemPartitionOnly_WhenResolvingTenant_ThenThrowsBadRequestException()
    {
        // Arrange
        var tool = new TestTenantAwareTool(HttpContextAccessor, TenantConfigurationStore);
        var systemTenant = SetupTenant(0, "System Partition", isActive: true, isSystemPartition: true);
        SetupAllTenants(systemTenant);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(
            async () => await tool.TestResolveTenantIdAsync(null, CancellationToken.None));

        ex.Message.Should().Contain("No active tenants available");
    }

    [Fact]
    public async Task GivenExplicitTenantIdTakesPrecedence_WhenRouteContextAlsoSet_ThenReturnsExplicitTenantId()
    {
        // Arrange
        var tool = new TestTenantAwareTool(HttpContextAccessor, TenantConfigurationStore);
        var explicitTenantId = 5;
        var routeTenantId = 3;
        SetTenantContext(routeTenantId);

        // Act
        var result = await tool.TestResolveTenantIdAsync(explicitTenantId, CancellationToken.None);

        // Assert - explicit parameter takes precedence over route context
        result.Should().Be(explicitTenantId);
    }

    #endregion

    #region Tenant Validation Tests

    [Fact]
    public async Task GivenActiveTenant_WhenValidatingAccess_ThenReturnsTenantConfiguration()
    {
        // Arrange
        var tool = new TestTenantAwareTool(HttpContextAccessor, TenantConfigurationStore);
        var tenantConfig = SetupTenant(1, "Active Tenant", isActive: true);

        // Act
        var result = await tool.TestValidateTenantAccessAsync(1, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.TenantId.Should().Be(1);
        result.DisplayName.Should().Be("Active Tenant");
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GivenNonExistentTenant_WhenValidatingAccess_ThenThrowsBadRequestException()
    {
        // Arrange
        var tool = new TestTenantAwareTool(HttpContextAccessor, TenantConfigurationStore);
        TenantConfigurationStore
            .GetTenantConfigurationAsync(999, Arg.Any<CancellationToken>())
            .Returns((TenantConfiguration?)null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(
            async () => await tool.TestValidateTenantAccessAsync(999, CancellationToken.None));

        ex.Message.Should().Contain("Tenant 999 does not exist");
    }

    [Fact]
    public async Task GivenInactiveTenant_WhenValidatingAccess_ThenThrowsBadRequestException()
    {
        // Arrange
        var tool = new TestTenantAwareTool(HttpContextAccessor, TenantConfigurationStore);
        SetupTenant(2, "Inactive Tenant", isActive: false);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(
            async () => await tool.TestValidateTenantAccessAsync(2, CancellationToken.None));

        ex.Message.Should().Contain("Tenant 2 is not active");
    }

    [Fact]
    public async Task GivenSystemPartition_WhenValidatingAccess_ThenThrowsBadRequestException()
    {
        // Arrange
        var tool = new TestTenantAwareTool(HttpContextAccessor, TenantConfigurationStore);
        SetupTenant(0, "System Partition", isActive: true, isSystemPartition: true);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<BadRequestException>(
            async () => await tool.TestValidateTenantAccessAsync(0, CancellationToken.None));

        ex.Message.Should().Contain("Tenant 0 is a system partition and cannot be accessed via API");
    }

    #endregion

    #region Test Helper Class

    /// <summary>
    /// Test implementation of TenantAwareMcpTool that exposes protected methods for testing.
    /// </summary>
    private class TestTenantAwareTool : TenantAwareMcpTool
    {
        public TestTenantAwareTool(
            IHttpContextAccessor httpContextAccessor,
            ITenantConfigurationStore tenantConfigurationStore)
            : base(httpContextAccessor, tenantConfigurationStore)
        {
        }

        public Task<int> TestResolveTenantIdAsync(int? explicitTenantId, CancellationToken cancellationToken)
        {
            return ResolveTenantIdAsync(explicitTenantId, cancellationToken);
        }

        public Task<TenantConfiguration> TestValidateTenantAccessAsync(int tenantId, CancellationToken cancellationToken)
        {
            return ValidateTenantAccessAsync(tenantId, cancellationToken);
        }
    }

    #endregion
}
