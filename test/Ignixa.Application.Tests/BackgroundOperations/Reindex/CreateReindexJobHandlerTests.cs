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
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
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
            RemoteEvent(44, new SearchParameterReindexCompleted(Canonical, "custom", "Patient", "remote-job", 1, TimeSpan.FromSeconds(1), 42))
        ]);

        var result = await HandleAsync(ReindexTriggerKind.Reconciliation);

        result.ShouldBeOfType<NoReindexWorkResult>();
        (await _jobs.ListAsync()).ShouldBeEmpty();
    }

    // §7 start rule: a finished job's own Pending parameters never restart on their own.
    [Fact]
    public async Task GivenJobFailsBeforeReconciliationAcquiresLock_WhenReconciliationCreatesJob_ThenRestartIsSuppressed()
    {
        var finished = Job("Failed", "failed-during-race", CreateJobDefinition([PendingTarget], targetEventId: 42));
        _jobLock.OnAcquired = () => _jobs.CreateAsync(finished, CancellationToken.None);

        var result = await HandleAsync(ReindexTriggerKind.Reconciliation);

        result.ShouldBeOfType<NoReindexWorkResult>().ErrorMessage.ShouldContain("failed-during-race");
        (await _jobs.ListAsync()).ShouldHaveSingleItem().JobId.ShouldBe("failed-during-race");
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
    [Fact]
    public async Task GivenActiveJob_WhenJobIsCreated_ThenActiveJobIdIsReturned()
    {
        await _jobs.CreateAsync(Job("Running", "active"), CancellationToken.None);

        var result = await HandleAsync();

        result.ShouldBeOfType<ActiveReindexJobResult>().ActiveJobId.ShouldBe("active");
        (await _jobs.ListAsync()).ShouldHaveSingleItem();
    }

    // §8.1: a Queued row and an orchestration with instanceId = jobId; §7: the start rule, which manual ignores.
    [Theory]
    [InlineData("Failed", 41, ReindexTriggerKind.Reconciliation)]
    [InlineData("Failed", 42, ReindexTriggerKind.Manual)]
    public async Task GivenNoFinishedJobOrOneTheStartRuleAllows_WhenAJobIsRequested_ThenRowAndOrchestrationCarryTheResolvedPlan(
        string finishedStatus, long finishedTargetEventId, ReindexTriggerKind trigger)
    {
        await _jobs.CreateAsync(Job(finishedStatus, "older", CreateJobDefinition([PendingTarget], finishedTargetEventId)), CancellationToken.None);

        var job = await CreatedJobAsync(await HandleAsync(trigger));

        job.Status.ShouldBe("Queued");
        job.OrchestrationInstanceId.ShouldBe(job.JobId);
        job.CreateDate.ShouldBe(Now);
        job.Definition.TargetEventId.ShouldBe(42);
        job.Definition.TenantIds.ShouldBe([1]);
        job.Definition.ResourceTypes.ShouldBe(["Patient"]);
        job.Definition.SearchParameters.Select(parameter => parameter.Code).ShouldBe(["custom"]);
        job.Definition.Trigger.ShouldBe(trigger.ToString());
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
        var versions = new FhirVersionContext(NullLoggerFactory.Instance, new SearchParameterResolutionOptions(), NullFhirBaseUriProvider.Instance);
        var resolver = new ReindexTargetResolver(TenantStore(_tenants), versions, _state, _events);
        var options = Options.Create(new ReindexOptions { BarrierDelay = TimeSpan.Zero });
        return new CreateReindexJobHandler(new TaskHubClient(_runtime), _jobs, _jobLock, resolver, options, new FakeTimeProvider(Now));
    }
}
