using DurableTask.Core;
using DurableTask.Core.History;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using static Ignixa.Application.Tests.BackgroundOperations.Reindex.ReindexTestHelper;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public sealed class CreateReindexJobHandlerTests : IDisposable
{
    private static readonly ReindexParameterDefinition PendingTarget = Target(activationEventId: 42);

    private readonly InMemoryBackgroundJobRepository<ReindexJobDefinition> _jobs = JobRepository();
    private readonly IOrchestrationServiceClient _runtime = Substitute.For<IOrchestrationServiceClient>();
    private readonly TestReindexJobLock _jobLock = new();
    private readonly TestSourceEventStore _events = new();
    private readonly ConformanceState _state = new();

    public void Dispose() => _state.Dispose();

    [Fact]
    public async Task GivenNoPendingParameters_WhenAutomationRequestsReindex_ThenNothingToDoIsReturned()
    {
        var result = await CreateHandler().HandleAsync(
            new CreateReindexJobCommand { Trigger = ReindexTriggerKind.Activation },
            CancellationToken.None);

        result.ShouldBeOfType<NoReindexWorkResult>();
        (await _jobs.ListAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenNoActiveTenant_WhenAJobIsRequested_ThenNothingToDoNamesTheMissingTenant()
    {
        var result = await CreateHandler(withPendingParameter: true, tenants: []).HandleAsync(
            new CreateReindexJobCommand { Trigger = ReindexTriggerKind.Manual },
            CancellationToken.None);

        result.ShouldBeOfType<NoReindexWorkResult>().ErrorMessage.ShouldBe("No active tenant is configured for reindexing.");
        (await _jobs.ListAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenRemoteCompletionAfterPollCatchUp_WhenReconciliationAcquiresJobLock_ThenNoRedundantJobIsCreated()
    {
        var handler = CreateHandler(withPendingParameter: true);
        _events.Unread.AddRange(
        [
            new SourceEvent(
                43,
                "reindex:remote",
                nameof(SearchParameterReindexStarted),
                new SearchParameterReindexStarted(Canonical, "custom", "Patient", "remote-job", ["Patient"], 42),
                DateTimeOffset.UtcNow),
            new SourceEvent(
                44,
                "reindex:remote",
                nameof(SearchParameterReindexCompleted),
                new SearchParameterReindexCompleted(Canonical, "custom", "Patient", "remote-job", 1, TimeSpan.FromSeconds(1), 42),
                DateTimeOffset.UtcNow)
        ]);

        var result = await handler.HandleAsync(
            new CreateReindexJobCommand { Trigger = ReindexTriggerKind.Reconciliation },
            CancellationToken.None);

        result.ShouldBeOfType<NoReindexWorkResult>();
        (await _jobs.ListAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task GivenJobEndsUnsuccessfullyBeforeReconciliationAcquiresLock_WhenReconciliationCreatesJob_ThenRestartIsSuppressed(
        string status)
    {
        var handler = CreateHandler(withPendingParameter: true);
        _jobLock.OnAcquired = () => _jobs.CreateAsync(
            Job(status, "failed-during-race", CreateJobDefinition([PendingTarget], targetEventId: 42)),
            CancellationToken.None);

        var result = await handler.HandleAsync(
            new CreateReindexJobCommand { Trigger = ReindexTriggerKind.Reconciliation },
            CancellationToken.None);

        result.ShouldBeOfType<NoReindexWorkResult>().ErrorMessage.ShouldContain("failed-during-race");
        (await _jobs.ListAsync()).ShouldHaveSingleItem().JobId.ShouldBe("failed-during-race");
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task GivenFinishedJobTargetedAnOlderActivation_WhenReconciliationCreatesJob_ThenJobStarts(string status)
    {
        var handler = CreateHandler(withPendingParameter: true);
        await _jobs.CreateAsync(Job(status, "older", CreateJobDefinition(targetEventId: 41)), CancellationToken.None);

        var result = await handler.HandleAsync(
            new CreateReindexJobCommand { Trigger = ReindexTriggerKind.Reconciliation },
            CancellationToken.None);

        var job = await CreatedJobAsync(result);
        job.Definition.TargetEventId.ShouldBe(42);
        job.Definition.Trigger.ShouldBe("Reconciliation");
        job.Definition.SearchParameters.Select(parameter => parameter.Code).ShouldBe(["custom"]);
    }

    [Fact]
    public async Task GivenFinishedJobCoversPendingParameters_WhenJobIsCreatedManually_ThenJobStarts()
    {
        var handler = CreateHandler(withPendingParameter: true);
        await _jobs.CreateAsync(
            Job("Failed", "failed", CreateJobDefinition([PendingTarget], targetEventId: 42)),
            CancellationToken.None);

        var result = await handler.HandleAsync(new CreateReindexJobCommand(), CancellationToken.None);

        (await CreatedJobAsync(result)).Definition.Trigger.ShouldBe("Manual");
    }

    [Fact]
    public async Task GivenInvalidConcurrency_WhenJobIsCreated_ThenTypedValidationResultIsReturned()
    {
        var result = await CreateHandler(withPendingParameter: true).HandleAsync(
            new CreateReindexJobCommand { MaximumConcurrency = 17 },
            CancellationToken.None);

        result.ShouldBeOfType<InvalidReindexRequestResult>();
        (await _jobs.ListAsync()).ShouldBeEmpty();
        _jobLock.Acquisitions.ShouldBe(0);
    }

    [Theory]
    [InlineData("Queued")]
    [InlineData("Running")]
    public async Task GivenActiveJob_WhenJobIsCreated_ThenActiveJobIdIsReturned(string status)
    {
        var handler = CreateHandler(withPendingParameter: true);
        await _jobs.CreateAsync(Job(status, "active"), CancellationToken.None);

        var result = await handler.HandleAsync(new CreateReindexJobCommand(), CancellationToken.None);

        result.ShouldBeOfType<ActiveReindexJobResult>().ActiveJobId.ShouldBe("active");
        (await _jobs.ListAsync()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GivenJobIsCreated_WhenOrchestrationStarts_ThenRowAndInputCarryTheResolvedPlan()
    {
        var result = await CreateHandler(withPendingParameter: true).HandleAsync(
            new CreateReindexJobCommand { Trigger = ReindexTriggerKind.Activation },
            CancellationToken.None);

        var job = await CreatedJobAsync(result);
        job.Status.ShouldBe("Queued");
        job.OrchestrationInstanceId.ShouldBe(job.JobId);
        job.CreateDate.ShouldBe(Now);
        job.Definition.TargetEventId.ShouldBe(42);
        job.Definition.TenantIds.ShouldBe([1]);
        job.Definition.ResourceTypes.ShouldBe(["Patient"]);
        job.Definition.Trigger.ShouldBe("Activation");
        await _runtime.Received(1).CreateTaskOrchestrationAsync(
            Arg.Is<TaskMessage>(message =>
                message.OrchestrationInstance.InstanceId == job.JobId &&
                message.Event is ExecutionStartedEvent),
            Arg.Any<OrchestrationStatus[]>());
    }

    [Fact]
    public async Task GivenOrchestrationStartFailure_WhenJobIsCreated_ThenRowIsDeletedAndNextAutomaticStartSucceeds()
    {
        var handler = CreateHandler(withPendingParameter: true);
        var failStart = true;
        _runtime.CreateTaskOrchestrationAsync(Arg.Any<TaskMessage>(), Arg.Any<OrchestrationStatus[]>())
            .Returns(_ => failStart ? Task.FromException(new TimeoutException("runtime unavailable")) : Task.CompletedTask);

        await Should.ThrowAsync<TimeoutException>(() => handler.HandleAsync(new CreateReindexJobCommand(), CancellationToken.None));
        (await _jobs.ListAsync()).ShouldBeEmpty();
        failStart = false;
        var retried = await handler.HandleAsync(
            new CreateReindexJobCommand { Trigger = ReindexTriggerKind.Reconciliation },
            CancellationToken.None);

        retried.ShouldBeOfType<ReindexJobCreatedResult>();
    }

    private async Task<BackgroundJob<ReindexJobDefinition>> CreatedJobAsync(CreateReindexJobResult result) =>
        (await _jobs.GetAsync(result.ShouldBeOfType<ReindexJobCreatedResult>().JobId, 1, CancellationToken.None))!;

    private CreateReindexJobHandler CreateHandler(bool withPendingParameter = false, TenantConfiguration[]? tenants = null)
    {
        if (withPendingParameter)
        {
            _state.ApplyAndTrack(Activation(eventId: 42));
        }

        var schema = Substitute.For<IFhirSchemaProvider>();
        schema.ResourceTypeNames.Returns(new HashSet<string>(StringComparer.Ordinal) { "Patient" });
        var patient = Substitute.For<IType>();
        patient.Info.Returns(new TypeInfo("Patient", isResource: true));
        schema.GetTypeDefinition("Patient").Returns(patient);
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetSchemaProvider(FhirVersion.R4, 1).Returns(schema);
        return new CreateReindexJobHandler(
            new TaskHubClient(_runtime),
            _jobs,
            _jobLock,
            new ReindexTargetResolver(TenantStore(tenants ?? [Tenant(1)]), versions, _state, _events),
            Options.Create(new ReindexOptions { BarrierDelay = TimeSpan.Zero }),
            new FixedTimeProvider(Now));
    }
}
