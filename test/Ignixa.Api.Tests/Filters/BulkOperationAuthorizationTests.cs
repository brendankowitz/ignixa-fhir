using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Api.Endpoints;
using Ignixa.Application.Features.Authorization;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Services;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Filters;

/// <summary>
/// Drives every bulk and server-wide route through the real SMART and RBAC handlers to prove each one refuses a
/// compartment-limited grant and, while several tenants are active, a caller without <c>SystemAdmin</c>.
/// </summary>
public sealed class BulkOperationAuthorizationTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ITenantConfigurationStore _tenants = Substitute.For<ITenantConfigurationStore>();

    public BulkOperationAuthorizationTests() => ActiveTenants(1);

    public static TheoryData<string> BulkRoutes =>
    [
        "StartExportSystemLevel", "StartExport", "StartGroupExport", "StartGroupExportForTenant", "GetExportStatus", "CancelExport",
        "StartImport", "GetImportStatus", "CancelImport",
        "GetBulkDeleteStatus", "CancelBulkDelete", "GetBulkDeleteStatusAgnostic", "CancelBulkDeleteAgnostic",
        "CreateReindexForTenant", "ListReindexForTenant", "GetReindexForTenant", "CancelReindexForTenant",
        "CreateReindex", "ListReindex", "GetReindex", "CancelReindex",
        "LoadPackage", "ListPackages", "UnloadPackage"
    ];

    public static TheoryData<string> ServerWideRoutes =>
    [
        "CreateReindexForTenant", "ListReindexForTenant", "GetReindexForTenant", "CancelReindexForTenant",
        "CreateReindex", "ListReindex", "GetReindex", "CancelReindex",
        "LoadPackage", "UnloadPackage"
    ];

    [Theory]
    [MemberData(nameof(BulkRoutes))]
    public async Task GivenAPatientScopedGrant_WhenCallingABulkRoute_ThenItIsForbiddenWithoutDispatching(string endpointName)
    {
        await using var app = BuildApp();

        var response = await SendAsync(app, endpointName, Caller("patient/*.cruds", patient: "p1"));

        response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        response.Diagnostics.ShouldContain("unrestricted grant");
        _mediator.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ServerWideRoutes))]
    public async Task GivenSeveralActiveTenantsAndNoSystemAdminRole_WhenCallingAServerWideRoute_ThenItIsForbiddenWithoutDispatching(
        string endpointName)
    {
        ActiveTenants(1, 2);
        await using var app = BuildApp();

        var response = await SendAsync(app, endpointName, Caller("system/*.cruds"));

        response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        response.Diagnostics.ShouldContain(TenantIsolationHandler.SystemAdminRole);
        _mediator.ReceivedCalls().ShouldBeEmpty();
    }

    // Reindex is disabled in this host, so reaching its availability filter (404) proves authorization passed.
    [Theory]
    [InlineData(new[] { 1, 2 }, TenantIsolationHandler.SystemAdminRole)]
    [InlineData(new[] { 1 }, null)]
    [InlineData(new[] { 0, 1 }, null)]
    public async Task GivenSystemAdminOrASingleActiveTenant_WhenCreatingAReindex_ThenAuthorizationPasses(int[] tenantIds, string? role)
    {
        ActiveTenants(tenantIds);
        await using var app = BuildApp();

        var response = await SendAsync(app, "CreateReindexForTenant", Caller("system/*.cruds", role: role));

        response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task GivenAnInactiveSecondTenant_WhenCreatingAReindexWithoutSystemAdmin_ThenAuthorizationPasses()
    {
        _tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>()).Returns(
            [Tenant(1), Tenant(2) with { IsActive = false }]);
        await using var app = BuildApp();

        var response = await SendAsync(app, "CreateReindexForTenant", Caller("system/*.cruds"));

        response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
    }

    private void ActiveTenants(params int[] tenantIds) =>
        _tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(tenantIds.Select(Tenant).ToArray());

    private static TenantConfiguration Tenant(int tenantId) =>
        new() { TenantId = tenantId, DisplayName = $"Tenant {tenantId}", FhirVersion = "4.0", IsActive = true };

    private static ClaimsPrincipal Caller(string scope, string? patient = null, string? role = null)
    {
        List<Claim> claims = [new(FhirClaimTypes.Subject, "caller"), new(FhirClaimTypes.Scope, scope)];
        if (patient is not null)
        {
            claims.Add(new Claim(FhirClaimTypes.Patient, patient));
        }

        if (role is not null)
        {
            claims.Add(new Claim(FhirClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private WebApplication BuildApp()
    {
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
        builder.Services.AddSingleton(Options.Create(new ReindexOptions { Enabled = false }));
        builder.Services.AddSingleton(Substitute.For<IAuditLogger>());
        builder.Services.AddSingleton(Substitute.For<IMetricsService>());
        builder.Services.AddSingleton(Substitute.For<IFhirVersionContext>());
        builder.Services.AddSingleton(new TaskHubClient(Substitute.For<IOrchestrationServiceClient>()));
        builder.Services.AddSingleton(Substitute.For<IBlobStorageClient>());
        builder.Services.AddSingleton(Substitute.For<IBackgroundJobRepository<ExportJobDefinition>>());
        builder.Services.AddSingleton(Substitute.For<IBackgroundJobRepository<ImportJobDefinition>>());
        var app = builder.Build();
        app.MapExportEndpoints();
        app.MapImportEndpoints();
        app.MapBulkDeleteEndpoints();
        app.MapReindexEndpoints();
        app.MapAdminPackageEndpoints();
        return app;
    }

    private static async Task<(int StatusCode, string Diagnostics)> SendAsync(
        WebApplication app,
        string endpointName,
        ClaimsPrincipal user)
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Single(candidate => candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == endpointName);
        await using var scope = app.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = user };
        context.SetEndpoint(endpoint);
        context.Items["TenantId"] = 1;
        foreach (var (name, value) in new[]
                 {
                     ("tenantId", "1"), ("jobId", "job"), ("groupId", "group"), ("packageId", "package"), ("version", "1.0.0")
                 })
        {
            context.Request.RouteValues[name] = value;
        }

        var route = ((RouteEndpoint)endpoint).RoutePattern.RawText!;
        context.Request.Path = route
            .Replace("{tenantId:int}", "1", StringComparison.Ordinal)
            .Replace("{jobId}", "job", StringComparison.Ordinal)
            .Replace("{groupId}", "group", StringComparison.Ordinal)
            .Replace("{packageId}", "package", StringComparison.Ordinal)
            .Replace("{version}", "1.0.0", StringComparison.Ordinal);
        context.Request.Method = endpoint.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods.Single();
        var body = Encoding.UTF8.GetBytes("""{"resourceType":"Parameters","packageId":"package","version":"1.0.0"}""");
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Request.ContentType = "application/json";
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        context.Response.Body.Position = 0;
        var diagnostics = context.Response.Body.Length == 0
            ? string.Empty
            : (await JsonNode.ParseAsync(context.Response.Body))?["issue"]?[0]?["diagnostics"]?.GetValue<string>() ?? string.Empty;
        return (context.Response.StatusCode, diagnostics);
    }
}
