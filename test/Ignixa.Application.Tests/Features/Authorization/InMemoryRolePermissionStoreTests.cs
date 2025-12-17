// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;
using Shouldly;
using Ignixa.Application.Features.Authorization;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Models;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Ignixa.Application.Tests.Features.Authorization;

public class InMemoryRolePermissionStoreTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_LoadsDefaultRoles_WhenNoConfiguration()
    {
        // Arrange
        var options = CreateOptions(new AuthorizationOptions());

        // Act
        var store = new InMemoryRolePermissionStore(options);

        // Assert
        store.RoleNames.ShouldContain("Admin");
        store.RoleNames.ShouldContain("SystemAdmin");
        store.RoleNames.ShouldContain("Clinician");
        store.RoleNames.ShouldContain("ReadOnly");
    }

    [Fact]
    public void Constructor_LoadsConfiguredRoles()
    {
        // Arrange
        var authzOptions = new AuthorizationOptions();
        authzOptions.DefaultRoles["CustomRole"] = new RolePermissions
        {
            Permissions = { new PermissionEntry { ResourceType = "Patient", Interaction = "read" } }
        };
        var options = CreateOptions(authzOptions);

        // Act
        var store = new InMemoryRolePermissionStore(options);

        // Assert
        store.RoleNames.ShouldContain("CustomRole");
    }

    [Fact]
    public async Task Constructor_ConfiguredRolesOverrideDefaults()
    {
        // Arrange - configure Admin with limited permissions
        var authzOptions = new AuthorizationOptions();
        authzOptions.DefaultRoles["Admin"] = new RolePermissions
        {
            Permissions = { new PermissionEntry { ResourceType = "Patient", Interaction = "read" } }
        };
        var options = CreateOptions(authzOptions);

        // Act
        var store = new InMemoryRolePermissionStore(options);

        // Assert - should have limited permissions, not default wildcard
        var permissions = await store.GetPermissionsAsync("tenant1", ["Admin"], CancellationToken.None);
        permissions.Count.ShouldBe(1);
        permissions[0].ResourceType.ShouldBe("Patient");
        permissions[0].Interaction.ShouldBe("read");
    }

    #endregion

    #region GetPermissionsAsync Tests

    [Fact]
    public async Task GetPermissionsAsync_SingleRole_ReturnsPermissions()
    {
        // Arrange
        var store = CreateStoreWithDefaults();

        // Act
        var permissions = await store.GetPermissionsAsync("tenant1", ["Admin"], CancellationToken.None);

        // Assert
        permissions.ShouldNotBeEmpty();
        permissions.ShouldContain(p => p.ResourceType == "*" && p.Interaction == "*");
    }

    [Fact]
    public async Task GetPermissionsAsync_MultipleRoles_AggregatesPermissions()
    {
        // Arrange
        var authzOptions = new AuthorizationOptions();
        authzOptions.DefaultRoles["RoleA"] = new RolePermissions
        {
            Permissions = { new PermissionEntry { ResourceType = "Patient", Interaction = "read" } }
        };
        authzOptions.DefaultRoles["RoleB"] = new RolePermissions
        {
            Permissions = { new PermissionEntry { ResourceType = "Observation", Interaction = "read" } }
        };
        var store = new InMemoryRolePermissionStore(CreateOptions(authzOptions));

        // Act
        var permissions = await store.GetPermissionsAsync("tenant1", ["RoleA", "RoleB"], CancellationToken.None);

        // Assert
        permissions.Count.ShouldBe(2);
        permissions.ShouldContain(p => p.ResourceType == "Patient");
        permissions.ShouldContain(p => p.ResourceType == "Observation");
    }

    [Fact]
    public async Task GetPermissionsAsync_UnknownRole_ReturnsEmpty()
    {
        // Arrange
        var store = CreateStoreWithDefaults();

        // Act
        var permissions = await store.GetPermissionsAsync("tenant1", ["UnknownRole"], CancellationToken.None);

        // Assert
        permissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPermissionsAsync_EmptyRoles_ReturnsEmpty()
    {
        // Arrange
        var store = CreateStoreWithDefaults();

        // Act
        var permissions = await store.GetPermissionsAsync("tenant1", [], CancellationToken.None);

        // Assert
        permissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPermissionsAsync_TenantIdIsIgnored()
    {
        // Arrange - same permissions regardless of tenant
        var store = CreateStoreWithDefaults();

        // Act
        var permissions1 = await store.GetPermissionsAsync("tenant1", ["Admin"], CancellationToken.None);
        var permissions2 = await store.GetPermissionsAsync("tenant2", ["Admin"], CancellationToken.None);

        // Assert - should be the same
        permissions1.ShouldBe(permissions2);
    }

    [Fact]
    public async Task GetPermissionsAsync_DuplicatePermissions_ReturnsDistinct()
    {
        // Arrange - two roles with same permission
        var authzOptions = new AuthorizationOptions();
        authzOptions.DefaultRoles["RoleA"] = new RolePermissions
        {
            Permissions = { new PermissionEntry { ResourceType = "Patient", Interaction = "read" } }
        };
        authzOptions.DefaultRoles["RoleB"] = new RolePermissions
        {
            Permissions = { new PermissionEntry { ResourceType = "Patient", Interaction = "read" } }
        };
        var store = new InMemoryRolePermissionStore(CreateOptions(authzOptions));

        // Act
        var permissions = await store.GetPermissionsAsync("tenant1", ["RoleA", "RoleB"], CancellationToken.None);

        // Assert - should be deduplicated
        permissions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetPermissionsAsync_CaseInsensitiveRoleNames()
    {
        // Arrange
        var store = CreateStoreWithDefaults();

        // Act
        var permissions = await store.GetPermissionsAsync("tenant1", ["ADMIN"], CancellationToken.None);

        // Assert
        permissions.ShouldNotBeEmpty();
    }

    #endregion

    #region SetRolePermissions Tests

    [Fact]
    public async Task SetRolePermissions_UpdatesExistingRole()
    {
        // Arrange
        var store = CreateStoreWithDefaults();
        var newPermissions = new[] { new ResourceGrant("Patient", "read") };

        // Act
        store.SetRolePermissions("Admin", newPermissions);
        var permissions = await store.GetPermissionsAsync("tenant1", ["Admin"], CancellationToken.None);

        // Assert
        permissions.Count.ShouldBe(1);
        permissions[0].ResourceType.ShouldBe("Patient");
    }

    [Fact]
    public async Task SetRolePermissions_AddsNewRole()
    {
        // Arrange
        var store = CreateStoreWithDefaults();
        var newPermissions = new[] { new ResourceGrant("*", "*") };

        // Act
        store.SetRolePermissions("NewRole", newPermissions);
        var permissions = await store.GetPermissionsAsync("tenant1", ["NewRole"], CancellationToken.None);

        // Assert
        permissions.ShouldNotBeEmpty();
        store.RoleNames.ShouldContain("NewRole");
    }

    #endregion

    #region RoleNames Tests

    [Fact]
    public void RoleNames_ReturnsAllConfiguredRoles()
    {
        // Arrange
        var authzOptions = new AuthorizationOptions();
        authzOptions.DefaultRoles["CustomRole1"] = new RolePermissions();
        authzOptions.DefaultRoles["CustomRole2"] = new RolePermissions();
        var store = new InMemoryRolePermissionStore(CreateOptions(authzOptions));

        // Act
        var roleNames = store.RoleNames.ToList();

        // Assert
        roleNames.ShouldContain("CustomRole1");
        roleNames.ShouldContain("CustomRole2");
        roleNames.ShouldContain("Admin"); // Default
        roleNames.ShouldContain("SystemAdmin"); // Default
    }

    #endregion

    #region Helper Methods

    private static IOptions<AuthorizationOptions> CreateOptions(AuthorizationOptions authzOptions)
    {
        var options = Substitute.For<IOptions<AuthorizationOptions>>();
        options.Value.Returns(authzOptions);
        return options;
    }

    private static InMemoryRolePermissionStore CreateStoreWithDefaults()
    {
        return new InMemoryRolePermissionStore(CreateOptions(new AuthorizationOptions()));
    }

    #endregion
}
