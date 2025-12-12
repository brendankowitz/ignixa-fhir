// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Domain;
using Ignixa.Domain.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Ignixa.Application.Tests.Features.Authorization;

public class CapabilityEnforcementHandlerTests
{
    private readonly IFhirVersionContext _versionContext;
    private readonly IFhirRequestContextAccessor _fhirContextAccessor;
    private readonly CapabilityEnforcementHandler _handler;

    public CapabilityEnforcementHandlerTests()
    {
        _versionContext = Substitute.For<IFhirVersionContext>();
        _fhirContextAccessor = Substitute.For<IFhirRequestContextAccessor>();

        // Create a real CapabilityStatementService with minimal mocked dependencies
        var segments = Enumerable.Empty<ICapabilitySegment>();
        var cache = Substitute.For<ICapabilityCache>();
        var tenantConfigStore = Substitute.For<ITenantConfigurationStore>();
        var versionInfo = Substitute.For<IApplicationVersionInfo>();

        var capabilityService = new CapabilityStatementService(
            segments,
            cache,
            tenantConfigStore,
            _versionContext,
            versionInfo,
            NullLogger<CapabilityStatementService>.Instance);

        _handler = new CapabilityEnforcementHandler(
            capabilityService,
            _versionContext,
            _fhirContextAccessor,
            NullLogger<CapabilityEnforcementHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_MetadataEndpoint_Bypasses()
    {
        // Arrange
        var httpContext = Substitute.For<HttpContext>();
        var context = new FhirAuthorizationContext
        {
            UserId = "user123",
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
    public void Priority_Is50()
    {
        // Assert
        _handler.Priority.Should().Be(50);
    }

    [Fact]
    public void InvalidateCache_DoesNotThrow()
    {
        // Act - should not throw
        var action = () => _handler.InvalidateCache("1");

        // Assert - no exception
        action.Should().NotThrow();
    }

    [Fact]
    public void ClearAllCaches_DoesNotThrow()
    {
        // Act - should not throw
        var action = () => _handler.ClearAllCaches();

        // Assert - no exception
        action.Should().NotThrow();
    }
}
