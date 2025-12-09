// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Ignixa.Application.Tests.Features.Authorization;

public class TenantIsolationHandlerTests
{
    private readonly TenantIsolationHandler _handler;

    public TenantIsolationHandlerTests()
    {
        _handler = new TenantIsolationHandler(NullLogger<TenantIsolationHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_SystemAdmin_BypassesTenantCheck()
    {
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var context = new FhirAuthorizationContext
        {
            UserId = "admin",
            Roles = new List<string> { "SystemAdmin" },
            TenantId = "1",
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
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var context = new FhirAuthorizationContext
        {
            UserId = "user123",
            Roles = new List<string> { "Clinician" },
            Interaction = FhirInteraction.Read,
            ResourceType = "Patient",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.Should().BeFalse();
        result.DenialReason.Should().Be("No tenant context");
    }

    [Fact]
    public async Task HandleAsync_ValidTenant_ReturnsSuccess()
    {
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var routeValues = new RouteValueDictionary { { "tenantId", "1" } };
        httpContext.Request.RouteValues.Returns(routeValues);

        var context = new FhirAuthorizationContext
        {
            UserId = "user123",
            TenantId = "1",
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
