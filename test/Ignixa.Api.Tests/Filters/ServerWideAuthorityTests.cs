using System.Security.Claims;
using System.Text;
using Ignixa.Api.Endpoints;
using Ignixa.Application.Features.Admin;
using Ignixa.Application.Features.Authorization;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Services;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Filters;

/// <summary>
/// Package load and unload change conformance definitions every tenant shares, so while several tenants are active
/// they need <c>SystemAdmin</c> on top of the route's own grant.
/// </summary>
public sealed class ServerWideAuthorityTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ITenantConfigurationStore _tenants = Substitute.For<ITenantConfigurationStore>();
    private bool _dispatched;

    [Theory]
    [InlineData("LoadPackage")]
    [InlineData("UnloadPackage")]
    public async Task GivenSeveralActiveTenantsAndNoSystemAdminRole_WhenChangingPackages_ThenItIsForbiddenWithoutDispatching(
        string endpointName)
    {
        ActiveTenants(Tenant(1), Tenant(2));
        await using var app = BuildApp();

        var status = await SendAsync(app, endpointName, role: null);

        status.ShouldBe(StatusCodes.Status403Forbidden);
        _dispatched.ShouldBeFalse();
    }

    [Theory]
    [InlineData("LoadPackage", true, TenantIsolationHandler.SystemAdminRole)]
    [InlineData("UnloadPackage", true, TenantIsolationHandler.SystemAdminRole)]
    [InlineData("LoadPackage", false, null)]
    [InlineData("UnloadPackage", false, null)]
    public async Task GivenSystemAdminOrOneActiveTenant_WhenChangingPackages_ThenTheHandlerRuns(
        string endpointName,
        bool secondTenantActive,
        string? role)
    {
        ActiveTenants(Tenant(0), Tenant(1), Tenant(2) with { IsActive = secondTenantActive });
        await using var app = BuildApp();

        await SendAsync(app, endpointName, role);

        _dispatched.ShouldBeTrue();
    }

    // The handlers map a failed dispatch to an error response; only whether they dispatched matters here.
    private Task<TResult> Dispatch<TResult>()
    {
        _dispatched = true;
        throw new NotSupportedException("Dispatch reached.");
    }

    private void ActiveTenants(params TenantConfiguration[] tenants) =>
        _tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>()).Returns(tenants);

    private static TenantConfiguration Tenant(int tenantId) =>
        new() { TenantId = tenantId, DisplayName = $"Tenant {tenantId}", FhirVersion = "4.0", IsActive = true };

    private WebApplication BuildApp()
    {
        _mediator.SendAsync(Arg.Any<LoadPackageCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => Dispatch<LoadPackageResult>());
        _mediator.SendAsync(Arg.Any<UnloadPackageCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => Dispatch<UnloadPackageResult>());
        var permissions = Substitute.For<IRolePermissionStore>();
        permissions.GetPermissionsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var requestContext = Substitute.For<IFhirRequestContextAccessor>();
        requestContext.RequestContext.Returns(Substitute.For<IFhirRequestContext>());

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(_mediator);
        builder.Services.AddSingleton(_tenants);
        builder.Services.AddSingleton(requestContext);
        builder.Services.AddSingleton<IFhirAuthorizationService>(new FhirAuthorizationService(
            [
                new RbacAuthorizationHandler(permissions, NullLogger<RbacAuthorizationHandler>.Instance),
                new SmartScopeAuthorizationHandler(NullLogger<SmartScopeAuthorizationHandler>.Instance)
            ],
            NullLogger<FhirAuthorizationService>.Instance));
        builder.Services.AddSingleton(Options.Create(new AuthorizationOptions { Enabled = true }));
        var app = builder.Build();
        app.MapAdminPackageEndpoints();
        return app;
    }

    private static async Task<int> SendAsync(WebApplication app, string endpointName, string? role)
    {
        var endpoint = (RouteEndpoint)((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Single(candidate => candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == endpointName);
        List<Claim> claims = [new(FhirClaimTypes.Subject, "caller"), new(FhirClaimTypes.Scope, "system/*.cruds")];
        if (role is not null)
        {
            claims.Add(new Claim(FhirClaimTypes.Role, role));
        }

        await using var scope = app.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };
        context.SetEndpoint(endpoint);
        foreach (var (name, value) in new[] { ("tenantId", "1"), ("packageId", "package"), ("version", "1.0.0") })
        {
            context.Request.RouteValues[name] = value;
        }

        context.Request.Path = endpoint.RoutePattern.RawText!
            .Replace("{tenantId:int}", "1", StringComparison.Ordinal)
            .Replace("{packageId}", "package", StringComparison.Ordinal)
            .Replace("{version}", "1.0.0", StringComparison.Ordinal);
        context.Request.Method = endpoint.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods.Single();
        var body = Encoding.UTF8.GetBytes("""{"packageId":"package","version":"1.0.0"}""");
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Request.ContentType = "application/json";
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetectionFeature());
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);
        return context.Response.StatusCode;
    }

    private sealed class BodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
