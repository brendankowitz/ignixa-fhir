// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Claims;
using Shouldly;
using Ignixa.Application.Features.Authorization;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Features.Experimental.Mcp.Authorization;
using Ignixa.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Ignixa.Application.Tests.Features.Authorization;

public class McpAuthorizationServiceTests
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IRolePermissionStore _rolePermissionStore;
    private readonly AuthorizationOptions _authzOptions;
    private readonly McpAuthorizationService _service;

    public McpAuthorizationServiceTests()
    {
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _rolePermissionStore = Substitute.For<IRolePermissionStore>();
        _authzOptions = new AuthorizationOptions();
        // McpEnabledRoles has default values: Admin, SystemAdmin, Mcp, Contributor

        var options = Substitute.For<IOptions<AuthorizationOptions>>();
        options.Value.Returns(_authzOptions);

        _service = new McpAuthorizationService(
            _httpContextAccessor,
            _rolePermissionStore,
            options,
            NullLogger<McpAuthorizationService>.Instance);
    }

    #region AuthorizeMcpAccessAsync Tests

    [Fact]
    public async Task AuthorizeMcpAccessAsync_NoRoles_ReturnsFalse()
    {
        // Arrange
        SetupUserWithRoles([]);

        // Act
        var result = await _service.AuthorizeMcpAccessAsync();

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task AuthorizeMcpAccessAsync_AdminRole_ReturnsTrue()
    {
        // Arrange
        SetupUserWithRoles(["Admin"]);

        // Act
        var result = await _service.AuthorizeMcpAccessAsync();

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task AuthorizeMcpAccessAsync_SystemAdminRole_ReturnsTrue()
    {
        // Arrange
        SetupUserWithRoles(["SystemAdmin"]);

        // Act
        var result = await _service.AuthorizeMcpAccessAsync();

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task AuthorizeMcpAccessAsync_McpRole_ReturnsTrue()
    {
        // Arrange
        SetupUserWithRoles(["Mcp"]);

        // Act
        var result = await _service.AuthorizeMcpAccessAsync();

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task AuthorizeMcpAccessAsync_ContributorRole_ReturnsTrue()
    {
        // Arrange
        SetupUserWithRoles(["Contributor"]);

        // Act
        var result = await _service.AuthorizeMcpAccessAsync();

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task AuthorizeMcpAccessAsync_ClinicianRole_ReturnsFalse()
    {
        // Arrange
        SetupUserWithRoles(["Clinician"]);

        // Act
        var result = await _service.AuthorizeMcpAccessAsync();

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task AuthorizeMcpAccessAsync_CustomRoleWithMcpAccessEnabled_ReturnsTrue()
    {
        // Arrange
        _authzOptions.DefaultRoles["CustomRole"] = new RolePermissions { McpAccess = true };
        SetupUserWithRoles(["CustomRole"]);

        // Act
        var result = await _service.AuthorizeMcpAccessAsync();

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task AuthorizeMcpAccessAsync_CaseInsensitive_ReturnsTrue()
    {
        // Arrange
        SetupUserWithRoles(["ADMIN"]); // uppercase

        // Act
        var result = await _service.AuthorizeMcpAccessAsync();

        // Assert
        result.ShouldBeTrue();
    }

    #endregion

    #region AuthorizeOperationAsync Tests

    [Fact]
    public async Task AuthorizeOperationAsync_ReadOperation_WithPermission_ReturnsTrue()
    {
        // Arrange
        SetupUserWithRoles(["Admin"]);
        _rolePermissionStore.GetPermissionsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([new ResourceGrant("*", "read")]);

        // Act
        var result = await _service.AuthorizeOperationAsync(McpOperationType.Read, "Patient");

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task AuthorizeOperationAsync_CreateOperation_WithoutPermission_ReturnsFalse()
    {
        // Arrange
        SetupUserWithRoles(["Admin"]);
        _rolePermissionStore.GetPermissionsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([new ResourceGrant("Patient", "read")]);

        // Act
        var result = await _service.AuthorizeOperationAsync(McpOperationType.Create, "Patient");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task AuthorizeOperationAsync_NoMcpAccess_ReturnsFalse()
    {
        // Arrange
        SetupUserWithRoles(["Clinician"]);

        // Act
        var result = await _service.AuthorizeOperationAsync(McpOperationType.Read);

        // Assert
        result.ShouldBeFalse();
        await _rolePermissionStore.DidNotReceive().GetPermissionsAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region EnsureMcpAccessAsync Tests

    [Fact]
    public async Task EnsureMcpAccessAsync_WithAccess_DoesNotThrow()
    {
        // Arrange
        SetupUserWithRoles(["Admin"]);

        // Act
        var act = () => _service.EnsureMcpAccessAsync();

        // Assert
        await Should.NotThrowAsync(async () => await act());
    }

    [Fact]
    public async Task EnsureMcpAccessAsync_WithoutAccess_ThrowsForbiddenException()
    {
        // Arrange
        SetupUserWithRoles(["Clinician"]);

        // Act
        var act = () => _service.EnsureMcpAccessAsync();

        // Assert
        var ex = await Should.ThrowAsync<ForbiddenException>(act);
        ex.Message.ShouldContain("MCP access denied");
    }

    #endregion

    #region EnsureOperationAuthorizedAsync Tests

    [Fact]
    public async Task EnsureOperationAuthorizedAsync_Authorized_DoesNotThrow()
    {
        // Arrange
        SetupUserWithRoles(["Admin"]);
        _rolePermissionStore.GetPermissionsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([new ResourceGrant("*", "*")]);

        // Act
        var act = () => _service.EnsureOperationAuthorizedAsync(McpOperationType.Create, "Patient");

        // Assert
        await Should.NotThrowAsync(async () => await act());
    }

    [Fact]
    public async Task EnsureOperationAuthorizedAsync_Unauthorized_ThrowsForbiddenException()
    {
        // Arrange
        SetupUserWithRoles(["Admin"]);
        _rolePermissionStore.GetPermissionsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([new ResourceGrant("Patient", "read")]);

        // Act
        var act = () => _service.EnsureOperationAuthorizedAsync(McpOperationType.Delete, "Patient");

        // Assert
        var ex = await Should.ThrowAsync<ForbiddenException>(act);
        ex.Message.ShouldContain("MCP operation denied");
    }

    #endregion

    #region GetCurrentUserRoles Tests

    [Fact]
    public void GetCurrentUserRoles_NoHttpContext_ReturnsEmpty()
    {
        // Arrange
        _httpContextAccessor.HttpContext.Returns((HttpContext)null);

        // Act
        var roles = _service.GetCurrentUserRoles();

        // Assert
        roles.ShouldBeEmpty();
    }

    [Fact]
    public void GetCurrentUserRoles_MultipleClaimTypes_AggregatesRoles()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(FhirClaimTypes.Role, "Admin"),
            new(FhirClaimTypes.Roles, "Clinician"),
            new(FhirClaimTypes.WsFederationRole, "ReadOnly")
        };
        SetupUserWithClaims(claims);

        // Act
        var roles = _service.GetCurrentUserRoles();

        // Assert
        roles.ShouldContain("Admin");
        roles.ShouldContain("Clinician");
        roles.ShouldContain("ReadOnly");
    }

    [Fact]
    public void GetCurrentUserRoles_CommaSeparatedRoles_ParsesCorrectly()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(FhirClaimTypes.Role, "Admin,Clinician,ReadOnly")
        };
        SetupUserWithClaims(claims);

        // Act
        var roles = _service.GetCurrentUserRoles();

        // Assert
        roles.ShouldContain("Admin");
        roles.ShouldContain("Clinician");
        roles.ShouldContain("ReadOnly");
    }

    [Fact]
    public void GetCurrentUserRoles_DuplicateRoles_ReturnsDistinct()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(FhirClaimTypes.Role, "Admin"),
            new(FhirClaimTypes.Roles, "Admin"), // duplicate
            new(FhirClaimTypes.Role, "admin") // case different
        };
        SetupUserWithClaims(claims);

        // Act
        var roles = _service.GetCurrentUserRoles();

        // Assert
        roles.Count.ShouldBe(1); // Case-insensitive deduplication
    }

    #endregion

    #region Helper Methods

    private void SetupUserWithRoles(IEnumerable<string> roles)
    {
        var claims = roles.Select(r => new Claim(FhirClaimTypes.Role, r)).ToList();
        SetupUserWithClaims(claims);
    }

    private void SetupUserWithClaims(List<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = Substitute.For<HttpContext>();
        httpContext.User.Returns(principal);

        _httpContextAccessor.HttpContext.Returns(httpContext);
    }

    #endregion
}
