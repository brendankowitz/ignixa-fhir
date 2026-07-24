// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

/// <summary>
/// Reindex and $import index resources with no HTTP request. If they cannot reach the same answer the
/// request path reached, a reindexed row stores a self-reference as external while the row it replaced
/// stored it as internal, and the resource silently drops out of absolute searches.
/// </summary>
public class FhirRequestContextBaseUriProviderTests
{
    private static readonly Uri ServiceRoot = new("https://fhir.example.org/");

    private static ITenantConfigurationStore UnconfiguredStore() => Substitute.For<ITenantConfigurationStore>();

    [Fact]
    public void GivenABackgroundContextForATenant_WhenResolving_ThenItAgreesWithTheRequestPath()
    {
        var resolver = new FhirServiceBaseUriResolver(ServiceRoot);
        var accessor = new FhirRequestContextAccessor
        {
            RequestContext = FhirRequestContextFactory.CreateBackgroundContext(tenantId: 1)
        };

        IFhirBaseUriProvider provider = new FhirRequestContextBaseUriProvider(accessor, resolver, UnconfiguredStore());

        provider.GetServiceBaseUris().ShouldBe(
            resolver.Resolve(requestOrigin: null, tenantId: 1),
            ignoreOrder: true);
        provider.IsServiceBaseUri(new Uri("https://fhir.example.org/")).ShouldBeTrue();
        provider.IsServiceBaseUri(new Uri("https://fhir.example.org/tenant/1/")).ShouldBeTrue();
    }

    [Fact]
    public void GivenNoContextAndNoConfiguredRoot_WhenResolving_ThenNothingIsRecognized()
    {
        IFhirBaseUriProvider provider = new FhirRequestContextBaseUriProvider(
            new FhirRequestContextAccessor { RequestContext = null },
            new FhirServiceBaseUriResolver(),
            UnconfiguredStore());

        provider.GetBaseUri().ShouldBeNull();
        provider.GetServiceBaseUris().ShouldBeEmpty();
        provider.IsServiceBaseUri(ServiceRoot).ShouldBeFalse();
    }

    [Fact]
    public void GivenARequestContext_WhenResolving_ThenTheRequestsOwnSetIsUsed()
    {
        var accessor = new FhirRequestContextAccessor
        {
            RequestContext = new FhirRequestContext
            {
                TenantId = 1,
                ServiceBaseUris = [new Uri("https://from-request.example.org/")]
            }
        };

        IFhirBaseUriProvider provider = new FhirRequestContextBaseUriProvider(
            accessor,
            new FhirServiceBaseUriResolver(ServiceRoot),
            UnconfiguredStore());

        provider.GetBaseUri().ShouldBe(new Uri("https://from-request.example.org/"));
    }

    [Fact]
    public void GivenAnUnslashedCandidate_WhenTestingRecognition_ThenItStillMatches()
    {
        IFhirBaseUriProvider provider = new FhirRequestContextBaseUriProvider(
            new FhirRequestContextAccessor { RequestContext = null },
            new FhirServiceBaseUriResolver(new Uri("https://fhir.example.org/fhir/")),
            UnconfiguredStore());

        provider.IsServiceBaseUri(new Uri("https://fhir.example.org/fhir")).ShouldBeTrue();
        provider.IsServiceBaseUri(new Uri("https://FHIR.EXAMPLE.ORG/fhir/")).ShouldBeTrue();
        provider.IsServiceBaseUri(new Uri("https://fhir.example.org/other/")).ShouldBeFalse();
    }
}
