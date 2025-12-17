// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Features.Authorization.Smart;
using Ignixa.Application.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Ignixa.Application.Tests.Features.Authorization;

public class RbacAuthorizationHandlerTests
{
    private readonly IRolePermissionStore _permissionStore;
    private readonly RbacAuthorizationHandler _handler;

    public RbacAuthorizationHandlerTests()
    {
        _permissionStore = Substitute.For<IRolePermissionStore>();
        _handler = new RbacAuthorizationHandler(
            _permissionStore,
            NullLogger<RbacAuthorizationHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_SmartScopesPresent_SkipsRbacCheck()
    {
        // Arrange - SMART v2 format
        var httpContext = Substitute.For<HttpContext>();
        var smartContext = new SmartAuthorizationContext
        {
            TokenClaims = new SmartTokenClaims
            {
                ScopeString = "patient/Patient.rs",
                Scopes = new List<SmartScope>
                {
                    new() { Type = SmartScopeType.Patient, ResourceType = "Patient", Permissions = SmartPermissions.Read | SmartPermissions.Search, PermissionString = "RS", OriginalScope = "patient/Patient.rs" }
                }
            },
            Scopes = new List<SmartScope>
            {
                new() { Type = SmartScopeType.Patient, ResourceType = "Patient", Permissions = SmartPermissions.Read | SmartPermissions.Search, PermissionString = "RS", OriginalScope = "patient/Patient.rs" }
            }
        };

        var context = new FhirAuthorizationContext
        {
            RequestContext = CreateRequestContext(),
            UserId = "user123",
            Roles = new List<string> { "Clinician" },
            SmartContext = smartContext,
            Interaction = FhirInteraction.Read,
            ResourceType = "Patient",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.ShouldBeTrue();
        await _permissionStore.DidNotReceive().GetPermissionsAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NoRoles_PassesToNextHandler()
    {
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var context = new FhirAuthorizationContext
        {
            RequestContext = CreateRequestContext(),
            UserId = "user123",
            Interaction = FhirInteraction.Read,
            ResourceType = "Patient",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task HandleAsync_MatchingPermission_ReturnsSuccess()
    {
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var context = new FhirAuthorizationContext
        {
            RequestContext = CreateRequestContext(1),
            UserId = "user123",
            Roles = new List<string> { "Clinician" },
            Interaction = FhirInteraction.Read,
            ResourceType = "Patient",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        _permissionStore.GetPermissionsAsync("1", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ResourceGrant> { new("Patient", "*") });

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task HandleAsync_NoMatchingPermission_ReturnsDenied()
    {
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var context = new FhirAuthorizationContext
        {
            RequestContext = CreateRequestContext(1),
            UserId = "user123",
            Roles = new List<string> { "ReadOnly" },
            Interaction = FhirInteraction.Delete,
            ResourceType = "Patient",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        _permissionStore.GetPermissionsAsync("1", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ResourceGrant> { new("*", "read") });

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.ShouldBeFalse();
        result.DenialReason.ShouldContain("No permission grants");
    }

    [Fact]
    public void Priority_Is30()
    {
        // Assert
        _handler.Priority.ShouldBe(30);
    }

    private static IFhirRequestContext CreateRequestContext(int tenantId = 1)
    {
        var context = Substitute.For<IFhirRequestContext>();
        context.TenantId.Returns(tenantId);
        return context;
    }
}
