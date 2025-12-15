// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Ignixa.Application.Tests.Features.Authorization;

public class TenantIsolationHandlerTests
{
    private readonly IFhirRequestContextAccessor _fhirContextAccessor;
    private readonly TenantIsolationHandler _handler;

    public TenantIsolationHandlerTests()
    {
        _fhirContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        _handler = new TenantIsolationHandler(_fhirContextAccessor, NullLogger<TenantIsolationHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_SystemAdmin_BypassesTenantCheck()
    {
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var requestContext = Substitute.For<IFhirRequestContext>();
        requestContext.TenantId.Returns(1);

        var context = new FhirAuthorizationContext
        {
            UserId = "admin",
            Roles = new List<string> { "SystemAdmin" },
            RequestContext = requestContext,
            Interaction = FhirInteraction.Read,
            ResourceType = "Patient",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NoTenantContext_ReturnsDenied()
    {
        // Arrange - User from tenant 1 trying to access tenant 2 (cross-tenant access)
        var httpContext = Substitute.For<HttpContext>();
        httpContext.Request.RouteValues.Returns(new Microsoft.AspNetCore.Routing.RouteValueDictionary { { "tenantId", "2" } });

        var requestContext = Substitute.For<IFhirRequestContext>();
        requestContext.TenantId.Returns(1); // User belongs to tenant 1

        var context = new FhirAuthorizationContext
        {
            UserId = "user123",
            Roles = new List<string> { "Clinician" },
            RequestContext = requestContext,
            Interaction = FhirInteraction.Read,
            ResourceType = "Patient",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.Should().BeFalse();
        result.DenialReason.Should().Contain("Access denied to tenant");
    }

    [Fact]
    public async Task HandleAsync_ValidTenant_ReturnsSuccess()
    {
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var requestContext = Substitute.For<IFhirRequestContext>();
        requestContext.TenantId.Returns(1);

        // Mock FhirRequestContextAccessor to return the request context
        _fhirContextAccessor.RequestContext.Returns(requestContext);

        var context = new FhirAuthorizationContext
        {
            UserId = "user123",
            RequestContext = requestContext,
            Roles = new List<string> { "Clinician" },
            Interaction = FhirInteraction.Read,
            ResourceType = "Patient",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public void Priority_Is20()
    {
        // Assert
        _handler.Priority.Should().Be(20);
    }
}
