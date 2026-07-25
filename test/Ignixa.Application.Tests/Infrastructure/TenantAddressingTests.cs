// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Models;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

/// <summary>
/// <see cref="TenantAddressing.For"/> is the single place that derives <see cref="TenantAddressing"/> from a
/// resolved tenant, so the two call sites (request middleware, background base URI provider) cannot each
/// independently get "sole tenant" or the tenant-0 guard wrong.
/// </summary>
public class TenantAddressingTests
{
    private static TenantConfiguration Tenant(int tenantId, IReadOnlyList<string>? hostnames = null) =>
        new()
        {
            TenantId = tenantId,
            DisplayName = "Acme",
            FhirVersion = "4.0",
            Hostnames = hostnames ?? Array.Empty<string>(),
        };

    [Fact]
    public void GivenTheSoleActiveTenant_WhenBuildingAddressing_ThenIncludeDeploymentRootIsTrue()
    {
        // Arrange
        var tenant = Tenant(1, ["fhir1.example.org"]);

        // Act
        var addressing = TenantAddressing.For(tenant, activeTenantCount: 1);

        // Assert
        addressing.TenantId.ShouldBe(1);
        addressing.Hostnames.ShouldBe(["fhir1.example.org"]);
        addressing.IncludeDeploymentRoot.ShouldBeTrue();
    }

    [Fact]
    public void GivenOneOfSeveralActiveTenants_WhenBuildingAddressing_ThenIncludeDeploymentRootIsFalse()
    {
        // Arrange
        var tenant = Tenant(1);

        // Act
        var addressing = TenantAddressing.For(tenant, activeTenantCount: 2);

        // Assert
        addressing.IncludeDeploymentRoot.ShouldBeFalse();
    }

    [Fact]
    public void GivenATenantWithNullHostnames_WhenBuildingAddressing_ThenHostnamesIsEmptyNotNull()
    {
        // Arrange
        var tenant = Tenant(1, hostnames: null);

        // Act
        var addressing = TenantAddressing.For(tenant, activeTenantCount: 1);

        // Assert
        addressing.Hostnames.ShouldNotBeNull();
        addressing.Hostnames.ShouldBeEmpty();
    }

    [Fact]
    public void GivenTheReservedSystemPartition_WhenBuildingAddressing_ThenItThrows()
    {
        // Arrange
        var tenant = Tenant(0);

        // Act
        var building = () => TenantAddressing.For(tenant, activeTenantCount: 1);

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(building);
    }

    [Fact]
    public void GivenANegativeTenantId_WhenBuildingAddressing_ThenItThrows()
    {
        // Arrange
        var tenant = Tenant(-1);

        // Act
        var building = () => TenantAddressing.For(tenant, activeTenantCount: 1);

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(building);
    }

    [Fact]
    public void GivenANullTenant_WhenBuildingAddressing_ThenItThrows()
    {
        // Act
        var building = () => TenantAddressing.For(null!, activeTenantCount: 1);

        // Assert
        Should.Throw<ArgumentNullException>(building);
    }
}
