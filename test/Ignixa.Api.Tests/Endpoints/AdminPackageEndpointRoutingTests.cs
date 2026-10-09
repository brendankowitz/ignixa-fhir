using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.Endpoints;
using Ignixa.Api.Middleware;
using Ignixa.Application.Features.Admin;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Endpoints;

public sealed class AdminPackageEndpointRoutingTests : IAsyncLifetime
{
    private readonly WebApplication _app;
    private readonly List<object> _requests = [];

    public AdminPackageEndpointRoutingTests()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<ListPackagesQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _requests.Add(call.Arg<ListPackagesQuery>());
            return new ListPackagesResult { Packages = [new PackageInfo { PackageId = "example.ig", Version = "1.2.3" }] };
        });
        mediator.SendAsync(Arg.Any<LoadPackageCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var command = call.Arg<LoadPackageCommand>();
            _requests.Add(command);
            return new LoadPackageResult { PackageId = command.PackageId, PackageVersion = command.Version, ImportedResources = 3 };
        });
        mediator.SendAsync(Arg.Any<UnloadPackageCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var command = call.Arg<UnloadPackageCommand>();
            _requests.Add(command);
            return new UnloadPackageResult { PackageId = command.PackageId, Version = command.Version, ResourcesDeactivated = 3 };
        });
        TenantConfiguration[] tenants =
        [
            new() { TenantId = 1, DisplayName = "One", FhirVersion = "4.0" },
            new() { TenantId = 2, DisplayName = "Two", FhirVersion = "4.0" },
        ];
        var store = Substitute.For<ITenantConfigurationStore>();
        store.GetTenantConfigurationAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<TenantConfiguration?>(tenants.SingleOrDefault(t => t.TenantId == call.Arg<int>())));
        store.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TenantConfiguration>>(tenants));
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(mediator);
        builder.Services.AddSingleton(store);
        _app = builder.Build();
    }

    [Theory]
    [InlineData(true, "GET", "admin/packages", "ListPackages")]
    [InlineData(false, "GET", "admin/packages", "ListPackages")]
    [InlineData(true, "POST", "admin/packages/load", "LoadPackage")]
    [InlineData(false, "POST", "admin/packages/load", "LoadPackage")]
    [InlineData(true, "DELETE", "admin/packages/example.ig/1.2.3", "UnloadPackage")]
    [InlineData(false, "DELETE", "admin/packages/example.ig/1.2.3", "UnloadPackage")]
    public async Task GivenCompetingFhirRoutes_WhenManagingPackages_ThenAdminEndpointWinsRegardlessOfRegistrationOrder(
        bool adminFirst, string method, string suffix, string endpointName)
    {
        var (context, body) = await SendAsync(adminFirst, method, $"/tenant/1/{suffix}");

        context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName.ShouldBe(endpointName);
        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        _requests.Count.ShouldBe(1);
        if (_requests[0] is ListPackagesQuery list)
        {
            list.TenantId.ShouldBe("1");
            body["count"]!.GetValue<int>().ShouldBe(1);
            body["packages"]![0]!["packageId"]!.GetValue<string>().ShouldBe("example.ig");
        }
        else if (_requests[0] is LoadPackageCommand load)
        {
            load.TenantId.ShouldBe("1");
            load.IncludeDependencies.ShouldBeTrue();
            body["packageId"]!.GetValue<string>().ShouldBe("example.ig");
            body["importedResources"]!.GetValue<int>().ShouldBe(3);
        }
        else
        {
            var unload = _requests[0].ShouldBeOfType<UnloadPackageCommand>();
            unload.TenantId.ShouldBe("1");
            unload.PackageId.ShouldBe("example.ig");
            unload.Version.ShouldBe("1.2.3");
            body["resourcesDeactivated"]!.GetValue<int>().ShouldBe(3);
        }
    }

    [Theory]
    [InlineData("GET", "admin/packages")]
    [InlineData("POST", "admin/packages/load")]
    [InlineData("DELETE", "admin/packages/example.ig/1.2.3")]
    public async Task GivenNonIntegerTenant_WhenManagingPackages_ThenNoAdminEndpointMatches(string method, string suffix)
    {
        var (context, _) = await SendAsync(true, method, $"/tenant/not-an-integer/{suffix}");

        context.GetEndpoint().ShouldBeNull();
        _requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("GET", "admin/packages")]
    [InlineData("POST", "admin/packages/load")]
    [InlineData("DELETE", "admin/packages/example.ig/1.2.3")]
    public async Task GivenSystemTenant_WhenManagingPackages_ThenTenantMiddlewareRejectsBeforeDispatch(string method, string suffix)
    {
        var (context, body) = await SendAsync(true, method, $"/tenant/0/{suffix}");

        context.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        body["issue"]![0]!["diagnostics"]!.GetValue<string>().ShouldContain("reserved for system operations");
        _requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("GET", "admin/packages")]
    [InlineData("POST", "admin/packages/load")]
    [InlineData("DELETE", "admin/packages/example.ig/1.2.3")]
    public async Task GivenUnknownTenant_WhenManagingPackages_ThenTenantMiddlewareRejectsBeforeDispatch(string method, string suffix)
    {
        var (context, _) = await SendAsync(false, method, $"/tenant/99/{suffix}");

        context.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        _requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenOrdinaryResourceRead_WhenRouting_ThenGenericFhirRouteRemainsSelected()
    {
        var (context, body) = await SendAsync(true, "GET", "/tenant/1/Patient/admin");

        context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName.ShouldBe("GenericFhirRead");
        body["resource"]!.GetValue<string>().ShouldBe("Patient/admin");
        _requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("SP_MIXED_BASE_SHADOW", StatusCodes.Status422UnprocessableEntity, "business-rule")]
    [InlineData("CONFORMANCE_CONFLICT", StatusCodes.Status409Conflict, "conflict")]
    public async Task GivenActivationRejectsTheStoredPackage_WhenLoading_ThenItReturnsAnOperationOutcomeNamingTheIssue(
        string issueCode, int expectedStatus, string expectedIssueType)
    {
        var mediator = _app.Services.GetRequiredService<IMediator>();
        mediator.SendAsync(Arg.Any<LoadPackageCommand>(), Arg.Any<CancellationToken>())
            .Returns<LoadPackageResult>(_ => throw new PackageActivationRejectedException(
                "example.ig",
                "1.2.3",
                [new ValidationIssue(issueCode, "SearchParameter 'identifier' resolves to different storage roots.")]));

        var (context, body) = await SendAsync(true, "POST", "/tenant/1/admin/packages/load");

        context.Response.StatusCode.ShouldBe(expectedStatus);
        context.Response.ContentType.ShouldBe("application/fhir+json");
        body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        var issue = body["issue"]!.AsArray().ShouldHaveSingleItem()!;
        issue["severity"]!.GetValue<string>().ShouldBe("error");
        issue["code"]!.GetValue<string>().ShouldBe(expectedIssueType);
        issue["details"]!["coding"]![0]!["system"]!.GetValue<string>()
            .ShouldBe(PackageActivationRejectedException.IssueCodeSystem);
        issue["details"]!["coding"]![0]!["code"]!.GetValue<string>().ShouldBe(issueCode);
        issue["details"]!["text"]!.GetValue<string>().ShouldBe("Package example.ig@1.2.3 was stored but not activated.");
        issue["diagnostics"]!.GetValue<string>().ShouldBe("SearchParameter 'identifier' resolves to different storage roots.");
    }

    [Fact]
    public async Task GivenDurableActivationWithWarnings_WhenLoading_ThenItReturnsOkWithTheIssues()
    {
        var mediator = _app.Services.GetRequiredService<IMediator>();
        mediator.SendAsync(Arg.Any<LoadPackageCommand>(), Arg.Any<CancellationToken>())
            .Returns(new LoadPackageResult
            {
                PackageId = "example.ig",
                PackageVersion = "1.2.3",
                Issues = [ValidationIssue.Warning("SP_TRANSITION_PENDING", "1 search parameter code(s) are hidden: Patient.identifier.")],
            });

        var (context, body) = await SendAsync(true, "POST", "/tenant/1/admin/packages/load");

        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        var issue = body["issues"]!.AsArray().ShouldHaveSingleItem()!;
        issue["severity"]!.GetValue<string>().ShouldBe("warning");
        issue["code"]!.GetValue<string>().ShouldBe("SP_TRANSITION_PENDING");
        issue["message"]!.GetValue<string>().ShouldContain("Patient.identifier");
    }

    private async Task<(HttpContext Context, JsonNode Body)> SendAsync(bool adminFirst, string method, string path)
    {
        var pipeline = new ApplicationBuilder(_app.Services);
        pipeline.UseRouting();
        pipeline.UseMiddleware<TenantResolutionMiddleware>();
        pipeline.UseEndpoints(endpoints =>
        {
            if (adminFirst)
            {
                endpoints.MapAdminPackageEndpoints();
            }
            // These are the generic FhirEndpoints route shapes. Sentinel delegates isolate endpoint
            // selection from unrelated resource authorization/storage, while the real admin handlers run.
            endpoints.MapGet("/tenant/{tenantId:int}/{resourceType}/{id}",
                    (string resourceType, string id) => Results.NotFound(new { resource = $"{resourceType}/{id}" }))
                .WithName("GenericFhirRead");
            endpoints.MapDelete("/tenant/{tenantId:int}/{resourceType}/{id}", () => Results.NotFound())
                .WithName("GenericFhirDelete");
            endpoints.MapPost("/tenant/{tenantId:int}/{resourceType}", () => Results.NotFound())
                .WithName("GenericFhirCreate");
            if (!adminFirst)
            {
                endpoints.MapAdminPackageEndpoints();
            }
        });
        await using var scope = _app.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        if (method == "POST")
        {
            byte[] payload = Encoding.UTF8.GetBytes("""{"packageId":"example.ig","version":"1.2.3","includeDependencies":true}""");
            context.Request.Body = new MemoryStream(payload);
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = payload.Length;
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature());
        }
        context.Response.Body = new MemoryStream();
        await pipeline.Build()(context);
        context.Response.Body.Position = 0;
        return (context, context.Response.Body.Length == 0
            ? new JsonObject()
            : (await JsonNode.ParseAsync(context.Response.Body))!);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private sealed class RequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
