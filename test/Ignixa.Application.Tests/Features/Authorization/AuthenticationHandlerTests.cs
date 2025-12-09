// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Models;
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
            Interaction = FhirInteraction.Capabilities,
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_UnauthenticatedRequest_ReturnsDenied()
    {
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var context = new FhirAuthorizationContext
        {
            Interaction = FhirInteraction.Read,
            ResourceType = "Patient",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.Should().BeFalse();
        result.DenialReason.Should().Be("Authentication required");
    }

    [Fact]
    public async Task HandleAsync_AuthenticatedByUserId_ReturnsSuccess()
    {
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var context = new FhirAuthorizationContext
        {
            UserId = "user123",
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
    public void Priority_Is10()
    {
        // Assert
        _handler.Priority.Should().Be(10);
    }
}
