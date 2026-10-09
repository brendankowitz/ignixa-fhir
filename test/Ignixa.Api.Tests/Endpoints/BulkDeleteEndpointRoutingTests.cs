using Ignixa.Api.Endpoints;
using Medino;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Endpoints;

/// <summary>
/// Confirms that the bulk-delete routes' literal segments (<c>$bulk-delete</c>, <c>_operations</c>) beat
/// the generic FHIR routes' parameter segments (<c>{resourceType}</c>, <c>{id}</c>) regardless of which
/// extension is mapped first, for both the tenant-explicit and tenant-agnostic route forms, and that the
/// generic routes -- including conditional delete, which collides on the exact same segment count as the
/// system-level kickoff -- still win for ordinary requests. Endpoint selection happens inside
/// <c>UseRouting()</c>, before the matched endpoint's filters/handler run; this test asserts only on
/// <see cref="HttpContext.GetEndpoint"/> and swallows whatever the (intentionally bare) DI container
/// cannot satisfy once execution reaches those filters, since that is downstream of routing and not what
/// this test is about.
/// </summary>
public sealed class BulkDeleteEndpointRoutingTests : IAsyncLifetime
{
    private readonly WebApplication _app;

    public BulkDeleteEndpointRoutingTests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(Substitute.For<IMediator>());
        _app = builder.Build();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenTenantSystemLevelBulkDeleteKickoff_WhenRoutedAgainstConditionalDelete_ThenBulkDeleteEndpointWins(
        bool bulkDeleteRegisteredFirst)
    {
        var context = await SendAsync(bulkDeleteRegisteredFirst, "DELETE", "/tenant/1/$bulk-delete");

        context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            .ShouldBe("StartBulkDeleteSystemLevel");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenAgnosticSystemLevelBulkDeleteKickoff_WhenRoutedAgainstConditionalDelete_ThenBulkDeleteEndpointWins(
        bool bulkDeleteRegisteredFirst)
    {
        var context = await SendAsync(bulkDeleteRegisteredFirst, "DELETE", "/$bulk-delete");

        context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            .ShouldBe("StartBulkDeleteSystemLevelAgnostic");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenTenantTypeLevelBulkDeleteKickoff_WhenRoutedAgainstGenericDeleteById_ThenBulkDeleteEndpointWins(
        bool bulkDeleteRegisteredFirst)
    {
        var context = await SendAsync(bulkDeleteRegisteredFirst, "DELETE", "/tenant/1/Patient/$bulk-delete");

        context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            .ShouldBe("StartBulkDeleteTypeLevel");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenAgnosticTypeLevelBulkDeleteKickoff_WhenRoutedAgainstGenericDeleteById_ThenBulkDeleteEndpointWins(
        bool bulkDeleteRegisteredFirst)
    {
        var context = await SendAsync(bulkDeleteRegisteredFirst, "DELETE", "/Patient/$bulk-delete");

        context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            .ShouldBe("StartBulkDeleteTypeLevelAgnostic");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenBulkDeleteStatusRoute_WhenRouted_ThenStatusEndpointIsSelected(bool bulkDeleteRegisteredFirst)
    {
        var context = await SendAsync(bulkDeleteRegisteredFirst, "GET", "/tenant/1/_operations/bulk-delete/job-1");

        context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            .ShouldBe("GetBulkDeleteStatus");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenTenantConditionalDelete_WhenRoutedAgainstBulkDeleteEndpoints_ThenGenericConditionalDeleteWins(
        bool bulkDeleteRegisteredFirst)
    {
        var context = await SendAsync(bulkDeleteRegisteredFirst, "DELETE", "/tenant/1/Patient");

        context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            .ShouldBe("GenericConditionalDeleteExplicit");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenAgnosticConditionalDelete_WhenRoutedAgainstBulkDeleteEndpoints_ThenGenericConditionalDeleteWins(
        bool bulkDeleteRegisteredFirst)
    {
        var context = await SendAsync(bulkDeleteRegisteredFirst, "DELETE", "/Patient");

        context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            .ShouldBe("GenericConditionalDeleteAgnostic");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenTenantOrdinaryResourceDelete_WhenRoutedAgainstBulkDeleteEndpoints_ThenGenericEndpointStillWins(
        bool bulkDeleteRegisteredFirst)
    {
        var context = await SendAsync(bulkDeleteRegisteredFirst, "DELETE", "/tenant/1/Patient/abc123");

        context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            .ShouldBe("GenericFhirDelete");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenAgnosticOrdinaryResourceDelete_WhenRoutedAgainstBulkDeleteEndpoints_ThenGenericEndpointStillWins(
        bool bulkDeleteRegisteredFirst)
    {
        var context = await SendAsync(bulkDeleteRegisteredFirst, "DELETE", "/Patient/abc123");

        context.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName
            .ShouldBe("GenericFhirDeleteAgnostic");
    }

    private async Task<HttpContext> SendAsync(bool bulkDeleteRegisteredFirst, string method, string path)
    {
        var pipeline = new ApplicationBuilder(_app.Services);
        pipeline.UseRouting();
        pipeline.UseEndpoints(endpoints =>
        {
            if (bulkDeleteRegisteredFirst)
            {
                endpoints.MapBulkDeleteEndpoints();
            }

            // Sentinels for every generic FHIR route shape bulk-delete must not shadow or be shadowed by:
            // tenant-explicit and tenant-agnostic read-by-id, delete-by-id, and conditional delete (the
            // conditional delete route collides on the exact same segment count as the system-level kickoff).
            endpoints.MapGet("/tenant/{tenantId:int}/{resourceType}/{id}", () => Results.NotFound())
                .WithName("GenericFhirRead");
            endpoints.MapDelete("/tenant/{tenantId:int}/{resourceType}/{id}", () => Results.NotFound())
                .WithName("GenericFhirDelete");
            endpoints.MapDelete("/tenant/{tenantId:int}/{resourceType}", () => Results.NotFound())
                .WithName("GenericConditionalDeleteExplicit");
            endpoints.MapGet("/{resourceType}/{id}", () => Results.NotFound())
                .WithName("GenericFhirReadAgnostic");
            endpoints.MapDelete("/{resourceType}/{id}", () => Results.NotFound())
                .WithName("GenericFhirDeleteAgnostic");
            endpoints.MapDelete("/{resourceType}", () => Results.NotFound())
                .WithName("GenericConditionalDeleteAgnostic");

            if (!bulkDeleteRegisteredFirst)
            {
                endpoints.MapBulkDeleteEndpoints();
            }
        });

        await using var scope = _app.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Response.Body = new MemoryStream();

        try
        {
            await pipeline.Build()(context);
        }
        catch
        {
            // Routing has already recorded the matched endpoint by the time UseRouting() hands off to
            // its filters/handler; this test's bare DI container is not expected to satisfy those, and
            // whether it does is not what this test is about.
        }

        return context;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();
}
