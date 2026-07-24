// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.Middleware;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Middleware;

public class TenantResolutionHostnameTests
{
    private static TenantConfiguration Tenant(int id, params string[] hosts) => new()
    {
        TenantId = id,
        DisplayName = $"T{id}",
        FhirVersion = "4.0",
        Hostnames = hosts,
    };

    [Fact]
    public async Task GivenARequestOnATenantHost_WhenResolved_ThenTenantIsSetFromTheHost()
    {
        // Arrange
        var store = Substitute.For<ITenantConfigurationStore>();
        store.ResolveByHostAsync("fhir2.example.org", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>(Tenant(2, "fhir2.example.org")));

        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString("fhir2.example.org");
        ctx.Request.Path = "/Patient";
        ctx.Request.Method = "GET";
        var mw = new TenantResolutionMiddleware(_ => Task.CompletedTask, store, NullLogger<TenantResolutionMiddleware>.Instance);

        // Act
        await mw.InvokeAsync(ctx);

        // Assert
        ctx.Items["TenantId"].ShouldBe(2);
    }

    [Fact]
    public async Task GivenAHostAndPathThatDisagree_WhenResolved_ThenReturns400()
    {
        // Arrange — host says tenant 1, path says tenant 2.
        var store = Substitute.For<ITenantConfigurationStore>();
        store.ResolveByHostAsync("fhir1.example.org", Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>(Tenant(1, "fhir1.example.org")));

        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Host = new HostString("fhir1.example.org");
        ctx.Request.Path = "/tenant/2/Patient";
        ctx.Request.RouteValues["tenantId"] = "2";
        ctx.Request.Method = "GET";
        var nextCalled = false;
        var mw = new TenantResolutionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, store, NullLogger<TenantResolutionMiddleware>.Instance);

        // Act
        await mw.InvokeAsync(ctx);

        // Assert
        ctx.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        nextCalled.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAnUnknownHostAndNoRoute_WhenResolved_ThenFallsThroughToAutoDetect()
    {
        // Arrange — unknown host must not resolve a tenant; single active tenant auto-detects.
        var store = Substitute.For<ITenantConfigurationStore>();
        store.ResolveByHostAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>((TenantConfiguration?)null));
        store.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TenantConfiguration>>(new[] { Tenant(1) }));
        store.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>(Tenant(1)));

        var ctx = new DefaultHttpContext();
        ctx.Request.Host = new HostString("nothing.example.org");
        ctx.Request.Path = "/Patient";
        ctx.Request.Method = "GET";
        var mw = new TenantResolutionMiddleware(_ => Task.CompletedTask, store, NullLogger<TenantResolutionMiddleware>.Instance);

        // Act
        await mw.InvokeAsync(ctx);

        // Assert
        ctx.Items["TenantId"].ShouldBe(1);
    }
}
