using System.Text;
using System.Text.Json.Nodes;
using System.Security.Claims;
using Ignixa.Abstractions;
using Ignixa.Api.Endpoints;
using Ignixa.Api.Middleware;
using Ignixa.Application.Tests.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Authorization;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Features.Authorization.Services;
using Ignixa.Application.Features.Conformance;
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

namespace Ignixa.Api.Tests.Endpoints;

public sealed class ReindexEndpointsTests : IAsyncLifetime
{
    private readonly WebApplication _app;
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ReindexOptions _reindexOptions = new();
    private CompositeRepositoryFactory _repositoryFactory = ReindexTestHelper.CreateRepositoryFactory();
    private readonly List<CreateReindexJobCommand> _createCommands = [];
    private CreateReindexJobResult _createResult = new ReindexJobCreatedResult("created-job");
    private ReindexStatusResult _status = CreateStatus();
    private IReadOnlyList<ReindexStatusResult> _jobs = [];
    private CancelReindexResult _cancelResult = new ReindexCancelledResult("created-job");

    public static TheoryData<string> OperationalRoutes =>
        ["CreateReindexForTenant", "ListReindexForTenant", "GetReindexForTenant", "CancelReindexForTenant", "CreateReindex", "ListReindex", "GetReindex", "CancelReindex"];

    public ReindexEndpointsTests()
    {
        _mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _createCommands.Add(call.Arg<CreateReindexJobCommand>());
                return Task.FromResult(_createResult);
            });
        _mediator.SendAsync(Arg.Any<GetReindexStatusQuery>(), Arg.Any<CancellationToken>()).Returns(_ => (ReindexStatusResult?)_status);
        _mediator.SendAsync(Arg.Any<GetReindexJobsQuery>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(_jobs));
        _mediator.SendAsync(Arg.Any<CancelReindexCommand>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(_cancelResult));

        _app = BuildApp(Substitute.For<IFhirAuthorizationService>(), authorizationEnabled: false);
    }

    // §6.1: kickoff returns 201, the job Parameters and Content-Location.
    [Fact]
    public async Task GivenCreatedJob_WhenCreatingForTenant_ThenReturnsParametersAndContentLocation()
    {
        _status = CreateStatus("created-job");

        var response = await SendAsync("CreateReindexForTenant", body: Parameters(("maximumConcurrency", "valueInteger", 2)));

        response.StatusCode.ShouldBe(StatusCodes.Status201Created);
        response.Headers["Content-Location"].ShouldBe("/tenant/1/$reindex/created-job");
        response.ResourceType.ShouldBe("Parameters");
        Describe(response.Body).ShouldContain("id[valueString]=\"created-job\"");
        // The request parser's own tests pin every parameter; this pins that the parsed command is dispatched.
        _createCommands.ShouldHaveSingleItem().MaximumConcurrency.ShouldBe(2);
    }

    // §10.2 Reindex:Enabled: a disabled server answers the operation with 404.
    [Fact]
    public async Task GivenDisabledReindex_WhenMappingEndpoints_ThenOperationalRoutesRemainMappedAndAnswerNotFound()
    {
        const string operationEndpointName = "CreateReindex";
        _reindexOptions.Enabled = false;

        var endpointNames = ((IEndpointRouteBuilder)_app).DataSources
            .SelectMany(source => source.Endpoints)
            .Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .ToArray();

        endpointNames.ShouldContain(operationEndpointName);
        endpointNames.ShouldContain("GetReindexOperationDefinitionForTenant");

        var response = await SendAsync(operationEndpointName, body: Parameters());

        (response.StatusCode, response.ResourceType).ShouldBe((StatusCodes.Status404NotFound, "OperationOutcome"));
        response.Diagnostics.ShouldBe("The $reindex operation is disabled on this server.");
        _createCommands.ShouldBeEmpty();
    }

    // §6.1: the Parameters body is optional.
    [Fact]
    public async Task GivenEmptyChunkedRequestBody_WhenCreating_ThenUsesDefaultParameters()
    {
        var response = await SendAsync("CreateReindexForTenant", body: string.Empty, setContentLength: false);

        response.StatusCode.ShouldBe(StatusCodes.Status201Created);
        _createCommands.ShouldHaveSingleItem();
    }

    // §6.1 (F12): a rejected parameter returns 400 before any job is created.
    [Fact]
    public async Task GivenUnparseableParameters_WhenCreating_ThenReturnsTheParseErrorWithoutDispatching()
    {
        var response = await SendAsync("CreateReindex", body: Parameters(("targetResourceTypes", "valueString", "Patient")));

        (response.StatusCode, response.ResourceType).ShouldBe((StatusCodes.Status400BadRequest, "OperationOutcome"));
        response.Diagnostics.ShouldBe("Parameter 'targetResourceTypes' is not supported.");
        _createCommands.ShouldBeEmpty();
    }

    // §6.3: counts and identifiers use valueInteger64 on R5, valueDecimal/valueString before.
    [Theory]
    [InlineData("3.0", "valueDecimal", "valueString")]
    [InlineData("4.0", "valueDecimal", "valueString")]
    [InlineData("4.3", "valueDecimal", "valueString")]
    [InlineData("5.0", "valueInteger64", "valueInteger64")]
    public async Task GivenValuesAboveInt32Max_WhenGettingStatus_ThenUsesVersionAppropriateFhirTypes(
        string fhirVersion,
        string countValueName,
        string identifierValueName)
    {
        const long largeValue = (long)int.MaxValue + 1;
        _status = CreateStatus() with
        {
            Progress = new ReindexProgress(ReindexPhase.Reindexing)
            {
                Tenants = [new ReindexTenantProgress(1, ReindexTenantStatus.Reindexing, largeValue, largeValue, largeValue, largeValue, largeValue, largeValue, null)]
            },
            Definition = CreateDefinition(targetEventId: largeValue)
        };

        var response = await SendAsync("GetReindexForTenant", fhirVersion: fhirVersion);

        response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        var lines = Describe(response.Body);
        string[] counts = ["totalResourcesToReindex", "resourcesSuccessfullyReindexed", "conflicts", "tenant.resourcesToReindex",
            "tenant.resourcesReindexed", "tenant.conflicts", "tenant.failedResources"];
        string[] identifiers = ["targetEventId", "tenant.cutoffTransactionId", "tenant.cutoffSurrogateId"];
        counts.ShouldAllBe(name => lines.Any(line => line.StartsWith($"{name}[{countValueName}]=", StringComparison.Ordinal)));
        identifiers.ShouldAllBe(name => lines.Any(line => line.StartsWith($"{name}[{identifierValueName}]=", StringComparison.Ordinal)));
    }

    // §6.3: the job Parameters body.
    [Fact]
    public async Task GivenStatusWithAllFields_WhenGettingStatus_ThenMapsFhirParameters()
    {
        _status = CreateStatus() with { IsStale = true };

        var response = await SendAsync("GetReindexForTenant", jobId: "complete-fields");

        (response.StatusCode, response.ResourceType).ShouldBe((StatusCodes.Status200OK, "Parameters"));
        string.Join('\n', Describe(response.Body)).ShouldBe("""
            id[valueString]="complete-fields"
            status[valueString]="Running"
            queuedTime[valueDateTime]="2026-10-07T00:00:00+00:00"
            startTime[valueDateTime]="2026-10-07T00:01:00+00:00"
            endTime[valueDateTime]="2026-10-07T00:03:00+00:00"
            lastModified[valueDateTime]="2026-10-07T00:02:00+00:00"
            stale[valueBoolean]=true
            totalResourcesToReindex[valueDecimal]=10
            resourcesSuccessfullyReindexed[valueDecimal]=7
            progress[valueDecimal]=70
            phase[valueString]="Reindexing"
            cancellationReason[valueString]="Operator request"
            conflicts[valueDecimal]=2
            failureDetails[valueString]="failure detail"
            maximumNumberOfResourcesPerQuery[valueInteger]=50
            maximumNumberOfResourcesPerWrite[valueInteger]=25
            maximumConcurrency[valueInteger]=2
            queryDelayIntervalInMilliseconds[valueInteger]=5
            trigger[valueString]="Manual"
            targetEventId[valueString]="42"
            resources[valueString]="Patient"
            resources[valueString]="Observation"
            searchParams[valueString]="http://example.test/SearchParameter/name"
            ignoredLifecycleEvents[valueString]="http://example.test/SearchParameter/ignored"
            tenant.tenantId[valueInteger]=1
            tenant.cutoffTransactionId[valueString]="10"
            tenant.cutoffSurrogateId[valueString]="20"
            tenant.status[valueString]="Reindexing"
            tenant.resourcesToReindex[valueDecimal]=5
            tenant.resourcesReindexed[valueDecimal]=3
            tenant.conflicts[valueDecimal]=1
            tenant.failedResources[valueDecimal]=1
            tenant.tenantId[valueInteger]=2
            tenant.cutoffTransactionId[valueString]="11"
            tenant.cutoffSurrogateId[valueString]="21"
            tenant.status[valueString]="Reindexing"
            tenant.resourcesToReindex[valueDecimal]=5
            tenant.resourcesReindexed[valueDecimal]=4
            tenant.conflicts[valueDecimal]=1
            tenant.failedResources[valueDecimal]=1
            failedResource.resourceType[valueString]="Patient"
            failedResource.id[valueString]="first"
            failedResource.reason[valueString]="first failure"
            failedResource.resourceType[valueString]="Observation"
            failedResource.id[valueString]="second"
            failedResource.reason[valueString]="second failure"
            """.ReplaceLineEndings("\n"));
    }

    // §6.1, §6.2, §6.4: each handler outcome maps to its status code.
    [Theory]
    [InlineData("CreateReindexForTenant", "active", 409, "OperationOutcome", "/tenant/1/$reindex/active-job")]
    [InlineData("CreateReindexForTenant", "no-work", 400, "OperationOutcome", null)]
    [InlineData("CreateReindexForTenant", "invalid", 400, "OperationOutcome", null)]
    [InlineData("GetReindexForTenant", "missing", 404, "OperationOutcome", null)]
    [InlineData("CancelReindexForTenant", "missing", 404, "OperationOutcome", null)]
    [InlineData("CancelReindexForTenant", "cancelled", 202, "Parameters", null)]
    [InlineData("CancelReindexForTenant", "terminal", 409, "OperationOutcome", null)]
    public async Task GivenAHandlerOutcome_WhenRouted_ThenItMapsToItsStatusAndResource(
        string endpointName,
        string outcome,
        int expectedStatus,
        string expectedResourceType,
        string? expectedContentLocation)
    {
        _createResult = outcome switch
        {
            "active" => new ActiveReindexJobResult("active-job"),
            "no-work" => new NoReindexWorkResult("No resources need reindexing."),
            "invalid" => new InvalidReindexRequestResult("maximumConcurrency must be between 1 and 16."),
            _ => _createResult
        };
        _status = outcome == "missing" ? null! : _status;
        _cancelResult = outcome switch
        {
            "missing" => new ReindexJobNotFoundResult("missing"),
            "terminal" => new ReindexJobAlreadyTerminalResult("job", ReindexJobStatus.Completed),
            _ => new ReindexCancelledResult("job")
        };

        var response = await SendAsync(endpointName, body: Parameters());

        (response.StatusCode, response.ResourceType).ShouldBe((expectedStatus, expectedResourceType));
        response.Headers.GetValueOrDefault("Content-Location").ShouldBe(expectedContentLocation);
        if (outcome == "invalid")
        {
            response.Diagnostics.ShouldBe("maximumConcurrency must be between 1 and 16.");
        }

        if (outcome == "cancelled")
        {
            Describe(response.Body).ShouldContain("status[valueString]=\"Cancelled\"");
        }
    }

    // §6.1: any Prefer other than respond-async returns 400.
    [Fact]
    public async Task GivenInvalidPreferHeader_WhenCreating_ThenReturnsBadRequestWithoutDispatching()
    {
        var response = await SendAsync("CreateReindexForTenant", body: Parameters(), prefer: "return=representation");

        response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        _createCommands.ShouldBeEmpty();
    }

    // §6.6: a tenant provider that cannot reindex makes the routes return 501.
    [Theory]
    [MemberData(nameof(OperationalRoutes))]
    public async Task GivenMixedProviderServer_WhenUsingOperationalRoute_ThenReturnsNotImplementedOutcome(string operationEndpointName)
    {
        _repositoryFactory = ReindexTestHelper.CreateRepositoryFactory(ReindexTestHelper.Tenant(1, "SqlServer"), ReindexTestHelper.Tenant(2, "FileSystem"));

        var response = await SendAsync(operationEndpointName, body: Parameters());

        (response.StatusCode, response.ResourceType).ShouldBe((StatusCodes.Status501NotImplemented, "OperationOutcome"));
        response.Diagnostics.ShouldBe("The $reindex operation is not supported by all active tenant providers.");
    }

    [Theory]
    [InlineData("POST", "/tenant/0/$reindex")]
    [InlineData("GET", "/tenant/0/$reindex")]
    [InlineData("GET", "/tenant/0/$reindex/job")]
    [InlineData("DELETE", "/tenant/0/$reindex/job")]
    public async Task GivenSystemPartitionRoute_WhenRoutedThroughTenantResolution_ThenItIsRejectedBeforeDispatch(
        string method,
        string path)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(_mediator);
        builder.Services.AddSingleton(Substitute.For<ITenantConfigurationStore>());
        await using var app = builder.Build();
        var pipeline = new ApplicationBuilder(app.Services);
        pipeline.UseRouting();
        pipeline.UseMiddleware<TenantResolutionMiddleware>();
        pipeline.UseEndpoints(endpoints => endpoints.MapReindexEndpoints());
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        await pipeline.Build()(context);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        context.Response.Body.Position = 0;
        (await JsonNode.ParseAsync(context.Response.Body))!["issue"]![0]!["diagnostics"]!.GetValue<string>()
            .ShouldContain("reserved for system operations");
        _mediator.ReceivedCalls().ShouldBeEmpty();
    }

    // §6.7: every operational route authorizes as a write on every resource type.
    [Theory]
    [MemberData(nameof(OperationalRoutes))]
    public async Task GivenDeniedAuthorization_WhenAccessingReindexRoute_ThenEndpointReturnsForbiddenWithoutDispatching(string endpointName)
    {
        var authorization = Substitute.For<IFhirAuthorizationService>();
        FhirAuthorizationContext? captured = null;
        authorization.AuthorizeAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<FhirAuthorizationContext>();
                return AuthorizationResult.Denied("Administrative access is required.");
            });

        await using var app = BuildApp(authorization, authorizationEnabled: true);

        var response = await SendAsync(endpointName, body: Parameters(), app: app);

        (response.StatusCode, response.ResourceType).ShouldBe((StatusCodes.Status403Forbidden, "OperationOutcome"));
        (captured.ShouldNotBeNull().Interaction, captured.ResourceType).ShouldBe((FhirInteraction.Update, "*"));
        _createCommands.ShouldBeEmpty();
    }

    // §6.7: a wildcard write scope runs the operation; a read scope is enough for its OperationDefinition.
    [Theory]
    [InlineData("CreateReindexForTenant", "system/*.write", StatusCodes.Status201Created)]
    [InlineData("CreateReindex", "system/*.write", StatusCodes.Status201Created)]
    [InlineData("GetReindexOperationDefinitionForTenant", "system/*.read", StatusCodes.Status200OK)]
    [InlineData("GetReindexOperationDefinition", "system/*.read", StatusCodes.Status200OK)]
    public async Task GivenTheSmartScopeTheRouteRequires_WhenAccessingIt_ThenRequestIsAllowed(string endpointName, string scope, int expectedStatus)
    {
        var permissions = Substitute.For<IRolePermissionStore>();
        permissions.GetPermissionsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns([]);
        await using var app = BuildApp(
            new FhirAuthorizationService(
                [
                    new RbacAuthorizationHandler(permissions, NullLogger<RbacAuthorizationHandler>.Instance),
                    new SmartScopeAuthorizationHandler(NullLogger<SmartScopeAuthorizationHandler>.Instance)
                ],
                NullLogger<FhirAuthorizationService>.Instance),
            authorizationEnabled: true);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(FhirClaimTypes.Subject, "user"), new Claim(FhirClaimTypes.Scope, scope)], "TestAuth"));

        var response = await SendAsync(endpointName, body: expectedStatus == StatusCodes.Status201Created ? Parameters() : null, app: app, user: user);

        response.StatusCode.ShouldBe(expectedStatus);
    }

    // §6.1: OperationDefinition/reindex lists exactly the accepted parameters.
    [Fact]
    public async Task GivenOperationDefinitionRequest_WhenGettingDefinition_ThenListsAcceptedParameters()
    {
        var response = await SendAsync("GetReindexOperationDefinitionForTenant");

        (response.StatusCode, response.ResourceType).ShouldBe((StatusCodes.Status200OK, "OperationDefinition"));
        response.Body["parameter"]!.AsArray().Select(parameter => parameter!["name"]!.GetValue<string>()).ShouldBe(
            ["maximumNumberOfResourcesPerQuery", "maximumNumberOfResourcesPerWrite", "maximumConcurrency", "queryDelayIntervalInMilliseconds"]);
    }

    private WebApplication BuildApp(IFhirAuthorizationService authorization, bool authorizationEnabled)
    {
        var requestContext = Substitute.For<IFhirRequestContextAccessor>();
        requestContext.RequestContext.Returns(Substitute.For<IFhirRequestContext>());
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(_mediator);
        builder.Services.AddSingleton(Options.Create(_reindexOptions));
        builder.Services.AddSingleton(_ => _repositoryFactory);
        builder.Services.AddSingleton(authorization);
        builder.Services.AddSingleton(requestContext);
        builder.Services.AddSingleton(Substitute.For<IAuditLogger>());
        builder.Services.AddSingleton(Substitute.For<IMetricsService>());
        builder.Services.AddSingleton<IOptions<AuthorizationOptions>>(Options.Create(new AuthorizationOptions { Enabled = authorizationEnabled }));
        var app = builder.Build();
        app.MapReindexEndpoints();
        return app;
    }

    // Invokes the named endpoint for tenant 1 on the class's app unless another is given.
    private async Task<Response> SendAsync(
        string endpointName,
        string jobId = "job",
        string? body = null,
        string? prefer = null,
        bool setContentLength = true,
        string fhirVersion = "4.0",
        WebApplication? app = null,
        ClaimsPrincipal? user = null)
    {
        const int tenantId = 1;
        app ??= _app;
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Single(candidate => candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == endpointName);
        await using var scope = app.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.SetEndpoint(endpoint);
        context.Items["TenantId"] = tenantId;
        context.Items["TenantConfiguration"] = new TenantConfiguration { TenantId = tenantId, DisplayName = "Test tenant", FhirVersion = fhirVersion };
        context.Request.RouteValues["tenantId"] = "1";
        context.Request.RouteValues["jobId"] = jobId;
        context.User = user ?? new ClaimsPrincipal();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = endpointName.Contains("OperationDefinition", StringComparison.Ordinal)
            ? $"/tenant/{tenantId}/OperationDefinition/reindex"
            : $"/tenant/{tenantId}/$reindex" + (endpointName.Contains("ForTenant", StringComparison.Ordinal) &&
                endpointName is "GetReindexForTenant" or "CancelReindexForTenant" ? $"/{jobId}" : string.Empty);
        context.Request.Method = endpointName.StartsWith("Create", StringComparison.Ordinal) ? HttpMethods.Post :
            endpointName.StartsWith("Cancel", StringComparison.Ordinal) ? HttpMethods.Delete : HttpMethods.Get;
        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Request.Body = new MemoryStream(bytes);
            if (setContentLength)
            {
                context.Request.ContentLength = bytes.Length;
            }
            context.Request.ContentType = "application/fhir+json";
        }
        if (prefer is not null)
        {
            context.Request.Headers["Prefer"] = prefer;
        }
        context.Response.Body = new MemoryStream();

        await endpoint.RequestDelegate!(context);

        context.Response.Body.Position = 0;
        return new Response(
            context.Response.StatusCode,
            context.Response.Body.Length == 0 ? new JsonObject() : (await JsonNode.ParseAsync(context.Response.Body))!,
            context.Response.Headers.ToDictionary(pair => pair.Key, pair => pair.Value.ToString()));
    }

    private static string Parameters(params (string Name, string ValueName, object Value)[] parameters) =>
        new JsonObject
        {
            ["resourceType"] = "Parameters",
            ["parameter"] = new JsonArray([.. parameters.Select(parameter => new JsonObject { ["name"] = parameter.Name, [parameter.ValueName] = JsonValue.Create(parameter.Value) })])
        }.ToJsonString();

    private static readonly System.Text.Json.JsonSerializerOptions RelaxedJson =
        new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Each Parameters value as <c>name[valueType]=json</c>, with part names joined by dots, in document order.</summary>
    private static List<string> Describe(JsonNode parameters) => Describe(parameters["parameter"]!.AsArray(), "").ToList();

    private static IEnumerable<string> Describe(JsonArray parameters, string prefix) =>
        parameters.SelectMany(parameter =>
        {
            var name = prefix + parameter!["name"]!.GetValue<string>();
            return parameter["part"] is JsonArray parts
                ? Describe(parts, name + ".")
                : parameter.AsObject()
                    .Where(property => property.Key.StartsWith("value", StringComparison.Ordinal))
                    .Select(property => $"{name}[{property.Key}]={property.Value!.ToJsonString(RelaxedJson)}");
        });

    private static ReindexStatusResult CreateStatus(string jobId = "complete-fields") =>
        new(
            jobId,
            ReindexJobStatus.Running,
            QueuedTime: Minute(0),
            StartTime: Minute(1),
            EndTime: Minute(3),
            LastModified: Minute(2),
            IsStale: false,
            "failure detail",
            new ReindexProgress(ReindexPhase.Reindexing)
            {
                CancellationReason = "Operator request",
                IgnoredLifecycleEvents = ["http://example.test/SearchParameter/ignored"],
                Tenants =
                [
                    new ReindexTenantProgress(1, ReindexTenantStatus.Reindexing, 10, 20, 5, 3, 1, 1, null) { FailedResources = [new("Patient", "first", "first failure")] },
                    new ReindexTenantProgress(2, ReindexTenantStatus.Reindexing, 11, 21, 5, 4, 1, 1, null) { FailedResources = [new("Observation", "second", "second failure")] }
                ]
            },
            CreateDefinition());

    private static DateTimeOffset Minute(int minute) => new(2026, 10, 7, 0, minute, 0, TimeSpan.Zero);

    private static ReindexJobDefinition CreateDefinition(long targetEventId = 42) => new()
    {
        TargetEventId = targetEventId,
        TenantIds = [1, 2],
        ResourceTypes = ["Patient", "Observation"],
        SearchParameters = [new ReindexParameterDefinition("http://example.test/SearchParameter/name", "name", "Patient", 1, 42, ["Patient"])],
        MaximumNumberOfResourcesPerQuery = 50,
        MaximumNumberOfResourcesPerWrite = 25,
        MaximumConcurrency = 2,
        QueryDelayIntervalInMilliseconds = 5,
        Trigger = "Manual"
    };

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private sealed record Response(int StatusCode, JsonNode Body, IReadOnlyDictionary<string, string> Headers)
    {
        public string ResourceType => Body["resourceType"]!.GetValue<string>();

        public string Diagnostics => Body["issue"]![0]!["diagnostics"]!.GetValue<string>();
    }
}
