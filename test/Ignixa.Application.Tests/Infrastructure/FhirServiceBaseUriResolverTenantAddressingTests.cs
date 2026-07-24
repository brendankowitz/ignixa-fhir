// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Infrastructure;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

public class FhirServiceBaseUriResolverTenantAddressingTests
{
    private static readonly Uri Root = new("https://example.org/");

    [Fact]
    public void GivenAConfiguredHostname_WhenResolving_ThenTheHostIsCanonicalAndThePathFormIsAlsoRecognized()
    {
        // Arrange
        var resolver = new FhirServiceBaseUriResolver(Root);
        var tenant = new TenantAddressing(1, ["fhir1.example.org"], IncludeDeploymentRoot: false);

        // Act
        var bases = resolver.Resolve(requestOrigin: null, tenant);

        // Assert
        bases[0].ShouldBe(new Uri("https://fhir1.example.org/"));
        bases.ShouldContain(new Uri("https://example.org/tenant/1/"));
        bases.ShouldNotContain(Root);
    }

    [Fact]
    public void GivenNoHostname_WhenResolving_ThenThePathFormIsCanonical()
    {
        // Arrange
        var resolver = new FhirServiceBaseUriResolver(Root);
        var tenant = new TenantAddressing(2, [], IncludeDeploymentRoot: false);

        // Act
        var bases = resolver.Resolve(requestOrigin: null, tenant);

        // Assert
        bases[0].ShouldBe(new Uri("https://example.org/tenant/2/"));
    }

    [Fact]
    public void GivenTheSoleTenant_WhenResolving_ThenTheDeploymentRootIsRecognized()
    {
        // Arrange
        var resolver = new FhirServiceBaseUriResolver(Root);
        var tenant = new TenantAddressing(1, [], IncludeDeploymentRoot: true);

        // Act
        var bases = resolver.Resolve(requestOrigin: null, tenant);

        // Assert
        bases.ShouldContain(Root);
    }

    [Fact]
    public void GivenNoConfiguredRootAndNoRequestOrigin_WhenResolving_ThenTheSetIsEmpty()
    {
        // Arrange
        var resolver = new FhirServiceBaseUriResolver(configuredServiceRoot: null);
        var tenant = new TenantAddressing(1, ["fhir1.example.org"], IncludeDeploymentRoot: false);

        // Act
        var bases = resolver.Resolve(requestOrigin: null, tenant);

        // Assert
        bases.ShouldBeEmpty();
    }

    [Fact]
    public void GivenMultipleHostnames_WhenResolving_ThenAllAreRecognizedCanonicalFirst()
    {
        // Arrange
        var resolver = new FhirServiceBaseUriResolver(Root);
        var tenant = new TenantAddressing(1, ["fhir1.example.org", "acme.example.org"], IncludeDeploymentRoot: false);

        // Act
        var bases = resolver.Resolve(requestOrigin: null, tenant);

        // Assert
        bases[0].ShouldBe(new Uri("https://fhir1.example.org/"));
        bases.ShouldContain(new Uri("https://acme.example.org/"));
    }
}
