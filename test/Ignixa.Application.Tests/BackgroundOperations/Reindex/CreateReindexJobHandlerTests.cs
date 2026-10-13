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

/// <summary>By default <c>Patient.custom</c> is Pending from activation event 42 and tenant 1 is active.</summary>
public sealed class CreateReindexJobHandlerTests : IDisposable
{
    private static readonly ReindexParameterDefinition PendingTarget = Target(activationEventId: 42);

    private readonly InMemoryBackgroundJobRepository<ReindexJobDefinition> _jobs = JobRepository();
    private readonly IOrchestrationServiceClient _runtime = Substitute.For<IOrchestrationServiceClient>();
    private readonly TestReindexJobLock _jobLock = new();
    private readonly TestSourceEventStore _events = new();
    private ConformanceState _state = new();
    private TenantConfiguration[] _tenants = [Tenant(1)];

    public CreateReindexJobHandlerTests() => _state.ApplyAndTrack(Activation(eventId: 42));

    public void Dispose() => _state.Dispose();

    // §6.1: with no Pending parameters there is nothing to do and no job row.
    [Fact]
    public async Task GivenNoPendingParameters_WhenAutomationRequestsReindex_ThenNothingToDoIsReturned()
    {
        _state.Dispose();
        _state = new ConformanceState();

        var result = await HandleAsync(ReindexTriggerKind.Activation);

        result.ShouldBeOfType<NoReindexWorkResult>();
        (await _jobs.ListAsync()).ShouldBeEmpty();
    }

    // §10.1: a job fans out over the active tenants, so with none there is nothing to do.
    [Fact]
    public async Task GivenNoActiveTenant_WhenAJobIsRequested_ThenNothingToDoNamesTheMissingTenant()
    {
        _tenants = [];

        var result = await HandleAsync();

        result.ShouldBeOfType<NoReindexWorkResult>().ErrorMessage.ShouldBe("No active tenant is configured for reindexing.");
        (await _jobs.ListAsync()).ShouldBeEmpty();
    }

    // §7: the start rule is re-applied under the singleton lock after catching up.
    [Fact]
    public async Task GivenRemoteCompletionAfterPollCatchUp_WhenReconciliationAcquiresJobLock_ThenNoRedundantJobIsCreated()
    {
        _events.Unread.AddRange(
        [
            RemoteEvent(43, new SearchParameterReindexStarted(Canonical, "custom", "Patient", "remote-job", ["Patient"], 42)),
            RemoteEvent(44, new SearchParameterReindexCompleted(
                Canonical, "custom", "Patient", "remote-job", 1, TimeSpan.FromSeconds(1), 42))
        ]);

        var result = await HandleAsync(ReindexTriggerKind.Reconciliation);

        result.ShouldBeOfType<NoReindexWorkResult>();
        (await _jobs.ListAsync()).ShouldBeEmpty();
    }

    // §7 start rule: a finished job's own Pending parameters never restart on their own.
    [Theory]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task GivenJobEndsUnsuccessfullyBeforeReconciliationAcquiresLock_WhenReconciliationCreatesJob_ThenRestartIsSuppressed(
        string status)
    {
        _jobLock.OnAcquired = () => _jobs.CreateAsync(
            Job(status, "failed-during-race", CreateJobDefinition([PendingTarget], targetEventId: 42)),
            CancellationToken.None);

        var result = await HandleAsync(ReindexTriggerKind.Reconciliation);

        result.ShouldBeOfType<NoReindexWorkResult>().ErrorMessage.ShouldContain("failed-during-race");
        (await _jobs.ListAsync()).ShouldHaveSingleItem().JobId.ShouldBe("failed-during-race");
    }

    // §7 start rule: a Pending activation newer than the latest finished job's TargetEventId starts a job.
    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task GivenFinishedJobTargetedAnOlderActivation_WhenReconciliationCreatesJob_ThenJobStarts(string status)
    {
        await _jobs.CreateAsync(Job(status, "older", CreateJobDefinition(targetEventId: 41)), CancellationToken.None);

        var job = await CreatedJobAsync(await HandleAsync(ReindexTriggerKind.Reconciliation));

        job.Definition.TargetEventId.ShouldBe(42);
        job.Definition.Trigger.ShouldBe("Reconciliation");
        job.Definition.SearchParameters.Select(parameter => parameter.Code).ShouldBe(["custom"]);
    }

    // §7 start rule: a manual $reindex ignores the rule.
    [Fact]
    public async Task GivenFinishedJobCoversPendingParameters_WhenJobIsCreatedManually_ThenJobStarts()
    {
        await _jobs.CreateAsync(Job("Failed", "failed", CreateJobDefinition([PendingTarget], targetEventId: 42)), CancellationToken.None);

        (await CreatedJobAsync(await HandleAsync())).Definition.Trigger.ShouldBe("Manual");
    }

    // §6.1: an out-of-range parameter is a typed 400 result, rejected before the job lock.
    [Fact]
    public async Task GivenInvalidConcurrency_WhenJobIsCreated_ThenTypedValidationResultIsReturned()
    {
        var result = await CreateHandler().HandleAsync(new CreateReindexJobCommand { MaximumConcurrency = 17 }, CancellationToken.None);

        result.ShouldBeOfType<InvalidReindexRequestResult>();
        (await _jobs.ListAsync()).ShouldBeEmpty();
        _jobLock.Acquisitions.ShouldBe(0);
    }

    // §6.1 (F8): an active or queued job is returned for the 409 instead of a second job.
    [Theory]
    [InlineData("Queued")]
    [InlineData("Running")]
    public async Task GivenActiveJob_WhenJobIsCreated_ThenActiveJobIdIsReturned(string status)
    {
        await _jobs.CreateAsync(Job(status, "active"), CancellationToken.None);

        var result = await HandleAsync();

        result.ShouldBeOfType<ActiveReindexJobResult>().ActiveJobId.ShouldBe("active");
        (await _jobs.ListAsync()).ShouldHaveSingleItem();
    }

    // §8.1: the handler writes the Queued job row and starts its orchestration with instanceId = jobId.
    [Fact]
    public async Task GivenJobIsCreated_WhenOrchestrationStarts_ThenRowAndInputCarryTheResolvedPlan()
    {
        var job = await CreatedJobAsync(await HandleAsync(ReindexTriggerKind.Activation));

        job.Status.ShouldBe("Queued");
        job.OrchestrationInstanceId.ShouldBe(job.JobId);
        job.CreateDate.ShouldBe(Now);
        job.Definition.TargetEventId.ShouldBe(42);
        job.Definition.TenantIds.ShouldBe([1]);
        job.Definition.ResourceTypes.ShouldBe(["Patient"]);
        job.Definition.Trigger.ShouldBe("Activation");
        await _runtime.Received(1).CreateTaskOrchestrationAsync(
            Arg.Is<TaskMessage>(message =>
                message.OrchestrationInstance.InstanceId == job.JobId && message.Event is ExecutionStartedEvent),
            Arg.Any<OrchestrationStatus[]>());
    }

    // §7: a job row whose orchestration could not be started is deleted.
    [Fact]
    public async Task GivenOrchestrationStartFailure_WhenJobIsCreated_ThenRowIsDeletedAndNextAutomaticStartSucceeds()
    {
        var failStart = true;
        _runtime.CreateTaskOrchestrationAsync(Arg.Any<TaskMessage>(), Arg.Any<OrchestrationStatus[]>())
            .Returns(_ => failStart ? Task.FromException(new TimeoutException("runtime unavailable")) : Task.CompletedTask);

        await Should.ThrowAsync<TimeoutException>(() => HandleAsync());
        (await _jobs.ListAsync()).ShouldBeEmpty();
        failStart = false;
        var retried = await HandleAsync(ReindexTriggerKind.Reconciliation);

        retried.ShouldBeOfType<ReindexJobCreatedResult>();
    }

    private static SourceEvent RemoteEvent(long eventId, object data) =>
        new(eventId, "reindex:remote", data.GetType().Name, data, DateTimeOffset.UtcNow);

    private async Task<BackgroundJob<ReindexJobDefinition>> CreatedJobAsync(CreateReindexJobResult result) =>
        (await _jobs.GetAsync(result.ShouldBeOfType<ReindexJobCreatedResult>().JobId, 1, CancellationToken.None))!;

    private Task<CreateReindexJobResult> HandleAsync(ReindexTriggerKind trigger = ReindexTriggerKind.Manual) =>
        CreateHandler().HandleAsync(new CreateReindexJobCommand { Trigger = trigger }, CancellationToken.None);

    private CreateReindexJobHandler CreateHandler()
    {
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
            new ReindexTargetResolver(TenantStore(_tenants), versions, _state, _events),
            Options.Create(new ReindexOptions { BarrierDelay = TimeSpan.Zero }),
            new FixedTimeProvider(Now));
    }
}
