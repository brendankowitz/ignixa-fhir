using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexJobReconcilerTests
{
    private const string Canonical = "http://example.org/SearchParameter/patient-custom";
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GivenRunningJobWithinOrphanGrace_WhenReconciled_ThenLockIsNotTaken()
    {
        var fixture = new Fixture();
        await fixture.Lifecycle.StartAsync("job", [fixture.Target], CancellationToken.None);
        var job = await fixture.CreateJobAsync("Running", lastObserved: Now - TimeSpan.FromMinutes(1));

        await fixture.Reconciler.ReconcileAsync(job, CancellationToken.None);

        fixture.JobLock.Acquisitions.ShouldBe(0);
        (await fixture.Repository.GetAsync("job", 1, CancellationToken.None))!.Status.ShouldBe("Running");
        fixture.State.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Reindexing);
    }

    [Fact]
    public async Task GivenOrchestrationStillRunning_WhenReconciled_ThenNothingChanges()
    {
        var fixture = new Fixture();
        await fixture.Lifecycle.StartAsync("job", [fixture.Target], CancellationToken.None);
        var job = await fixture.CreateJobAsync("Running", lastObserved: Now - TimeSpan.FromMinutes(10));
        fixture.Runtime.GetOrchestrationStateAsync("job", false)
            .Returns([State(OrchestrationStatus.Running)]);

        await fixture.Reconciler.ReconcileAsync(job, CancellationToken.None);

        (await fixture.Repository.GetAsync("job", 1, CancellationToken.None))!.Status.ShouldBe("Running");
        fixture.State.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Reindexing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenRunningJobPastGraceWithDeadOrchestration_WhenReconciled_ThenJobFailsAndParametersReturnToPending(
        bool orchestrationIsMissing)
    {
        var fixture = new Fixture();
        await fixture.Lifecycle.StartAsync("job", [fixture.Target], CancellationToken.None);
        var job = await fixture.CreateJobAsync("Running", lastObserved: Now - TimeSpan.FromMinutes(10));
        fixture.Runtime.GetOrchestrationStateAsync("job", false)
            .Returns(orchestrationIsMissing ? [] : [State(OrchestrationStatus.Failed)]);

        await fixture.Reconciler.ReconcileAsync(job, CancellationToken.None);

        var failed = (await fixture.Repository.GetAsync("job", 1, CancellationToken.None))!;
        failed.Status.ShouldBe("Failed");
        failed.EndDate.ShouldBe(Now);
        failed.ErrorMessage.ShouldContain(orchestrationIsMissing ? "missing" : "Failed");
        var parameter = fixture.State.GetSearchParameter("Patient", "custom")!;
        parameter.Status.ShouldBe(SearchParameterStatus.Pending);
        parameter.ReindexJobId.ShouldBeNull();
    }

    [Fact]
    public async Task GivenQueuedJobPastGraceWhoseOrchestrationWasNeverCreated_WhenReconciled_ThenRowIsDeleted()
    {
        var fixture = new Fixture();
        var job = await fixture.CreateJobAsync("Queued", lastObserved: Now - TimeSpan.FromMinutes(10));
        fixture.Runtime.GetOrchestrationStateAsync("job", false).Returns([]);

        await fixture.Reconciler.ReconcileAsync(job, CancellationToken.None);

        (await fixture.Repository.GetAsync("job", 1, CancellationToken.None)).ShouldBeNull();
        fixture.State.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Pending);
    }

    [Fact]
    public async Task GivenCompletionEventsAppendedBeforeFinalWriteWasLost_WhenReconciled_ThenJobIsCompletedExactlyOnce()
    {
        var fixture = new Fixture();
        await fixture.Lifecycle.StartAsync("job", [fixture.Target], CancellationToken.None);
        await fixture.Lifecycle.CompleteAsync(
            "job",
            [new ReindexTargetCompletion(fixture.Target, true, 12, TimeSpan.Zero, null)],
            CancellationToken.None);
        var job = await fixture.CreateJobAsync("Running", lastObserved: Now - TimeSpan.FromMinutes(10));
        fixture.Runtime.GetOrchestrationStateAsync("job", false)
            .Returns([State(OrchestrationStatus.Failed)]);
        var appendsBefore = fixture.Appends;

        await fixture.Reconciler.ReconcileAsync(job, CancellationToken.None);
        await fixture.Reconciler.ReconcileAsync(job, CancellationToken.None);

        var completed = (await fixture.Repository.GetAsync("job", 1, CancellationToken.None))!;
        completed.Status.ShouldBe("Completed");
        completed.Result!["success"]!.GetValue<bool>().ShouldBeTrue();
        fixture.Updates.ShouldBe(1);
        fixture.Appends.ShouldBe(appendsBefore);
        fixture.State.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Enabled);
    }

    [Fact]
    public async Task GivenFailureEventsAppendedBeforeFinalWriteWasLost_WhenReconciled_ThenJobFailsWithoutNewEvents()
    {
        var fixture = new Fixture();
        await fixture.Lifecycle.StartAsync("job", [fixture.Target], CancellationToken.None);
        await fixture.Lifecycle.CompleteAsync(
            "job",
            [new ReindexTargetCompletion(fixture.Target, false, 0, TimeSpan.Zero, "worker failed")],
            CancellationToken.None);
        var job = await fixture.CreateJobAsync("Running", lastObserved: Now - TimeSpan.FromMinutes(10));
        fixture.Runtime.GetOrchestrationStateAsync("job", false)
            .Returns([State(OrchestrationStatus.Failed)]);
        var appendsBefore = fixture.Appends;

        await fixture.Reconciler.ReconcileAsync(job, CancellationToken.None);

        (await fixture.Repository.GetAsync("job", 1, CancellationToken.None))!.Status.ShouldBe("Failed");
        fixture.Appends.ShouldBe(appendsBefore);
        fixture.State.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Pending);
    }

    [Fact]
    public async Task GivenJobFinishedBeforeLockWasAcquired_WhenReconciled_ThenTerminalStateIsLeftAlone()
    {
        var fixture = new Fixture();
        var job = await fixture.CreateJobAsync("Running", lastObserved: Now - TimeSpan.FromMinutes(10));
        var finished = (await fixture.Repository.GetAsync("job", 1, CancellationToken.None))!;
        finished.Status = "Completed";
        await fixture.Repository.UpdateAsync(finished, 1, CancellationToken.None);
        var updatesBefore = fixture.Updates;

        await fixture.Reconciler.ReconcileAsync(job, CancellationToken.None);

        fixture.Updates.ShouldBe(updatesBefore);
        (await fixture.Repository.GetAsync("job", 1, CancellationToken.None))!.Status.ShouldBe("Completed");
        await fixture.Runtime.DidNotReceive().GetOrchestrationStateAsync(Arg.Any<string>(), Arg.Any<bool>());
    }

    private static OrchestrationState State(OrchestrationStatus status) => new()
    {
        OrchestrationInstance = new OrchestrationInstance { InstanceId = "job" },
        OrchestrationStatus = status
    };

    private sealed class Fixture
    {
        private readonly InMemoryBackgroundJobRepository<ReindexJobDefinition> _store;

        public Fixture()
        {
            var tenants = Substitute.For<ITenantConfigurationStore>();
            tenants.Mode.Returns(TenantMode.Isolated);
            _store = new InMemoryBackgroundJobRepository<ReindexJobDefinition>(
                tenants,
                NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);
            Repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
            Repository.GetAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call => _store.GetAsync(call.Arg<string>(), call.ArgAt<int>(1), CancellationToken.None));
            Repository.CreateAsync(Arg.Any<BackgroundJob<ReindexJobDefinition>>(), Arg.Any<CancellationToken>())
                .Returns(call => _store.CreateAsync(call.Arg<BackgroundJob<ReindexJobDefinition>>(), CancellationToken.None));
            Repository.UpdateAsync(Arg.Any<BackgroundJob<ReindexJobDefinition>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Updates++;
                    return _store.UpdateAsync(call.Arg<BackgroundJob<ReindexJobDefinition>>(), call.ArgAt<int>(1), CancellationToken.None);
                });
            Repository.DeleteAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call => _store.DeleteAsync(call.Arg<string>(), call.ArgAt<int>(1), CancellationToken.None));
            State.ApplyAndTrack(new SourceEvent(
                1,
                "search",
                nameof(SearchParameterActivated),
                new SearchParameterActivated(
                    Canonical, "custom", "Patient", "Patient.id", SearchParamType.String,
                    "example@1.0.0", null, 17, null, null, null, null),
                DateTimeOffset.UtcNow));
            var eventStore = Substitute.For<ISourceEventStore>();
            eventStore.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(AsyncEnumerable.Empty<SourceEvent>());
            long nextEventId = 2;
            eventStore.AppendAsync(
                    Arg.Any<IEnumerable<NewSourceEvent>>(),
                    Arg.Any<long>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Appends++;
                    return call.Arg<IEnumerable<NewSourceEvent>>()
                        .Select(evt => new SourceEvent(
                            nextEventId++, evt.StreamId, evt.EventType, evt.Data, DateTimeOffset.UtcNow))
                        .ToArray();
                });
            Lifecycle = new ReindexLifecycleEventWriter(eventStore, State);
            Reconciler = new ReindexJobReconciler(
                new TaskHubClient(Runtime),
                Repository,
                Lifecycle,
                JobLock,
                Options.Create(new ReindexOptions { OrphanGrace = TimeSpan.FromMinutes(2) }),
                new FixedTimeProvider(Now),
                NullLogger<ReindexJobReconciler>.Instance);
        }

        public IBackgroundJobRepository<ReindexJobDefinition> Repository { get; }
        public IOrchestrationServiceClient Runtime { get; } = Substitute.For<IOrchestrationServiceClient>();
        public CountingJobLock JobLock { get; } = new();
        public ConformanceState State { get; } = new();
        public ReindexLifecycleEventWriter Lifecycle { get; }
        public ReindexJobReconciler Reconciler { get; }
        public ReindexParameterDefinition Target { get; } = new(Canonical, "custom", "Patient", 17, 1, ["Patient"]);
        public int Updates { get; private set; }
        public int Appends { get; private set; }

        public async Task<BackgroundJob<ReindexJobDefinition>> CreateJobAsync(string status, DateTimeOffset lastObserved)
        {
            var job = new BackgroundJob<ReindexJobDefinition>
            {
                JobId = "job",
                OrchestrationInstanceId = "job",
                JobType = (int)BackgroundJobType.Reindex,
                Status = status,
                Definition = new ReindexJobDefinition
                {
                    TargetEventId = 1,
                    TenantIds = [1],
                    ResourceTypes = ["Patient"],
                    SearchParameters = [Target],
                    MaximumNumberOfResourcesPerQuery = 10_000,
                    MaximumNumberOfResourcesPerWrite = 1_000,
                    MaximumConcurrency = 4,
                    QueryDelayIntervalInMilliseconds = 0,
                    Trigger = "Manual"
                },
                CreateDate = lastObserved,
                HeartbeatDate = lastObserved
            };
            await Repository.CreateAsync(job, CancellationToken.None);
            return (await Repository.GetAsync("job", 1, CancellationToken.None))!;
        }
    }

    private sealed class CountingJobLock : IReindexJobLock
    {
        public int Acquisitions { get; private set; }

        public Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
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
