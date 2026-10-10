using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Specification.ValueSets.Normative;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public sealed class ReindexTriggerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GivenAutoStartIsFalse_WhenActivationTriggers_ThenRequestIsNotSent()
    {
        var fixture = new Fixture(autoStart: false);

        var result = await fixture.Trigger.RequestReindexAsync("activation", CancellationToken.None);

        result.JobId.ShouldBeNull();
        result.Message.ShouldContain("disabled");
        await fixture.Mediator.DidNotReceive().SendAsync(
            Arg.Any<CreateReindexJobCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenReindexIsDisabled_WhenActivationTriggers_ThenPendingStateIsExplicit()
    {
        var fixture = new Fixture(enabled: false);

        var result = await fixture.Trigger.RequestReindexAsync("activation", CancellationToken.None);

        result.JobId.ShouldBeNull();
        result.Message.ShouldContain("disabled");
        result.Message.ShouldContain("Pending");
        await fixture.Mediator.DidNotReceive().SendAsync(
            Arg.Any<CreateReindexJobCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenProviderIsUnavailable_WhenActivationTriggers_ThenPendingStateIsExplicit()
    {
        var fixture = new Fixture(tenants:
        [
            ReindexTestHelper.Tenant(1, "SqlServer"),
            ReindexTestHelper.Tenant(3, "FileSystem")
        ]);

        var result = await fixture.Trigger.RequestReindexAsync("activation", CancellationToken.None);

        result.JobId.ShouldBeNull();
        result.Message.ShouldContain("tenant 3");
        result.Message.ShouldContain("Pending");
    }

    [Fact]
    public async Task GivenJobIsCreated_WhenActivationTriggers_ThenJobIdIsReturned()
    {
        var fixture = new Fixture();
        fixture.Mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ReindexJobCreatedResult("job-1"));

        var result = await fixture.Trigger.RequestReindexAsync("activation", CancellationToken.None);

        result.ShouldBe(new ReindexTriggerResult("job-1", false, null));
        await fixture.Mediator.Received(1).SendAsync(
            Arg.Is<CreateReindexJobCommand>(command => command.Trigger == ReindexTriggerKind.Activation),
            CancellationToken.None);
    }

    [Fact]
    public async Task GivenJobIsActive_WhenActivationTriggers_ThenTheActiveJobIsReported()
    {
        var fixture = new Fixture();
        fixture.Mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ActiveReindexJobResult("active-job"));

        var result = await fixture.Trigger.RequestReindexAsync("activation", CancellationToken.None);

        result.ShouldBe(new ReindexTriggerResult("active-job", true, null));
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(TimeoutException))]
    public async Task GivenOperationalFailure_WhenActivationTriggers_ThenResultIsDeferred(Type failure)
    {
        var fixture = new Fixture();
        fixture.Mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<CreateReindexJobResult>>(_ =>
                throw (Exception)Activator.CreateInstance(failure, "Storage unavailable.")!);

        var result = await fixture.Trigger.RequestReindexAsync("activation", CancellationToken.None);

        result.Deferred.ShouldBeTrue();
        result.JobId.ShouldBeNull();
        result.Message.ShouldContain("periodic reconciliation");
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(NullReferenceException))]
    public async Task GivenProgrammerError_WhenActivationTriggers_ThenItPropagates(Type failure)
    {
        var fixture = new Fixture();
        fixture.Mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<CreateReindexJobResult>>(_ =>
                throw (Exception)Activator.CreateInstance(failure, "Bug in the trigger.")!);

        var exception = await Should.ThrowAsync<Exception>(() =>
            fixture.Trigger.RequestReindexAsync("activation", CancellationToken.None));

        exception.ShouldBeOfType(failure);
    }

    [Fact]
    public async Task GivenCancellation_WhenActivationTriggers_ThenCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var fixture = new Fixture();
        fixture.Mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<CreateReindexJobResult>>(_ => throw new OperationCanceledException(cancellation.Token));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            fixture.Trigger.RequestReindexAsync("activation", cancellation.Token));
    }

    [Fact]
    public async Task GivenUnsupportedResult_WhenActivationTriggers_ThenItFailsFast()
    {
        var fixture = new Fixture();
        fixture.Mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>())
            .Returns(new UnsupportedReindexResult());

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            fixture.Trigger.RequestReindexAsync("activation", CancellationToken.None));

        exception.Message.ShouldContain(nameof(UnsupportedReindexResult));
    }

    [Fact]
    public async Task GivenIdleServer_WhenTickRuns_ThenNeitherLockNorRequestHappens()
    {
        var fixture = new Fixture(withPendingParameter: false);

        await fixture.Trigger.ReconcileAsync(CancellationToken.None);

        fixture.JobLock.Acquisitions.ShouldBe(0);
        await fixture.Mediator.DidNotReceive().SendAsync(
            Arg.Any<CreateReindexJobCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    [InlineData("Completed")]
    public async Task GivenFinishedJobCoversPendingParameters_WhenTickRuns_ThenNeitherLockNorRequestHappens(string status)
    {
        var fixture = new Fixture();
        await fixture.CreateJobAsync("finished", status, targetEventId: 42);

        await fixture.Trigger.ReconcileAsync(CancellationToken.None);

        fixture.JobLock.Acquisitions.ShouldBe(0);
        await fixture.Mediator.DidNotReceive().SendAsync(
            Arg.Any<CreateReindexJobCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenActivationNewerThanFailedJob_WhenTickRuns_ThenReconciliationJobIsRequested()
    {
        var fixture = new Fixture();
        await fixture.CreateJobAsync("failed", "Failed", targetEventId: 41);

        await fixture.Trigger.ReconcileAsync(CancellationToken.None);

        await fixture.Mediator.Received(1).SendAsync(
            Arg.Is<CreateReindexJobCommand>(command => command.Trigger == ReindexTriggerKind.Reconciliation),
            CancellationToken.None);
    }

    [Fact]
    public async Task GivenActivationArrivesWhileJobRuns_WhenJobFinishes_ThenFollowUpIsRequestedOnTheNextTick()
    {
        var fixture = new Fixture();
        await fixture.CreateJobAsync("running", "Running", targetEventId: 41);

        await fixture.Trigger.ReconcileAsync(CancellationToken.None);
        await fixture.Mediator.DidNotReceive().SendAsync(
            Arg.Any<CreateReindexJobCommand>(),
            Arg.Any<CancellationToken>());

        var running = (await fixture.Repository.GetAsync("running", 1, CancellationToken.None))!;
        running.Status = "Completed";
        await fixture.Repository.UpdateAsync(running, 1, CancellationToken.None);
        await fixture.Trigger.ReconcileAsync(CancellationToken.None);

        await fixture.Mediator.Received(1).SendAsync(
            Arg.Is<CreateReindexJobCommand>(command => command.Trigger == ReindexTriggerKind.Reconciliation),
            CancellationToken.None);
    }

    [Fact]
    public async Task GivenAutoStartIsFalse_WhenTickRuns_ThenNoJobIsRequested()
    {
        var fixture = new Fixture(autoStart: false);

        await fixture.Trigger.ReconcileAsync(CancellationToken.None);

        await fixture.Mediator.DidNotReceive().SendAsync(
            Arg.Any<CreateReindexJobCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAutoStartIsFalse_WhenTickFindsOrphanedJob_ThenItIsStillRecovered()
    {
        var fixture = new Fixture(autoStart: false);
        await fixture.CreateJobAsync("orphan", "Running", targetEventId: 42, lastObserved: Now - TimeSpan.FromMinutes(10));
        fixture.Runtime.GetOrchestrationStateAsync("orphan", false).Returns([]);

        await fixture.Trigger.ReconcileAsync(CancellationToken.None);

        (await fixture.Repository.GetAsync("orphan", 1, CancellationToken.None))!.Status.ShouldBe("Failed");
    }

    [Fact]
    public async Task GivenActivationLockIsHeld_WhenTickRuns_ThenPendingParametersAreReadOnlyAfterItIsReleased()
    {
        var fixture = new Fixture();
        var held = await fixture.State.AcquireActivationLockAsync(CancellationToken.None);
        var tick = fixture.Trigger.ReconcileAsync(CancellationToken.None);
        await Task.Yield();

        tick.IsCompleted.ShouldBeFalse();
        await fixture.Mediator.DidNotReceive().SendAsync(
            Arg.Any<CreateReindexJobCommand>(),
            Arg.Any<CancellationToken>());
        held.Dispose();
        await tick;

        await fixture.Mediator.Received(1).SendAsync(
            Arg.Is<CreateReindexJobCommand>(command => command.Trigger == ReindexTriggerKind.Reconciliation),
            CancellationToken.None);
    }

    [Fact]
    public async Task GivenOperationalFailure_WhenTickRuns_ThenItIsDeferredToTheNextTick()
    {
        var fixture = new Fixture();
        fixture.Mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<CreateReindexJobResult>>(_ => throw new IOException("Database unavailable."));

        await fixture.Trigger.ReconcileAsync(CancellationToken.None);
    }

    [Fact]
    public async Task GivenProgrammerError_WhenTickRuns_ThenItPropagates()
    {
        var fixture = new Fixture();
        fixture.Mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>())
            .Returns<Task<CreateReindexJobResult>>(_ => throw new InvalidOperationException("Bug."));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            fixture.Trigger.ReconcileAsync(CancellationToken.None));
    }

    private sealed record UnsupportedReindexResult : CreateReindexJobResult;

    private sealed class Fixture
    {
        public Fixture(
            bool autoStart = true,
            bool withPendingParameter = true,
            bool enabled = true,
            TenantConfiguration[]? tenants = null)
        {
            var jobTenants = Substitute.For<ITenantConfigurationStore>();
            jobTenants.Mode.Returns(TenantMode.Isolated);
            Repository = new InMemoryBackgroundJobRepository<ReindexJobDefinition>(
                jobTenants,
                NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);
            if (withPendingParameter)
            {
                State.ApplyAndTrack(new SourceEvent(
                    42,
                    "search",
                    nameof(SearchParameterActivated),
                    new SearchParameterActivated(
                        "http://example.org/SearchParameter/patient-custom", "custom", "Patient", "Patient.id",
                        SearchParamType.String, "example@1.0.0", null, 17, null, null, null, null),
                    DateTimeOffset.UtcNow));
            }

            Mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>())
                .Returns(new NoReindexWorkResult("No resources need reindexing."));
            var eventStore = Substitute.For<ISourceEventStore>();
            eventStore.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(AsyncEnumerable.Empty<SourceEvent>());
            var options = Options.Create(new ReindexOptions
            {
                Enabled = enabled,
                AutoStart = autoStart,
                OrphanGrace = TimeSpan.FromMinutes(2)
            });
            Trigger = new ReindexTrigger(
                Mediator,
                Repository,
                State,
                new ReindexJobReconciler(
                    new TaskHubClient(Runtime),
                    Repository,
                    new ReindexLifecycleEventWriter(eventStore, State),
                    JobLock,
                    options,
                    new FixedTimeProvider(Now),
                    NullLogger<ReindexJobReconciler>.Instance),
                ReindexTestHelper.CreateRepositoryFactory(tenants ?? []),
                options,
                NullLogger<ReindexTrigger>.Instance);
        }

        public ReindexTrigger Trigger { get; }
        public IMediator Mediator { get; } = Substitute.For<IMediator>();
        public IOrchestrationServiceClient Runtime { get; } = Substitute.For<IOrchestrationServiceClient>();
        public InMemoryBackgroundJobRepository<ReindexJobDefinition> Repository { get; }
        public ConformanceState State { get; } = new();
        public CountingJobLock JobLock { get; } = new();

        public Task CreateJobAsync(
            string jobId,
            string status,
            long targetEventId,
            DateTimeOffset? lastObserved = null) =>
            Repository.CreateAsync(new BackgroundJob<ReindexJobDefinition>
            {
                JobId = jobId,
                OrchestrationInstanceId = jobId,
                JobType = (int)BackgroundJobType.Reindex,
                Status = status,
                Definition = new ReindexJobDefinition
                {
                    TargetEventId = targetEventId,
                    TenantIds = [1],
                    ResourceTypes = ["Patient"],
                    SearchParameters = [],
                    MaximumNumberOfResourcesPerQuery = 10_000,
                    MaximumNumberOfResourcesPerWrite = 1_000,
                    MaximumConcurrency = 4,
                    QueryDelayIntervalInMilliseconds = 0,
                    Trigger = "Manual"
                },
                CreateDate = lastObserved ?? Now,
                HeartbeatDate = lastObserved ?? Now,
                EndDate = status is "Completed" or "Failed" or "Cancelled" ? Now : null
            }, CancellationToken.None);
    }

    private sealed class CountingJobLock : IReindexJobLock
    {
        public int Acquisitions { get; private set; }

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            Acquisitions++;
            return action(cancellationToken);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
