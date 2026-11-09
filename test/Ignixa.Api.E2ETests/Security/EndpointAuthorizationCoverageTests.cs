using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.Api.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Ignixa.Api.E2ETests.Security;

/// <summary>
/// Census of the composed application's routes: every endpoint runs <see cref="FhirAuthorizationFilter"/> unless it
/// is deliberately public, and every operation route declares the interaction it authorizes as.
/// </summary>
[Collection(E2ETestCollection.Name)]
public class EndpointAuthorizationCoverageTests(IgnixaApiFixture fixture)
{
    /// <summary>Routes that must answer anonymous callers: discovery and liveness.</summary>
    private static readonly HashSet<string> PublicRoutes =
    [
        "GET /metadata",
        "GET /tenant/{tenantId:int}/metadata",
        "GET /.well-known/smart-configuration",
        "GET /tenant/{tenantId:int}/.well-known/smart-configuration",
        "GET /health/check",
    ];

    [Fact]
    public void GivenTheComposedApplication_WhenListingEndpoints_ThenEveryNonPublicRouteIsAuthorized()
    {
        var unauthorized = Routes()
            .Where(route => route.Endpoint.Metadata.GetMetadata<FhirAuthorizationRequiredMetadata>() is null)
            .Select(route => route.Key)
            .Order()
            .ToList();

        unauthorized.ShouldBe(PublicRoutes.Order().ToList());
    }

    [Fact]
    public void GivenTheComposedApplication_WhenListingOperationRoutes_ThenEachDeclaresItsInteraction()
    {
        var operations = Routes().Where(route => route.Key.Contains('$', StringComparison.Ordinal)).ToList();

        operations.ShouldNotBeEmpty();
        operations
            .Where(route => route.Endpoint.Metadata.GetMetadata<FhirAuthorizationMetadata>() is null)
            .Select(route => route.Key)
            .ShouldBeEmpty();
    }

    private IEnumerable<(string Key, RouteEndpoint Endpoint)> Routes() =>
        fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(method => ($"{method} {endpoint.RoutePattern.RawText}", endpoint)));
}
