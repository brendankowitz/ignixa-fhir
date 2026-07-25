// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Infrastructure;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

/// <summary>
/// The resolver is the single authority for "which base URIs identify this server for tenant N". Every
/// path — request, bundle entry, reindex, $import — goes through it, so if it can be made to answer two
/// different things for one tenant, those paths will store the same reference two different ways.
/// </summary>
public class FhirServiceBaseUriResolverTests
{
    private static readonly Uri RequestOrigin = new("https://fhir.example.org");

    [Fact]
    public void GivenATenant_WhenResolving_ThenBothTheRootAndTheTenantScopedBaseAreRecognized()
    {
        var resolver = new FhirServiceBaseUriResolver();

        var bases = resolver.Resolve(RequestOrigin, tenantId: 1);

        bases.ShouldBe(
            [new Uri("https://fhir.example.org/"), new Uri("https://fhir.example.org/tenant/1/")],
            ignoreOrder: true);
    }

    [Fact]
    public void GivenTheSameTenant_WhenTheCanonicalFormDiffers_ThenTheRecognizedSetIsUnchanged()
    {
        var resolver = new FhirServiceBaseUriResolver();

        var asRoot = resolver.Resolve(RequestOrigin, 1, FhirServiceBaseUriForm.Root);
        var asTenantScoped = resolver.Resolve(RequestOrigin, 1, FhirServiceBaseUriForm.TenantScoped);

        asRoot.ShouldBe(asTenantScoped, ignoreOrder: true);
        asRoot[0].ShouldBe(new Uri("https://fhir.example.org/"));
        asTenantScoped[0].ShouldBe(new Uri("https://fhir.example.org/tenant/1/"));
    }

    [Fact]
    public void GivenAConfiguredServiceRoot_WhenTheRequestOriginDiffers_ThenTheConfiguredRootWins()
    {
        var resolver = new FhirServiceBaseUriResolver(new Uri("https://fhir.example.org/"));

        var bases = resolver.Resolve(new Uri("https://attacker.example.net"), tenantId: 1);

        bases.ShouldNotContain(new Uri("https://attacker.example.net/"));
        bases.ShouldContain(new Uri("https://fhir.example.org/"));
    }

    [Fact]
    public void GivenAConfiguredRootWithoutATrailingSlash_WhenResolving_ThenItIsNormalizedToADirectory()
    {
        var resolver = new FhirServiceBaseUriResolver(new Uri("https://fhir.example.org/fhir"));

        resolver.ConfiguredServiceRoot.ShouldBe(new Uri("https://fhir.example.org/fhir/"));
        resolver.Resolve(requestOrigin: null, tenantId: 2).ShouldBe(
            [new Uri("https://fhir.example.org/fhir/"), new Uri("https://fhir.example.org/fhir/tenant/2/")],
            ignoreOrder: true);
    }

    [Fact]
    public void GivenNoTenant_WhenResolving_ThenOnlyTheRootIsRecognized()
    {
        var resolver = new FhirServiceBaseUriResolver();

        resolver.Resolve(RequestOrigin, tenantId: null).ShouldBe([new Uri("https://fhir.example.org/")]);
    }

    [Fact]
    public void GivenTheReservedSystemPartition_WhenResolving_ThenNoTenantRouteBaseIsEmitted()
    {
        var resolver = new FhirServiceBaseUriResolver();

        resolver.Resolve(RequestOrigin, tenantId: 0)
            .ShouldNotContain(new Uri("https://fhir.example.org/tenant/0/"));
    }

    [Fact]
    public void GivenNeitherAConfiguredRootNorARequestOrigin_WhenResolving_ThenNothingIsRecognized()
    {
        new FhirServiceBaseUriResolver().Resolve(requestOrigin: null, tenantId: 1).ShouldBeEmpty();
    }
}
