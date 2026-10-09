using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Claims;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Api.Endpoints;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Activities;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Authorization;
using Ignixa.Application.Features.Authorization.Handlers;
using Ignixa.Application.Features.Authorization.Models;
using Ignixa.Application.Features.Authorization.Services;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Reindex;
using Ignixa.Application.Infrastructure;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Specification.ValueSets.Normative;
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
    private readonly IReindexAvailability _availability = Substitute.For<IReindexAvailability>();
    private readonly List<CreateReindexJobCommand> _createCommands = [];
    private CreateReindexJobResult _createResult = new ReindexJobCreatedResult("created-job");
    private ReindexStatusResult _status = CreateStatus();
    private IReadOnlyList<ReindexStatusResult> _jobs = [];
    private CancelReindexResult _cancelResult = new ReindexCancelledResult("created-job");

    public ReindexEndpointsTests()
    {
        _availability.GetAvailabilityAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ReindexAvailability.Available));
        _mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _createCommands.Add(call.Arg<CreateReindexJobCommand>());
                return Task.FromResult(_createResult);
            });
        _mediator.SendAsync(Arg.Any<GetReindexStatusQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => (ReindexStatusResult?)_status);
        _mediator.SendAsync(Arg.Any<GetReindexJobsQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(_jobs));
        _mediator.SendAsync(Arg.Any<CancelReindexCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(_cancelResult));

        var authorization = Substitute.For<IFhirAuthorizationService>();
        var requestContext = Substitute.For<IFhirRequestContextAccessor>();
        var audit = Substitute.For<IAuditLogger>();
        var metrics = Substitute.For<IMetricsService>();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(_mediator);
        builder.Services.AddSingleton(_availability);
        builder.Services.AddSingleton(authorization);
        builder.Services.AddSingleton(requestContext);
        builder.Services.AddSingleton(audit);
        builder.Services.AddSingleton(metrics);
        builder.Services.AddSingleton<IOptions<AuthorizationOptions>>(
            Options.Create(new AuthorizationOptions { Enabled = false }));
        _app = builder.Build();
        _app.MapReindexEndpoints();
    }

    [Fact]
    public async Task GivenCreatedJob_WhenCreatingForTenant_ThenReturnsParametersAndContentLocation()
    {
        _status = CreateStatus("created-job");

        var response = await SendAsync("CreateReindexForTenant", body: Parameters(
            ("maximumNumberOfResourcesPerQuery", "valueInteger", 50),
            ("maximumNumberOfResourcesPerWrite", "valueInteger", 25),
            ("maximumConcurrency", "valueInteger", 2),
            ("queryDelayIntervalInMilliseconds", "valueInteger", 5)));

        response.StatusCode.ShouldBe(StatusCodes.Status201Created);
        response.Headers["Content-Location"].ShouldBe("/tenant/1/$reindex/created-job");
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("Parameters");
        Value(response.Body, "id").ShouldBe("created-job");
        _createCommands.ShouldHaveSingleItem();
        _createCommands[0].MaximumNumberOfResourcesPerQuery.ShouldBe(50);
        _createCommands[0].MaximumNumberOfResourcesPerWrite.ShouldBe(25);
        _createCommands[0].MaximumConcurrency.ShouldBe(2);
        _createCommands[0].QueryDelayIntervalInMilliseconds.ShouldBe(5);
    }

    [Theory]
    [InlineData("CreateReindexForTenant")]
    [InlineData("ListReindexForTenant")]
    [InlineData("GetReindexForTenant")]
    [InlineData("CancelReindexForTenant")]
    [InlineData("CreateReindex")]
    [InlineData("ListReindex")]
    [InlineData("GetReindex")]
    [InlineData("CancelReindex")]
    public async Task GivenDisabledReindex_WhenMappingEndpoints_ThenOperationalRoutesRemainMapped(
        string operationEndpointName)
    {
        _availability.GetAvailabilityAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ReindexAvailability.Disabled));
        _createResult = new ReindexDisabledResult();

        var endpointNames = ((IEndpointRouteBuilder)_app).DataSources
            .SelectMany(source => source.Endpoints)
            .Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .ToArray();

        endpointNames.ShouldContain(operationEndpointName);
        endpointNames.ShouldContain("GetReindexOperationDefinitionForTenant");

        var response = await SendAsync(operationEndpointName, body: Parameters());

        response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        response.Body["issue"]![0]!["diagnostics"]!.GetValue<string>()
            .ShouldBe("The $reindex operation is disabled on this server.");
        if (!operationEndpointName.StartsWith("Create", StringComparison.Ordinal))
        {
            await _mediator.DidNotReceive().SendAsync(
                Arg.Any<CreateReindexJobCommand>(),
                Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task GivenEmptyChunkedRequestBody_WhenCreating_ThenUsesDefaultParameters()
    {
        var response = await SendAsync(
            "CreateReindexForTenant",
            body: string.Empty,
            setContentLength: false);

        response.StatusCode.ShouldBe(StatusCodes.Status201Created);
        _createCommands.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("maximumNumberOfResourcesPerQuery", "valueInteger", 10)]
    [InlineData("maximumNumberOfResourcesPerWrite", "valueInteger", 10)]
    [InlineData("maximumConcurrency", "valueInteger", 2)]
    [InlineData("queryDelayIntervalInMilliseconds", "valueInteger", 5)]
    public async Task GivenDuplicateSingletonParameter_WhenCreating_ThenReturnsBadRequestWithoutDispatching(
        string name,
        string valueName,
        object value)
    {
        var response = await SendAsync("CreateReindexForTenant", body: Parameters(
            (name, valueName, value),
            (name, valueName, value)));

        response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        _createCommands.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("CreateReindex")]
    [InlineData("CreateReindexForTenant")]
    public async Task GivenTargetResourceTypes_WhenCreating_ThenReturnsUnsupportedParameterOutcomeWithoutDispatching(
        string endpointName)
    {
        var response = await SendAsync(endpointName, body: Parameters(
            ("targetResourceTypes", "valueString", "Patient")));

        response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        response.Body["issue"]![0]!["diagnostics"]!.GetValue<string>()
            .ShouldBe("Parameter 'targetResourceTypes' is not supported.");
        _createCommands.ShouldBeEmpty();
    }

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
        var status = CreateStatus();
        _status = status with
        {
            Progress = JsonNode.Parse($$"""
                {
                  "totalResourcesToReindex": {{largeValue}},
                  "resourcesSuccessfullyReindexed": {{largeValue}},
                  "conflicts": {{largeValue}},
                  "tenants": [{
                    "tenantId": 1,
                    "cutoffTransactionId": {{largeValue}},
                    "cutoffSurrogateId": {{largeValue}},
                    "resourcesToReindex": {{largeValue}},
                    "resourcesReindexed": {{largeValue}},
                    "conflicts": {{largeValue}},
                    "failedResources": {{largeValue}}
                  }]
                }
                """),
            Definition = new ReindexJobDefinition
            {
                TargetEventId = largeValue,
                TenantIds = status.Definition!.TenantIds,
                ResourceTypes = status.Definition.ResourceTypes,
                SearchParameters = status.Definition.SearchParameters,
                MaximumNumberOfResourcesPerQuery = status.Definition.MaximumNumberOfResourcesPerQuery,
                MaximumNumberOfResourcesPerWrite = status.Definition.MaximumNumberOfResourcesPerWrite,
                MaximumConcurrency = status.Definition.MaximumConcurrency,
                QueryDelayIntervalInMilliseconds = status.Definition.QueryDelayIntervalInMilliseconds,
                Trigger = status.Definition.Trigger
            }
        };

        var response = await SendAsync(
            "GetReindexForTenant",
            fhirVersion: fhirVersion);

        response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        ValueProperty(response.Body, "totalResourcesToReindex").ShouldBe(countValueName);
        ValueProperty(response.Body, "resourcesSuccessfullyReindexed").ShouldBe(countValueName);
        ValueProperty(response.Body, "conflicts").ShouldBe(countValueName);
        ValueProperty(response.Body, "targetEventId").ShouldBe(identifierValueName);

        var tenant = Parts(response.Body, "tenant").ShouldHaveSingleItem();
        ValueProperty(tenant, "cutoffTransactionId").ShouldBe(identifierValueName);
        ValueProperty(tenant, "cutoffSurrogateId").ShouldBe(identifierValueName);
        ValueProperty(tenant, "resourcesToReindex").ShouldBe(countValueName);
        ValueProperty(tenant, "resourcesReindexed").ShouldBe(countValueName);
        ValueProperty(tenant, "conflicts").ShouldBe(countValueName);
        ValueProperty(tenant, "failedResources").ShouldBe(countValueName);
    }

    [Fact]
    public async Task GivenActiveJob_WhenCreating_ThenReturnsConflictOutcomeAndActiveLocation()
    {
        _createResult = new ActiveReindexJobResult("active-job");

        var response = await SendAsync("CreateReindexForTenant", body: Parameters());

        response.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        response.Headers["Content-Location"].ShouldBe("/tenant/1/$reindex/active-job");
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
    }

    [Fact]
    public async Task GivenStatusWithAllFields_WhenGettingStatus_ThenMapsFhirParameters()
    {
        _status = CreateStatus();

        var response = await SendAsync("GetReindexForTenant", jobId: "complete-fields");

        response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("Parameters");
        Value(response.Body, "id").ShouldBe("complete-fields");
        Value(response.Body, "status").ShouldBe("Running");
        Value(response.Body, "queuedTime").ShouldBeOfType<string>();
        Value(response.Body, "startTime").ShouldBeOfType<string>();
        Value(response.Body, "endTime").ShouldBeOfType<string>();
        Value(response.Body, "lastModified").ShouldBeOfType<string>();
        Value(response.Body, "totalResourcesToReindex").ShouldBe(10);
        Value(response.Body, "resourcesSuccessfullyReindexed").ShouldBe(7);
        Convert.ToDouble(Value(response.Body, "progress"), System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe(99.9d);
        Value(response.Body, "phase").ShouldBe("Reindexing");
        Value(response.Body, "cancellationReason").ShouldBe("Operator request");
        Value(response.Body, "conflicts").ShouldBe(2);
        Value(response.Body, "failureDetails").ShouldBe("failure detail");
        Value(response.Body, "maximumNumberOfResourcesPerQuery").ShouldBe(50);
        Value(response.Body, "maximumNumberOfResourcesPerWrite").ShouldBe(25);
        Value(response.Body, "maximumConcurrency").ShouldBe(2);
        Value(response.Body, "queryDelayIntervalInMilliseconds").ShouldBe(5);
        Value(response.Body, "trigger").ShouldBe("Manual");
        Value(response.Body, "targetEventId").ShouldBe("42");
        Values(response.Body, "resources").ShouldBe(["Patient", "Observation"]);
        Values(response.Body, "searchParams").ShouldBe(["http://example.test/SearchParameter/name"]);
        Values(response.Body, "notCovered").ShouldBeEmpty();
        Values(response.Body, "ignoredLifecycleEvents").ShouldBe(["http://example.test/SearchParameter/ignored"]);

        var tenants = Parts(response.Body, "tenant");
        tenants.Count.ShouldBe(2);
        Value(tenants[0], "tenantId").ShouldBe(1);
        Value(tenants[0], "cutoffTransactionId").ShouldBe("10");
        Value(tenants[0], "cutoffSurrogateId").ShouldBe("20");
        Value(tenants[0], "resourcesToReindex").ShouldBe(5);
        Value(tenants[1], "tenantId").ShouldBe(2);

        var failures = Parts(response.Body, "failedResource");
        failures.Count.ShouldBe(2);
        Value(failures[0], "resourceType").ShouldBe("Patient");
        Value(failures[0], "id").ShouldBe("first");
        Value(failures[0], "reason").ShouldBe("first failure");
        Value(failures[1], "id").ShouldBe("second");
    }

    [Fact]
    public async Task GivenCompletionDecisionSurvivesRestart_WhenGettingStatus_ThenTerminalProgressRoundTrips()
    {
        const string canonical = "http://example.test/SearchParameter/patient-custom";
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns([
                new TenantConfiguration
                {
                    TenantId = 1,
                    DisplayName = "Tenant 1",
                    FhirVersion = "4.0",
                    IsActive = true
                }
            ]);
        var repository = new InMemoryBackgroundJobRepository<ReindexJobDefinition>(
            tenants,
            NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);
        var target = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        await repository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
        {
            JobId = "round-trip",
            OrchestrationInstanceId = "round-trip",
            JobType = (int)BackgroundJobType.Reindex,
            Status = "Running",
            Definition = new ReindexJobDefinition
            {
                TargetEventId = 1,
                TenantIds = [1],
                ResourceTypes = ["Patient"],
                SearchParameters =
                [
                    new ReindexParameterDefinition(
                        target.Canonical,
                        target.Code,
                        target.ResourceType,
                        target.SearchParamId,
                        target.ActivationEventId,
                        target.AffectedResourceTypes)
                ],
                MaximumNumberOfResourcesPerQuery = 10,
                MaximumNumberOfResourcesPerWrite = 10,
                MaximumConcurrency = 1,
                QueryDelayIntervalInMilliseconds = 0,
                Trigger = "Manual"
            },
            CreateDate = DateTimeOffset.UtcNow,
            HeartbeatDate = DateTimeOffset.UtcNow
        }, CancellationToken.None);

        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical));
        var lifecycle = new ReindexLifecycleEventWriter(EventStore(), state);
        await lifecycle.StartAsync("round-trip", [target], CancellationToken.None);
        var fhirRepository = Substitute.For<IFhirRepository, IReindexStore>();
        ((IReindexStore)fhirRepository).HasSearchParameterAsync(17, Arg.Any<CancellationToken>())
            .Returns(true);
        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        repositoryFactory.GetRepositoryAsync(1, Arg.Any<CancellationToken>())
            .Returns(fhirRepository);
        using var jobLock = new TestJobLock();
        var writer = new CompleteReindexActivity(
            repositoryFactory,
            lifecycle,
            new ReindexJobUpdater(repository, jobLock, new ThrowingCompletionHook()),
            tenants,
            TimeProvider.System);
        var failedResource = new ReindexFailedResource("Patient", "p1", "index failure");

        await Should.ThrowAsync<DurableTask.Core.Exceptions.TaskFailureException>(() => writer.RunAsync(
            new TaskContext(new OrchestrationInstance { InstanceId = "round-trip" }),
            JsonSerializer.Serialize(new[]
            {
                new CompleteReindexInput(
                    "round-trip",
                    1,
                    [target],
                    [new ReindexTenantOutput(1, true, 10, 20, 1, 0, 0, 1, [failedResource], "index failure")],
                    [])
            })));

        (await repository.GetAsync("round-trip", 1, CancellationToken.None))!
            .Status.ShouldBe("Completing");

        var runtime = Substitute.For<IOrchestrationServiceClient>();
        runtime.GetOrchestrationStateAsync("round-trip", false).Returns([]);
        var reconciler = new ReindexJobReconciler(
            new TaskHubClient(runtime),
            repository,
            lifecycle,
            new ReindexJobUpdater(repository, jobLock, Substitute.For<IReindexCompletionHook>()),
            jobLock,
            Options.Create(new ReindexOptions()),
            TimeProvider.System,
            NullLogger<ReindexJobReconciler>.Instance);
        await reconciler.ReconcileAsync(CancellationToken.None);

        var statusHandler = new GetReindexStatusHandler(
            repository,
            Options.Create(new ReindexOptions()),
            TimeProvider.System,
            NullLogger<GetReindexStatusHandler>.Instance);
        _status = (await statusHandler.HandleAsync(
            new GetReindexStatusQuery("round-trip"),
            CancellationToken.None))!;

        var response = await SendAsync("GetReindexForTenant", jobId: "round-trip");

        response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        Value(response.Body, "status").ShouldBe("Failed");
        var tenant = Parts(response.Body, "tenant").ShouldHaveSingleItem();
        Value(tenant, "tenantId").ShouldBe(1);
        Value(tenant, "resourcesToReindex").ShouldBe(1);
        Value(tenant, "resourcesReindexed").ShouldBe(0);
        Value(tenant, "failedResources").ShouldBe(1);
        var failure = Parts(response.Body, "failedResource").ShouldHaveSingleItem();
        Value(failure, "resourceType").ShouldBe("Patient");
        Value(failure, "id").ShouldBe("p1");
        Value(failure, "reason").ShouldBe("index failure");
    }

    [Fact]
    public async Task GivenUnknownJob_WhenGettingStatusOrCancelling_ThenReturnsNotFoundOutcome()
    {
        _status = null!;
        _cancelResult = new ReindexJobNotFoundResult("missing");

        var status = await SendAsync("GetReindexForTenant", jobId: "missing");
        var cancel = await SendAsync("CancelReindexForTenant", jobId: "missing");

        status.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        status.Body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        cancel.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        cancel.Body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
    }

    [Fact]
    public async Task GivenRunningJob_WhenCancelling_ThenReturnsAcceptedParameters()
    {
        _cancelResult = new ReindexCancelledResult("running");

        var response = await SendAsync("CancelReindexForTenant", jobId: "running");

        response.StatusCode.ShouldBe(StatusCodes.Status202Accepted);
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("Parameters");
        Value(response.Body, "id").ShouldBe("running");
        Value(response.Body, "status").ShouldBe("Cancelled");
    }

    [Fact]
    public async Task GivenTerminalJob_WhenCancelling_ThenReturnsConflictOutcome()
    {
        _cancelResult = new ReindexJobAlreadyTerminalResult("done", "Completed");

        var response = await SendAsync("CancelReindexForTenant", jobId: "done");

        response.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
    }

    [Fact]
    public async Task GivenNoReindexWork_WhenCreating_ThenReturnsBadRequestOutcome()
    {
        _createResult = new NoReindexWorkResult("No resources need reindexing.");

        var response = await SendAsync("CreateReindexForTenant", body: Parameters());

        response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
    }

    [Theory]
    [InlineData("unexpected")]
    [InlineData("targetSearchParameterTypes")]
    [InlineData("targetDataStoreUsagePercentage")]
    public async Task GivenUnsupportedParameter_WhenCreating_ThenReturnsBadRequest(string parameter)
    {
        var response = await SendAsync("CreateReindexForTenant", body: Parameters(
            (parameter, "valueString", "value")));

        response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        _createCommands.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("maximumNumberOfResourcesPerQuery")]
    [InlineData("maximumNumberOfResourcesPerWrite")]
    [InlineData("maximumConcurrency")]
    [InlineData("queryDelayIntervalInMilliseconds")]
    public async Task GivenNonNumericParameter_WhenCreating_ThenReturnsBadRequest(string parameter)
    {
        var response = await SendAsync("CreateReindexForTenant", body: Parameters(
            (parameter, "valueString", "not-an-integer")));

        response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        _createCommands.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("maximumNumberOfResourcesPerQuery", 0, StatusCodes.Status400BadRequest)]
    [InlineData("maximumNumberOfResourcesPerQuery", 10001, StatusCodes.Status400BadRequest)]
    [InlineData("maximumNumberOfResourcesPerWrite", 0, StatusCodes.Status400BadRequest)]
    [InlineData("maximumNumberOfResourcesPerWrite", 10001, StatusCodes.Status400BadRequest)]
    [InlineData("maximumConcurrency", 0, StatusCodes.Status400BadRequest)]
    [InlineData("maximumConcurrency", 17, StatusCodes.Status400BadRequest)]
    [InlineData("queryDelayIntervalInMilliseconds", -1, StatusCodes.Status400BadRequest)]
    [InlineData("queryDelayIntervalInMilliseconds", 60001, StatusCodes.Status400BadRequest)]
    public async Task GivenOutOfRangeParameter_WhenCreating_ThenReturnsExpectedStatusCode(
        string parameter,
        int value,
        int expectedStatusCode)
    {
        _createResult = new InvalidReindexRequestResult($"{parameter} is out of range.");

        var response = await SendAsync("CreateReindexForTenant", body: Parameters(
            (parameter, "valueInteger", value)));

        response.StatusCode.ShouldBe(expectedStatusCode);
        _createCommands.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GivenInvalidPreferHeader_WhenCreating_ThenReturnsBadRequestWithoutDispatching()
    {
        var response = await SendAsync("CreateReindexForTenant", body: Parameters(), prefer: "return=representation");

        response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
        _createCommands.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("CreateReindexForTenant")]
    [InlineData("ListReindexForTenant")]
    [InlineData("GetReindexForTenant")]
    [InlineData("CancelReindexForTenant")]
    [InlineData("CreateReindex")]
    [InlineData("ListReindex")]
    [InlineData("GetReindex")]
    [InlineData("CancelReindex")]
    public async Task GivenMixedProviderServer_WhenUsingOperationalRoute_ThenReturnsNotImplementedOutcome(
        string operationEndpointName)
    {
        _availability.GetAvailabilityAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReindexAvailability(ReindexAvailabilityStatus.Unsupported, 2)));
        _createResult = new ReindexProviderUnavailableResult(2);

        var response = await SendAsync(operationEndpointName, body: Parameters());

        response.StatusCode.ShouldBe(StatusCodes.Status501NotImplemented);
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        response.Body["issue"]![0]!["diagnostics"]!.GetValue<string>()
            .ShouldBe("The $reindex operation is not supported by all active tenant providers.");
    }

    [Fact]
    public async Task GivenSystemTenantRoute_WhenAccessingReindex_ThenItIsRejected()
    {
        var response = await SendAsync("CreateReindexForTenant", tenantId: 0, body: Parameters());

        response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        _createCommands.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("CreateReindexForTenant")]
    [InlineData("ListReindexForTenant")]
    [InlineData("GetReindexForTenant")]
    [InlineData("CancelReindexForTenant")]
    [InlineData("CreateReindex")]
    [InlineData("ListReindex")]
    [InlineData("GetReindex")]
    [InlineData("CancelReindex")]
    public async Task GivenDeniedAuthorization_WhenAccessingReindexRoute_ThenEndpointReturnsForbiddenWithoutDispatching(
        string endpointName)
    {
        var mediator = Substitute.For<IMediator>();
        var authorization = Substitute.For<IFhirAuthorizationService>();
        var contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        var availability = Substitute.For<IReindexAvailability>();
        contextAccessor.RequestContext.Returns(Substitute.For<IFhirRequestContext>());
        availability.GetAvailabilityAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ReindexAvailability.Available));
        FhirAuthorizationContext? captured = null;
        authorization.AuthorizeAsync(Arg.Any<FhirAuthorizationContext>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<FhirAuthorizationContext>();
                return AuthorizationResult.Denied("Administrative access is required.");
            });

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(mediator);
        builder.Services.AddSingleton(availability);
        builder.Services.AddSingleton(authorization);
        builder.Services.AddSingleton(contextAccessor);
        builder.Services.AddSingleton(Substitute.For<IAuditLogger>());
        builder.Services.AddSingleton(Substitute.For<IMetricsService>());
        builder.Services.AddSingleton<IOptions<AuthorizationOptions>>(
            Options.Create(new AuthorizationOptions { Enabled = true }));
        await using var app = builder.Build();
        app.MapReindexEndpoints();

        var response = await SendAsync(app, endpointName, body: Parameters());

        response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        captured.ShouldNotBeNull();
        captured!.Interaction.ShouldBe(FhirInteraction.Update);
        captured.ResourceType.ShouldBe("*");
        await mediator.DidNotReceive().SendAsync(
            Arg.Any<CreateReindexJobCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("CreateReindexForTenant")]
    [InlineData("ListReindexForTenant")]
    [InlineData("GetReindexForTenant")]
    [InlineData("CancelReindexForTenant")]
    [InlineData("CreateReindex")]
    [InlineData("ListReindex")]
    [InlineData("GetReindex")]
    [InlineData("CancelReindex")]
    public async Task GivenReadOnlySmartScope_WhenAccessingReindexRoute_ThenReturnsForbidden(string endpointName)
    {
        await using var app = CreateAuthorizedApp([]);

        var response = await SendAsync(
            app,
            endpointName,
            body: Parameters(),
            user: CreateUser(scope: "system/*.read"));

        response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
    }

    [Theory]
    [InlineData("CreateReindexForTenant")]
    [InlineData("ListReindexForTenant")]
    [InlineData("GetReindexForTenant")]
    [InlineData("CancelReindexForTenant")]
    [InlineData("CreateReindex")]
    [InlineData("ListReindex")]
    [InlineData("GetReindex")]
    [InlineData("CancelReindex")]
    public async Task GivenWildcardWriteSmartScope_WhenAccessingReindexRoute_ThenRequestIsAllowed(string endpointName)
    {
        await using var app = CreateAuthorizedApp([]);

        var response = await SendAsync(
            app,
            endpointName,
            body: Parameters(),
            user: CreateUser(scope: "system/*.write"));

        response.StatusCode.ShouldNotBe(StatusCodes.Status403Forbidden);
    }

    [Theory]
    [InlineData("CreateReindexForTenant")]
    [InlineData("ListReindexForTenant")]
    [InlineData("GetReindexForTenant")]
    [InlineData("CancelReindexForTenant")]
    [InlineData("CreateReindex")]
    [InlineData("ListReindex")]
    [InlineData("GetReindex")]
    [InlineData("CancelReindex")]
    public async Task GivenReadOnlyRbacPermission_WhenAccessingReindexRoute_ThenReturnsForbidden(string endpointName)
    {
        await using var app = CreateAuthorizedApp([new ResourceGrant("*", "read")]);

        var response = await SendAsync(
            app,
            endpointName,
            body: Parameters(),
            user: CreateUser(role: "Reader"));

        response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
    }

    [Theory]
    [InlineData("CreateReindexForTenant")]
    [InlineData("ListReindexForTenant")]
    [InlineData("GetReindexForTenant")]
    [InlineData("CancelReindexForTenant")]
    [InlineData("CreateReindex")]
    [InlineData("ListReindex")]
    [InlineData("GetReindex")]
    [InlineData("CancelReindex")]
    public async Task GivenWildcardWriteRbacPermission_WhenAccessingReindexRoute_ThenRequestIsAllowed(string endpointName)
    {
        await using var app = CreateAuthorizedApp([new ResourceGrant("*", "update")]);

        var response = await SendAsync(
            app,
            endpointName,
            body: Parameters(),
            user: CreateUser(role: "Writer"));

        response.StatusCode.ShouldNotBe(StatusCodes.Status403Forbidden);
    }

    [Theory]
    [InlineData("GetReindexOperationDefinitionForTenant")]
    [InlineData("GetReindexOperationDefinition")]
    public async Task GivenReadOnlySmartScope_WhenGettingOperationDefinition_ThenRequestIsAllowed(
        string endpointName)
    {
        await using var app = CreateAuthorizedApp([]);

        var response = await SendAsync(
            app,
            endpointName,
            user: CreateUser(scope: "system/*.read"));

        response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task GivenAuthorizationDisabled_WhenAccessingReindexRoute_ThenReadOnlyScopeDoesNotBlockRequest()
    {
        await using var app = CreateAuthorizedApp([], authorizationEnabled: false);

        var response = await SendAsync(
            app,
            "CreateReindexForTenant",
            body: Parameters(),
            user: CreateUser(scope: "system/*.read"));

        response.StatusCode.ShouldBe(StatusCodes.Status201Created);
    }

    [Fact]
    public async Task GivenOperationDefinitionRequest_WhenGettingDefinition_ThenListsAcceptedParameters()
    {
        var response = await SendAsync("GetReindexOperationDefinitionForTenant");

        response.StatusCode.ShouldBe(StatusCodes.Status200OK);
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("OperationDefinition");
        response.Body["parameter"]!.AsArray().Select(parameter => parameter!["name"]!.GetValue<string>())
            .ShouldBe([
                "maximumNumberOfResourcesPerQuery",
                "maximumNumberOfResourcesPerWrite",
                "maximumConcurrency",
                "queryDelayIntervalInMilliseconds"
            ]);
    }

    private async Task<Response> SendAsync(
        string endpointName,
        int tenantId = 1,
        string jobId = "job",
        string? body = null,
        string? prefer = null,
        bool setContentLength = true,
        string fhirVersion = "4.0") =>
        await SendAsync(_app, endpointName, tenantId, jobId, body, prefer, setContentLength, fhirVersion);

    private WebApplication CreateAuthorizedApp(
        IReadOnlyList<ResourceGrant> rolePermissions,
        bool authorizationEnabled = true)
    {
        var permissionStore = Substitute.For<IRolePermissionStore>();
        permissionStore.GetPermissionsAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(rolePermissions);

        var authorization = new FhirAuthorizationService(
            [
                new RbacAuthorizationHandler(
                    permissionStore,
                    NullLogger<RbacAuthorizationHandler>.Instance),
                new SmartScopeAuthorizationHandler(
                    NullLogger<SmartScopeAuthorizationHandler>.Instance)
            ],
            NullLogger<FhirAuthorizationService>.Instance);
        var requestContext = Substitute.For<IFhirRequestContextAccessor>();
        requestContext.RequestContext.Returns(Substitute.For<IFhirRequestContext>());

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(_mediator);
        builder.Services.AddSingleton(_availability);
        builder.Services.AddSingleton<IFhirAuthorizationService>(authorization);
        builder.Services.AddSingleton(requestContext);
        builder.Services.AddSingleton(Substitute.For<IAuditLogger>());
        builder.Services.AddSingleton(Substitute.For<IMetricsService>());
        builder.Services.AddSingleton<IOptions<AuthorizationOptions>>(
            Options.Create(new AuthorizationOptions { Enabled = authorizationEnabled }));

        var app = builder.Build();
        app.MapReindexEndpoints();
        return app;
    }

    private static ClaimsPrincipal CreateUser(string? scope = null, string? role = null)
    {
        var claims = new List<Claim> { new(FhirClaimTypes.Subject, "user") };
        if (scope is not null)
        {
            claims.Add(new Claim(FhirClaimTypes.Scope, scope));
        }

        if (role is not null)
        {
            claims.Add(new Claim(FhirClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static async Task<Response> SendAsync(
        WebApplication app,
        string endpointName,
        int tenantId = 1,
        string jobId = "job",
        string? body = null,
        string? prefer = null,
        bool setContentLength = true,
        string fhirVersion = "4.0",
        ClaimsPrincipal? user = null)
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .Single(candidate => candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == endpointName);
        await using var scope = app.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.SetEndpoint(endpoint);
        context.Items["TenantId"] = tenantId;
        context.Items["TenantConfiguration"] = new TenantConfiguration
        {
            TenantId = tenantId,
            DisplayName = "Test tenant",
            FhirVersion = fhirVersion
        };
        context.Request.RouteValues["tenantId"] = tenantId.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
            ["parameter"] = new JsonArray(parameters.Select(parameter => (JsonNode)new JsonObject
            {
                ["name"] = parameter.Name,
                [parameter.ValueName] = JsonValue.Create(parameter.Value)
            }).ToArray())
        }.ToJsonString();

    private static object Value(JsonNode parameters, string name) =>
        ConvertValue(ValueNode(parameters, name));

    private static IReadOnlyList<object> Values(JsonNode parameters, string name) =>
        ParameterArray(parameters)
            .Where(parameter => parameter!["name"]!.GetValue<string>() == name)
            .Select(parameter => ConvertValue(ValueNode(parameter!)))
            .ToArray();

    private static IReadOnlyList<JsonNode> Parts(JsonNode parameters, string name) =>
        ParameterArray(parameters)
            .Where(parameter => parameter!["name"]!.GetValue<string>() == name)
            .Select(parameter => parameter!["part"]!)
            .ToArray();

    private static JsonArray ParameterArray(JsonNode parameters) =>
        parameters as JsonArray
        ?? parameters["parameter"] as JsonArray
        ?? throw new InvalidOperationException("Expected a Parameters.parameter array.");

    private static JsonNode ValueNode(JsonNode parameters, string name) =>
        ValueNode(ParameterArray(parameters)
            .Single(parameter => parameter!["name"]!.GetValue<string>() == name)!);

    private static JsonNode ValueNode(JsonNode parameter) =>
        parameter.AsObject()
            .Single(property => property.Key.StartsWith("value", StringComparison.Ordinal))
            .Value
        ?? throw new InvalidOperationException("Expected a Parameters value.");

    private static string ValueProperty(JsonNode parameters, string name) =>
        ParameterArray(parameters)
            .Single(parameter => parameter!["name"]!.GetValue<string>() == name)!
            .AsObject()
            .Single(property => property.Key.StartsWith("value", StringComparison.Ordinal))
            .Key;

    private static object ConvertValue(JsonNode value)
    {
        var jsonValue = value.AsValue();
        if (jsonValue.TryGetValue<string>(out var stringValue))
        {
            return stringValue;
        }
        if (jsonValue.TryGetValue<int>(out var intValue))
        {
            return intValue;
        }
        if (jsonValue.TryGetValue<long>(out var longValue))
        {
            return longValue;
        }
        if (jsonValue.TryGetValue<double>(out var doubleValue))
        {
            return doubleValue;
        }

        throw new InvalidOperationException($"Unsupported Parameters value {value}.");
    }

    private static ReindexStatusResult CreateStatus(string jobId = "complete-fields") =>
        new(
            jobId,
            "Running",
            DateTimeOffset.Parse("2026-10-07T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-10-07T00:01:00Z", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-10-07T00:03:00Z", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-10-07T00:02:00Z", System.Globalization.CultureInfo.InvariantCulture),
            false,
            "failure detail",
            JsonNode.Parse("""
                {
                  "totalResourcesToReindex": 10,
                  "resourcesSuccessfullyReindexed": 7,
                  "progress": 100,
                  "phase": "Reindexing",
                  "cancellationReason": "Operator request",
                  "conflicts": 2,
                  "notCovered": ["http://example.test/SearchParameter/not-covered"],
                  "ignoredLifecycleEvents": ["http://example.test/SearchParameter/ignored"],
                  "tenants": [
                    {"tenantId": 1, "cutoffTransactionId": 10, "cutoffSurrogateId": 20, "status": "Running", "resourcesToReindex": 5, "resourcesReindexed": 3, "conflicts": 1, "failedResources": 1},
                    {"tenantId": 2, "cutoffTransactionId": 11, "cutoffSurrogateId": 21, "status": "Running", "resourcesToReindex": 5, "resourcesReindexed": 4, "conflicts": 1, "failedResources": 1}
                  ],
                  "failedResources": [
                    {"resourceType": "Patient", "id": "first", "reason": "first failure"},
                    {"resourceType": "Observation", "id": "second", "reason": "second failure"}
                  ]
                }
                """),
            null,
            new ReindexJobDefinition
            {
                TargetEventId = 42,
                TenantIds = [1, 2],
                ResourceTypes = ["Patient", "Observation"],
                SearchParameters = [new ReindexParameterDefinition(
                    "http://example.test/SearchParameter/name", "name", "Patient", 1, 42, ["Patient"])],
                MaximumNumberOfResourcesPerQuery = 50,
                MaximumNumberOfResourcesPerWrite = 25,
                MaximumConcurrency = 2,
                QueryDelayIntervalInMilliseconds = 5,
                Trigger = "Manual"
            });

    private static ISourceEventStore EventStore()
    {
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(EmptyEvents());
        long nextEventId = 2;
        store.AppendAsync(
                Arg.Any<IEnumerable<NewSourceEvent>>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<NewSourceEvent>>()
                .Select(evt => new SourceEvent(
                    nextEventId++,
                    evt.StreamId,
                    evt.EventType,
                    evt.Data,
                    DateTimeOffset.UtcNow))
                .ToArray());
        return store;
    }

    private static async IAsyncEnumerable<SourceEvent> EmptyEvents()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static SourceEvent Activation(string canonical) => new(
        1,
        "search",
        nameof(SearchParameterActivated),
        new SearchParameterActivated(
            canonical,
            "custom",
            "Patient",
            "Patient.id",
            SearchParamType.String,
            "example@1.0.0",
            null,
            17,
            null,
            null,
            null,
            null),
        DateTimeOffset.UtcNow);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private sealed record Response(int StatusCode, JsonNode Body, IReadOnlyDictionary<string, string> Headers);

    private sealed class ThrowingCompletionHook : IReindexCompletionHook
    {
        public Task OnCompletedAsync(
            BackgroundJob<ReindexJobDefinition> job,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated restart");
    }

    private sealed class TestJobLock : IReindexJobLock, IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                return await action(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void Dispose() => _semaphore.Dispose();
    }
}
