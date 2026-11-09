// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Application.Features.Authorization.Smart;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Ignixa.Application.Tests.Features.Authorization;

public class AuthenticationHandlerTests
{
    private readonly AuthenticationHandler _handler;

    public AuthenticationHandlerTests()
    {
        _handler = new AuthenticationHandler(NullLogger<AuthenticationHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_MetadataEndpoint_AllowsWithoutAuthentication()
    {
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var context = new FhirAuthorizationContext
        {
            RequestContext = CreateRequestContext(),
            Interaction = FhirInteraction.Capabilities,
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task HandleAsync_UnauthenticatedRequest_ReturnsDenied()
    {
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var context = new FhirAuthorizationContext
        {
            RequestContext = CreateRequestContext(),
            Interaction = FhirInteraction.Read,
            ResourceType = "Patient",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.ShouldBeFalse();
        result.DenialReason.ShouldBe("Authentication required");
    }

    // The RBAC and SMART handlers skip a caller with nothing for them to check, so this handler must refuse one
    // that holds neither a resource scope nor a role.
    [Fact]
    public async Task GivenAnAuthenticatedCallerWithNoScopeOrRole_WhenHandled_ThenItIsDenied()
    {
        var result = await _handler.HandleAsync(CreateContext(roles: null, scopes: null), CancellationToken.None);

        result.Allowed.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAnAuthenticatedCallerWithARole_WhenHandled_ThenItPasses()
    {
        var result = await _handler.HandleAsync(CreateContext(roles: ["Clinician"], scopes: null), CancellationToken.None);

        result.Allowed.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAnAuthenticatedCallerWithAResourceScope_WhenHandled_ThenItPasses()
    {
        var scopes = SmartScopeParser.ParseScopes("system/Patient.rs");

        var result = await _handler.HandleAsync(CreateContext(roles: null, scopes), CancellationToken.None);

        result.Allowed.ShouldBeTrue();
    }

    private static FhirAuthorizationContext CreateContext(List<string>? roles, IReadOnlyList<SmartScope>? scopes) =>
        new()
        {
            RequestContext = CreateRequestContext(),
            UserId = "user123",
            Roles = roles,
            SmartContext = scopes is null
                ? null
                : new SmartAuthorizationContext
                {
                    TokenClaims = new SmartTokenClaims { ScopeString = "system/Patient.rs", Scopes = scopes },
                    Scopes = scopes
                },
            Interaction = FhirInteraction.Read,
            ResourceType = "Patient",
            HttpContext = Substitute.For<HttpContext>(),
            Timestamp = DateTimeOffset.UtcNow
        };

    [Fact]
    public void Priority_Is10()
    {
        // Assert
        _handler.Priority.ShouldBe(10);
    }

    private static IFhirRequestContext CreateRequestContext(int tenantId = 1)
    {
        var context = Substitute.For<IFhirRequestContext>();
        context.TenantId.Returns(tenantId);
        return context;
    }
}
