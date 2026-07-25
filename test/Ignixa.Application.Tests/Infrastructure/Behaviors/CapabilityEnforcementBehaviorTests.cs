// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Infrastructure.Behaviors;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure.Behaviors;

/// <summary>
/// Pins that <see cref="CapabilityEnforcementBehavior{TRequest, TResponse}"/> rejects a request whose
/// required capability the CapabilityStatement does not declare with a 403 <see cref="ForbiddenException"/>,
/// not a 400. This is the revert of the anti-pattern the row-generator fix in this PR also closes: a plain
/// <see cref="InvalidOperationException"/> would fall through the middleware's generic handler to 400.
/// </summary>
public class CapabilityEnforcementBehaviorTests
{
    [Fact]
    public async Task GivenARequestWhoseRequiredCapabilityIsAbsent_WhenHandling_ThenThrowsForbiddenWithStatus403()
    {
        // Arrange: a rest component with no resource entries means any capability requirement expression
        // that checks for a specific resource type's interaction evaluates to false.
        var segments = new ICapabilitySegment[] { new MinimalRestCapabilitySegment() };

        var cache = Substitute.For<ICapabilityCache>();
        cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CapabilityCacheEntry?>((CapabilityCacheEntry?)null));

        var tenantConfigStore = Substitute.For<ITenantConfigurationStore>();
        tenantConfigStore.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TenantConfiguration>>(Array.Empty<TenantConfiguration>()));

        var versionInfo = Substitute.For<IApplicationVersionInfo>();
        versionInfo.Version.Returns("1.0.0");

        // Real FhirVersionContext (not mocked): the behavior evaluates a FHIRPath expression against the
        // CapabilityStatement, which needs a real schema provider to resolve, not a substitute.
        var loggerFactory = Substitute.For<ILoggerFactory>();
        var versionContext = new FhirVersionContext(
            loggerFactory,
            new SearchParameterResolutionOptions(),
            NullFhirBaseUriProvider.Instance);

        var capabilityService = new CapabilityStatementService(
            segments,
            cache,
            tenantConfigStore,
            versionContext,
            versionInfo,
            NullLogger<CapabilityStatementService>.Instance);

        var contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        contextAccessor.RequestContext.Returns((IFhirRequestContext?)null);

        var behavior = new CapabilityEnforcementBehavior<GetResourceQuery, SearchEntryResult?>(
            capabilityService,
            tenantConfigStore,
            contextAccessor,
            versionContext,
            NullLogger<CapabilityEnforcementBehavior<GetResourceQuery, SearchEntryResult?>>.Instance);

        var request = new GetResourceQuery("Measure", "does-not-matter");

        // Act & Assert
        var exception = await Should.ThrowAsync<ForbiddenException>(
            async () => await behavior.HandleAsync(
                request,
                () => Task.FromResult<SearchEntryResult?>(null),
                CancellationToken.None));

        exception.StatusCode.ShouldBe(403);
    }

    /// <summary>
    /// Adds a bare "rest" component with no resource entries, mirroring what
    /// <see cref="Ignixa.Application.Features.Metadata.Segments.ResourceInteractionCapabilitySegment"/> and
    /// its siblings do for the first segment applied, without pulling in their extra dependencies.
    /// </summary>
    private sealed class MinimalRestCapabilitySegment : ICapabilitySegment
    {
        public string SegmentKey => "test-minimal-rest";

        public int Priority => 1;

        public ValueTask ApplyAsync(
            CapabilityStatementJsonNode statement,
            CapabilityContext context,
            CancellationToken cancellationToken)
        {
            if (statement.Rest.Count == 0)
            {
                statement.Rest.Add(new RestComponentJsonNode
                {
                    Mode = RestComponentJsonNode.RestfulCapabilityMode.Server,
                });
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<string> GetVersionHashAsync(CapabilityContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult("static");
    }
}
