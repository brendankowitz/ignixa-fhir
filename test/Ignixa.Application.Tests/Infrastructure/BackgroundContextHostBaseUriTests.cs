// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

/// <summary>
/// Reindex and $import build an <see cref="IFhirRequestContext"/> with no request behind it. Before this
/// fix, the provider's fallback for that context called the path-form <c>Resolve</c> overload, so a
/// tenant addressed by hostname on the request path was reachable only via <c>/tenant/{id}/</c> from
/// background indexing. This pins that both paths reach the tenant's configured hostname.
/// </summary>
public class BackgroundContextHostBaseUriTests
{
    [Fact]
    public async Task GivenABackgroundContextForATenantWithAHost_WhenResolvingBaseUris_ThenItMatchesTheRequestPath()
    {
        // Arrange
        var store = Substitute.For<ITenantConfigurationStore>();
        var tenantConfiguration = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Acme",
            FhirVersion = "4.0",
            Hostnames = ["fhir1.example.org"],
        };
        IReadOnlyList<TenantConfiguration> allTenants =
        [
            tenantConfiguration,
            new TenantConfiguration { TenantId = 2, DisplayName = "Beta", FhirVersion = "4.0" },
        ];
        store.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>(tenantConfiguration));
        store.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TenantConfiguration>>(allTenants));

        var accessor = new FhirRequestContextAccessor
        {
            RequestContext = FhirRequestContextFactory.CreateBackgroundContext(1),
        };
        var resolver = new FhirServiceBaseUriResolver(new Uri("https://example.org/"));
        var provider = new FhirRequestContextBaseUriProvider(accessor, resolver, store);

        // Act
        var bases = provider.GetServiceBaseUris();

        // Assert
        bases[0].ShouldBe(new Uri("https://fhir1.example.org/"));
    }
}
