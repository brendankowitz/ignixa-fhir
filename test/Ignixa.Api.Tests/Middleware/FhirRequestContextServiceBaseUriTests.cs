// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Api.Middleware;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Specification.Generated;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Middleware;

/// <summary>
/// A single-tenant deployment serves the same resource at /Patient/1 and at /tenant/1/Patient/1, and hands
/// out absolute links in whichever form the caller used. If the base URI a request indexes against depends
/// on the route form, a reference ingested via one route becomes unfindable by an absolute search issued
/// via the other — same server, same tenant, two answers. These tests pin that the recognized set of
/// service bases is a function of the tenant, not of the route.
/// </summary>
public class FhirRequestContextServiceBaseUriTests
{
    private const int TenantId = 1;

    [Fact]
    public async Task GivenTheTwoRouteForms_WhenBuildingTheRequestContext_ThenBothRecognizeTheSameServiceBases()
    {
        var agnostic = await BuildContextAsync("/Patient", new FhirServiceBaseUriResolver());
        var tenantExplicit = await BuildContextAsync("/tenant/1/Patient", new FhirServiceBaseUriResolver());

        agnostic.ServiceBaseUris.ShouldBe(tenantExplicit.ServiceBaseUris, ignoreOrder: true);
        agnostic.ServiceBaseUris.ShouldBe(
            [new Uri("https://fhir.example.org/"), new Uri("https://fhir.example.org/tenant/1/")],
            ignoreOrder: true);
    }

    [Theory]
    [InlineData("/Patient", "https://fhir.example.org/tenant/1/Patient/p1")]
    [InlineData("/Patient", "https://fhir.example.org/Patient/p1")]
    [InlineData("/tenant/1/Patient", "https://fhir.example.org/tenant/1/Patient/p1")]
    [InlineData("/tenant/1/Patient", "https://fhir.example.org/Patient/p1")]
    public async Task GivenAnAbsoluteSelfReference_WhenParsedUnderEitherRouteForm_ThenItCollapsesToInternal(
        string requestPath,
        string reference)
    {
        var parsed = await ParseUnderRequestAsync(requestPath, reference, new FhirServiceBaseUriResolver());

        parsed.Kind.ShouldBe(ReferenceKind.Internal);
        parsed.BaseUri.ShouldBeNull();
        parsed.ResourceType.ShouldBe("Patient");
        parsed.ResourceId.ShouldBe("p1");
    }

    [Fact]
    public async Task GivenAReferenceToAnotherServer_WhenParsed_ThenItStaysExternal()
    {
        var parsed = await ParseUnderRequestAsync(
            "/Patient",
            "https://other.example.org/Patient/p1",
            new FhirServiceBaseUriResolver());

        parsed.Kind.ShouldBe(ReferenceKind.External);
        parsed.BaseUri.ShouldBe(new Uri("https://other.example.org/"));
    }

    [Fact]
    public async Task GivenAConfiguredServiceRoot_WhenTheHostHeaderIsForged_ThenTheForgedHostIsNotTreatedAsThisServer()
    {
        var resolver = new FhirServiceBaseUriResolver(new Uri("https://fhir.example.org/"));

        var context = await BuildContextAsync("/Patient", resolver, host: "attacker.example.net");

        context.ServiceBaseUris.ShouldNotContain(new Uri("https://attacker.example.net/"));
        context.ServiceBaseUris.ShouldBe(
            [new Uri("https://fhir.example.org/"), new Uri("https://fhir.example.org/tenant/1/")],
            ignoreOrder: true);

        var parsed = await ParseUnderRequestAsync(
            "/Patient",
            "https://attacker.example.net/Patient/p1",
            resolver,
            host: "attacker.example.net");

        parsed.Kind.ShouldBe(ReferenceKind.External);
    }

    [Fact]
    public async Task GivenAConfiguredServiceRootWithoutATrailingSlash_WhenParsingASelfReference_ThenItStillCollapses()
    {
        var resolver = new FhirServiceBaseUriResolver(new Uri("https://fhir.example.org/fhir"));

        var parsed = await ParseUnderRequestAsync(
            "/Patient",
            "https://fhir.example.org/fhir/Patient/p1",
            resolver);

        parsed.Kind.ShouldBe(ReferenceKind.Internal);
    }

    [Fact]
    public async Task GivenATenantExplicitRoute_WhenBuildingTheRequestContext_ThenTheCanonicalBaseMatchesTheRoute()
    {
        var agnostic = await BuildContextAsync("/Patient", new FhirServiceBaseUriResolver());
        var tenantExplicit = await BuildContextAsync("/tenant/1/Patient", new FhirServiceBaseUriResolver());

        agnostic.BaseUri.ShouldBe(new Uri("https://fhir.example.org/"));
        tenantExplicit.BaseUri.ShouldBe(new Uri("https://fhir.example.org/tenant/1/"));
    }

    [Fact]
    public async Task GivenARequestOnATenantHost_WhenContextBuilt_ThenBaseUriIsTheCanonicalHost()
    {
        var resolvedTenant = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Acme",
            FhirVersion = "4.0",
            Hostnames = new[] { "fhir1.example.org" },
        };

        var context = await BuildContextAsync(
            "/Patient",
            new FhirServiceBaseUriResolver(new Uri("https://example.org/")),
            host: "fhir1.example.org",
            resolvedTenant: resolvedTenant);

        context.BaseUri.ShouldBe(new Uri("https://fhir1.example.org/"));
        context.ServiceBaseUris.ShouldContain(new Uri("https://example.org/tenant/1/"));
    }

    /// <summary>
    /// Regression guard: a single-tenant deployment with no hostnames configured must keep emitting the
    /// deployment root as its canonical base after upgrading to hostname-aware tenant resolution. Before the
    /// fix, the resolver appended the tenant/{id}/ path form before the deployment root, silently flipping
    /// Location headers, Bundle.entry.fullUrl, and pagination links for deployments nobody reconfigured.
    /// </summary>
    [Fact]
    public async Task GivenAHostnameLessSoleTenant_WhenBuildingTheRequestContext_ThenTheDeploymentRootIsCanonical()
    {
        var resolvedTenant = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Acme",
            FhirVersion = "4.0",
            Hostnames = Array.Empty<string>(),
        };

        var context = await BuildContextAsync(
            "/Patient",
            new FhirServiceBaseUriResolver(new Uri("https://example.org/")),
            resolvedTenant: resolvedTenant);

        context.BaseUri.ShouldBe(new Uri("https://example.org/"));
    }

    private static Task<ReferenceSearchValue> ParseUnderRequestAsync(
        string requestPath,
        string reference,
        FhirServiceBaseUriResolver resolver,
        string host = "fhir.example.org")
        => DuringRequestAsync(requestPath, resolver, host, accessor =>
        {
            var parser = new ReferenceSearchValueParser(
                new R4CoreSchemaProvider(),
                new FhirRequestContextBaseUriProvider(accessor, resolver, Substitute.For<ITenantConfigurationStore>()));

            return parser.Parse(reference);
        });

    private static Task<IFhirRequestContext> BuildContextAsync(
        string requestPath,
        FhirServiceBaseUriResolver resolver,
        string host = "fhir.example.org",
        TenantConfiguration? resolvedTenant = null)
        => DuringRequestAsync(requestPath, resolver, host, accessor => accessor.RequestContext!, resolvedTenant);

    /// <summary>
    /// Runs the middleware and evaluates <paramref name="observe"/> from inside the pipeline. The context
    /// lives in an <see cref="AsyncLocal{T}"/>, so a value the middleware sets is not visible to the caller
    /// once InvokeAsync has returned — downstream is the only place it can be read.
    /// </summary>
    private static async Task<T> DuringRequestAsync<T>(
        string requestPath,
        FhirServiceBaseUriResolver resolver,
        string host,
        Func<IFhirRequestContextAccessor, T> observe,
        TenantConfiguration? resolvedTenant = null)
    {
        var accessor = new FhirRequestContextAccessor
        {
            RequestContext = null
        };

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString(host);
        httpContext.Request.Path = requestPath;
        httpContext.Items["TenantId"] = TenantId;

        if (resolvedTenant is not null)
        {
            httpContext.Items["TenantConfiguration"] = resolvedTenant;
        }

        var configStore = Substitute.For<ITenantConfigurationStore>();
        configStore.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TenantConfiguration>>(
                resolvedTenant is null ? Array.Empty<TenantConfiguration>() : [resolvedTenant]));

        T? observed = default;

        var middleware = new FhirRequestContextMiddleware(
            _ =>
            {
                observed = observe(accessor);
                return Task.CompletedTask;
            },
            NullLogger<FhirRequestContextMiddleware>.Instance);

        await middleware.InvokeAsync(
            httpContext,
            accessor,
            Substitute.For<IFhirVersionContext>(),
            resolver,
            configStore);

        return observed!;
    }
}
