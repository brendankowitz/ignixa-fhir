// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Features.Authorization.Smart;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Ignixa.Application.Tests.Features.Authorization;

public class SmartScopeAuthorizationHandlerTests
{
    private readonly SmartScopeAuthorizationHandler _handler;

    public SmartScopeAuthorizationHandlerTests()
    {
        _handler = new SmartScopeAuthorizationHandler(NullLogger<SmartScopeAuthorizationHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_NoSmartContext_SkipsCheck()
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
    public async Task HandleAsync_MatchingScope_ReturnsSuccess()
    {
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var scopes = new List<SmartScope>
        {
            new() { Type = SmartScopeType.User, ResourceType = "Observation", Permission = "READ", OriginalScope = "user/Observation.read" }
        };
        var smartContext = new SmartAuthorizationContext
        {
            TokenClaims = new SmartTokenClaims { ScopeString = "user/Observation.read", Scopes = scopes },
            Scopes = scopes
        };

        var context = new FhirAuthorizationContext
        {
            UserId = "user123",
            SmartContext = smartContext,
            Interaction = FhirInteraction.Read,
            ResourceType = "Observation",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NoMatchingScope_ReturnsDenied()
    {
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var scopes = new List<SmartScope>
        {
            new() { Type = SmartScopeType.User, ResourceType = "Patient", Permission = "READ", OriginalScope = "user/Patient.read" }
        };
        var smartContext = new SmartAuthorizationContext
        {
            TokenClaims = new SmartTokenClaims { ScopeString = "user/Patient.read", Scopes = scopes },
            Scopes = scopes
        };

        var context = new FhirAuthorizationContext
        {
            UserId = "user123",
            SmartContext = smartContext,
            Interaction = FhirInteraction.Read,
            ResourceType = "Observation",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_PatientScope_AppliesPatientFilter()
    {
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var scopes = new List<SmartScope>
        {
            new() { Type = SmartScopeType.Patient, ResourceType = "Observation", Permission = "READ", OriginalScope = "patient/Observation.read" }
        };
        var smartContext = new SmartAuthorizationContext
        {
            TokenClaims = new SmartTokenClaims { ScopeString = "patient/Observation.read", Scopes = scopes, PatientId = "patient-123" },
            Scopes = scopes,
            PatientContext = "patient-123"
        };

        var context = new FhirAuthorizationContext
        {
            UserId = "user123",
            SmartContext = smartContext,
            Interaction = FhirInteraction.Read,
            ResourceType = "Observation",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.Should().BeTrue();
        result.Filter.Should().NotBeNull();
        result.Filter!.PatientFilter.Should().Be("patient-123");
    }

    [Fact]
    public async Task HandleAsync_PatientScopeWithoutContext_ReturnsDenied()
    {
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var scopes = new List<SmartScope>
        {
            new() { Type = SmartScopeType.Patient, ResourceType = "Observation", Permission = "READ", OriginalScope = "patient/Observation.read" }
        };
        var smartContext = new SmartAuthorizationContext
        {
            TokenClaims = new SmartTokenClaims { ScopeString = "patient/Observation.read", Scopes = scopes },
            Scopes = scopes
            // No PatientContext - missing patient ID
        };

        var context = new FhirAuthorizationContext
        {
            UserId = "user123",
            SmartContext = smartContext,
            Interaction = FhirInteraction.Read,
            ResourceType = "Observation",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.Should().BeFalse();
        result.DenialReason.Should().Contain("patient context");
    }

    [Fact]
    public void Priority_Is40()
    {
        // Assert
        _handler.Priority.Should().Be(40);
    }
}
