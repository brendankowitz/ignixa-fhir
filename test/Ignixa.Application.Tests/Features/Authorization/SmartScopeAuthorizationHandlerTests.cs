// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Features.Authorization.Smart;
using Ignixa.Application.Infrastructure;
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
        var requestContext = CreateRequestContext();
        var context = new FhirAuthorizationContext
        {
            RequestContext = requestContext,
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
        // Arrange - SMART v2 format
        var httpContext = Substitute.For<HttpContext>();
        var scopes = new List<SmartScope>
        {
            new() { Type = SmartScopeType.User, ResourceType = "Observation", Permissions = SmartPermissions.Read | SmartPermissions.Search, PermissionString = "RS", OriginalScope = "user/Observation.rs" }
        };
        var smartContext = new SmartAuthorizationContext
        {
            TokenClaims = new SmartTokenClaims { ScopeString = "user/Observation.rs", Scopes = scopes },
            Scopes = scopes
        };

        var context = new FhirAuthorizationContext
        {
            RequestContext = CreateRequestContext(),
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
        // Arrange - SMART v2 format
        var httpContext = Substitute.For<HttpContext>();
        var scopes = new List<SmartScope>
        {
            new() { Type = SmartScopeType.User, ResourceType = "Patient", Permissions = SmartPermissions.Read | SmartPermissions.Search, PermissionString = "RS", OriginalScope = "user/Patient.rs" }
        };
        var smartContext = new SmartAuthorizationContext
        {
            TokenClaims = new SmartTokenClaims { ScopeString = "user/Patient.rs", Scopes = scopes },
            Scopes = scopes
        };

        var context = new FhirAuthorizationContext
        {
            RequestContext = CreateRequestContext(),
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
        // Arrange - SMART v2 format
        var httpContext = Substitute.For<HttpContext>();
        var scopes = new List<SmartScope>
        {
            new() { Type = SmartScopeType.Patient, ResourceType = "Observation", Permissions = SmartPermissions.Read | SmartPermissions.Search, PermissionString = "RS", OriginalScope = "patient/Observation.rs" }
        };
        var smartContext = new SmartAuthorizationContext
        {
            TokenClaims = new SmartTokenClaims { ScopeString = "patient/Observation.rs", Scopes = scopes, PatientId = "patient-123" },
            Scopes = scopes,
            PatientContext = "patient-123"
        };

        var context = new FhirAuthorizationContext
        {
            RequestContext = CreateRequestContext(),
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
        // Arrange - SMART v2 format
        var httpContext = Substitute.For<HttpContext>();
        var scopes = new List<SmartScope>
        {
            new() { Type = SmartScopeType.Patient, ResourceType = "Observation", Permissions = SmartPermissions.Read | SmartPermissions.Search, PermissionString = "RS", OriginalScope = "patient/Observation.rs" }
        };
        var smartContext = new SmartAuthorizationContext
        {
            TokenClaims = new SmartTokenClaims { ScopeString = "patient/Observation.rs", Scopes = scopes },
            Scopes = scopes
            // No PatientContext - missing patient ID
        };

        var context = new FhirAuthorizationContext
        {
            RequestContext = CreateRequestContext(),
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
    public async Task HandleAsync_PractitionerScope_AppliesPractitionerFilter()
    {
        // Arrange - SMART v2 Practitioner scope
        var httpContext = Substitute.For<HttpContext>();
        var scopes = new List<SmartScope>
        {
            new() { Type = SmartScopeType.Practitioner, ResourceType = "Schedule", Permissions = SmartPermissions.Read | SmartPermissions.Search, PermissionString = "RS", OriginalScope = "practitioner/Schedule.rs" }
        };
        var smartContext = new SmartAuthorizationContext
        {
            TokenClaims = new SmartTokenClaims { ScopeString = "practitioner/Schedule.rs", Scopes = scopes, FhirUser = "Practitioner/pract-456" },
            Scopes = scopes,
            UserContext = "Practitioner/pract-456"
        };

        var context = new FhirAuthorizationContext
        {
            RequestContext = CreateRequestContext(),
            UserId = "user123",
            SmartContext = smartContext,
            Interaction = FhirInteraction.Read,
            ResourceType = "Schedule",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.Should().BeTrue();
        result.Filter.Should().NotBeNull();
        result.Filter!.PractitionerFilter.Should().Be("Practitioner/pract-456");
    }

    [Fact]
    public void Priority_Is40()
    {
        // Assert
        _handler.Priority.Should().Be(40);
    }

    [Fact]
    public async Task HandleAsync_PostSearchWithSearchConstraints_PatientScope_ReturnsDenied()
    {
        // Arrange - Patient scope with search constraints
        var httpContext = Substitute.For<HttpContext>();
        var request = Substitute.For<HttpRequest>();
        request.Method.Returns("POST");
        httpContext.Request.Returns(request);

        var scopes = new List<SmartScope>
        {
            new()
            {
                Type = SmartScopeType.Patient,
                ResourceType = "Observation",
                Permissions = SmartPermissions.Read | SmartPermissions.Search,
                PermissionString = "RS",
                OriginalScope = "patient/Observation.rs?category=laboratory",
                SearchConstraints = new Dictionary<string, string> { { "category", "laboratory" } }
            }
        };

        var smartContext = new SmartAuthorizationContext
        {
            TokenClaims = new SmartTokenClaims { ScopeString = "patient/Observation.rs?category=laboratory", Scopes = scopes, PatientId = "patient-123" },
            Scopes = scopes,
            PatientContext = "patient-123"
        };

        var context = new FhirAuthorizationContext
        {
            RequestContext = CreateRequestContext(),
            UserId = "user123",
            SmartContext = smartContext,
            Interaction = FhirInteraction.SearchType,
            ResourceType = "Observation",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.Should().BeFalse();
        result.DenialReason.Should().Contain("POST _search is not supported with constrained SMART scopes");
    }

    [Fact]
    public async Task HandleAsync_GetSearchWithSearchConstraints_PatientScope_ReturnsSuccessWithFilter()
    {
        // Arrange - Patient scope with search constraints, using GET
        var httpContext = Substitute.For<HttpContext>();
        var request = Substitute.For<HttpRequest>();
        request.Method.Returns("GET");
        httpContext.Request.Returns(request);

        var scopes = new List<SmartScope>
        {
            new()
            {
                Type = SmartScopeType.Patient,
                ResourceType = "Observation",
                Permissions = SmartPermissions.Read | SmartPermissions.Search,
                PermissionString = "RS",
                OriginalScope = "patient/Observation.rs?category=laboratory",
                SearchConstraints = new Dictionary<string, string> { { "category", "laboratory" } }
            }
        };

        var smartContext = new SmartAuthorizationContext
        {
            TokenClaims = new SmartTokenClaims { ScopeString = "patient/Observation.rs?category=laboratory", Scopes = scopes, PatientId = "patient-123" },
            Scopes = scopes,
            PatientContext = "patient-123"
        };

        var context = new FhirAuthorizationContext
        {
            RequestContext = CreateRequestContext(),
            UserId = "user123",
            SmartContext = smartContext,
            Interaction = FhirInteraction.SearchType,
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
        result.Filter.SearchFilters.Should().ContainKey("category");
        result.Filter.SearchFilters!["category"].Should().Be("laboratory");
    }

    [Fact]
    public async Task HandleAsync_PostSearchWithSearchConstraints_UserScope_ReturnsDenied()
    {
        // Arrange - User scope with search constraints
        var httpContext = Substitute.For<HttpContext>();
        var request = Substitute.For<HttpRequest>();
        request.Method.Returns("POST");
        httpContext.Request.Returns(request);

        var scopes = new List<SmartScope>
        {
            new()
            {
                Type = SmartScopeType.User,
                ResourceType = "Observation",
                Permissions = SmartPermissions.Read | SmartPermissions.Search,
                PermissionString = "RS",
                OriginalScope = "user/Observation.rs?status=final",
                SearchConstraints = new Dictionary<string, string> { { "status", "final" } }
            }
        };

        var smartContext = new SmartAuthorizationContext
        {
            TokenClaims = new SmartTokenClaims { ScopeString = "user/Observation.rs?status=final", Scopes = scopes },
            Scopes = scopes
        };

        var context = new FhirAuthorizationContext
        {
            RequestContext = CreateRequestContext(),
            UserId = "user123",
            SmartContext = smartContext,
            Interaction = FhirInteraction.SearchType,
            ResourceType = "Observation",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.Should().BeFalse();
        result.DenialReason.Should().Contain("POST _search is not supported with constrained SMART scopes");
    }

    [Fact]
    public async Task HandleAsync_PostSearchWithoutSearchConstraints_PatientScope_ReturnsSuccessWithFilter()
    {
        // Arrange - Patient scope WITHOUT search constraints (POST _search is allowed)
        var httpContext = Substitute.For<HttpContext>();
        var request = Substitute.For<HttpRequest>();
        request.Method.Returns("POST");
        httpContext.Request.Returns(request);

        var scopes = new List<SmartScope>
        {
            new()
            {
                Type = SmartScopeType.Patient,
                ResourceType = "Observation",
                Permissions = SmartPermissions.Read | SmartPermissions.Search,
                PermissionString = "RS",
                OriginalScope = "patient/Observation.rs"
                // No SearchConstraints
            }
        };

        var smartContext = new SmartAuthorizationContext
        {
            TokenClaims = new SmartTokenClaims { ScopeString = "patient/Observation.rs", Scopes = scopes, PatientId = "patient-123" },
            Scopes = scopes,
            PatientContext = "patient-123"
        };

        var context = new FhirAuthorizationContext
        {
            RequestContext = CreateRequestContext(),
            UserId = "user123",
            SmartContext = smartContext,
            Interaction = FhirInteraction.SearchType,
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
        result.Filter.SearchFilters.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_PostSearchWithSearchConstraints_SystemScope_ReturnsDenied()
    {
        // Arrange - System scope with search constraints
        var httpContext = Substitute.For<HttpContext>();
        var request = Substitute.For<HttpRequest>();
        request.Method.Returns("POST");
        httpContext.Request.Returns(request);

        var scopes = new List<SmartScope>
        {
            new()
            {
                Type = SmartScopeType.System,
                ResourceType = "Patient",
                Permissions = SmartPermissions.Read | SmartPermissions.Search,
                PermissionString = "RS",
                OriginalScope = "system/Patient.rs?active=true",
                SearchConstraints = new Dictionary<string, string> { { "active", "true" } }
            }
        };

        var smartContext = new SmartAuthorizationContext
        {
            TokenClaims = new SmartTokenClaims { ScopeString = "system/Patient.rs?active=true", Scopes = scopes },
            Scopes = scopes
        };

        var context = new FhirAuthorizationContext
        {
            RequestContext = CreateRequestContext(),
            UserId = "system-client",
            SmartContext = smartContext,
            Interaction = FhirInteraction.SearchType,
            ResourceType = "Patient",
            HttpContext = httpContext,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await _handler.HandleAsync(context, CancellationToken.None);

        // Assert
        result.Allowed.Should().BeFalse();
        result.DenialReason.Should().Contain("POST _search is not supported with constrained SMART scopes");
    }

    private static IFhirRequestContext CreateRequestContext(int tenantId = 1)
    {
        var context = Substitute.For<IFhirRequestContext>();
        context.TenantId.Returns(tenantId);
        return context;
    }
}
