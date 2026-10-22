using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.Endpoints;
using Ignixa.Api.Middleware;
using Ignixa.Application.Features.Admin;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.PackageManagement.Models;
using Medino;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Endpoints;

public sealed class AdminPackageEndpointLoadOutcomeTests : IAsyncLifetime
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly WebApplication _app;

    public AdminPackageEndpointLoadOutcomeTests()
    {
        TenantConfiguration tenant = new() { TenantId = 1, DisplayName = "One", FhirVersion = "4.0" };
        var store = Substitute.For<ITenantConfigurationStore>();
        store.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>(tenant));
        store.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TenantConfiguration>>([tenant]));
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(_mediator);
        builder.Services.AddSingleton(store);
        _app = builder.Build();
        _app.MapAdminPackageEndpoints();
    }

    [Fact]
    public async Task GivenIncompleteClosureWithSensitiveReasons_WhenLoadingPackage_ThenResponseReportsSafeStructuredOutcome()
    {
        _mediator.SendAsync(Arg.Any<LoadPackageCommand>(), Arg.Any<CancellationToken>()).Returns(new LoadPackageResult
        {
            PackageId = "example.ig",
            PackageVersion = "1.2.3",
            LoadedPackages = ["example.ig@1.2.3"],
            SkippedPackages = ["broken@2.0.0 (skipped: InvalidOperationException: token=do-not-return)"],
            ClosureMembers =
            [
                new("example.ig", "1.2.3", PackageClosureMemberStatus.Imported),
                new("broken", "2.0.0", PackageClosureMemberStatus.LoadFailed, "token=do-not-return"),
            ],
            Activation = ActivationResult.Failed([new ValidationIssue("conflict", "SearchParameter clash")]),
        });

        var (context, body) = await SendLoadAsync(CancellationToken.None);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        body["isComplete"]!.GetValue<bool>().ShouldBeFalse();
        body["skippedPackages"]![0]!.GetValue<string>().ShouldBe("broken@2.0.0 (skipped: package could not be loaded)");
        body["closureMembers"]![1]!["status"]!.GetValue<string>().ShouldBe("LoadFailed");
        body["closureMembers"]![1]!["reason"]!.GetValue<string>().ShouldBe("package could not be loaded");
        body["closureMembers"]![1]!["isBlocking"]!.GetValue<bool>().ShouldBeTrue();
        body["activation"]!["succeeded"]!.GetValue<bool>().ShouldBeFalse();
        body["activation"]!["issues"]![0]!["code"]!.GetValue<string>().ShouldBe("conflict");
        body.ToJsonString().ShouldNotContain("token=do-not-return");
    }

    [Fact]
    public void GivenLoadPackageEndpoint_WhenInspectingSuccessMetadata_ThenItDeclaresLoadPackageResponse()
    {
        IEndpointRouteBuilder endpointRouteBuilder = _app;
        var endpoint = endpointRouteBuilder.DataSources
            .SelectMany(dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "/tenant/{tenantId:int}/admin/packages/load");

        var responseMetadata = endpoint.Metadata
            .OfType<IProducesResponseTypeMetadata>()
            .Single(metadata => metadata.StatusCode == StatusCodes.Status200OK);

        responseMetadata.Type.ShouldBe(typeof(AdminPackageEndpoints.LoadPackageResponse));
    }

    [Fact]
    public async Task GivenCallerCancels_WhenLoadingPackage_ThenCancellationIsNotConvertedToServerError()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        _mediator.SendAsync(Arg.Any<LoadPackageCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));

        await Should.ThrowAsync<OperationCanceledException>(() => SendLoadAsync(cancellation.Token));
    }

    private async Task<(HttpContext Context, JsonNode Body)> SendLoadAsync(CancellationToken requestAborted)
    {
        var pipeline = new ApplicationBuilder(_app.Services);
        pipeline.UseRouting();
        pipeline.UseMiddleware<TenantResolutionMiddleware>();
        pipeline.UseEndpoints(endpoints => endpoints.MapAdminPackageEndpoints());
        await using var scope = _app.Services.CreateAsyncScope();
        byte[] payload = Encoding.UTF8.GetBytes("""{"packageId":"example.ig","version":"1.2.3","includeDependencies":true}""");
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = requestAborted };
        context.Request.Method = "POST";
        context.Request.Path = "/tenant/1/admin/packages/load";
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Body = new MemoryStream(payload);
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = payload.Length;
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new RequestBodyDetectionFeature());
        context.Response.Body = new MemoryStream();
        await pipeline.Build()(context);
        context.Response.Body.Position = 0;
        return (context, (await JsonNode.ParseAsync(context.Response.Body, cancellationToken: CancellationToken.None))!);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private sealed class RequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
