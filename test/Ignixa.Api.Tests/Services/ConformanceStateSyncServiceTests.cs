using DurableTask.Core;
using Ignixa.Api.Services;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Reindex;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Medino;
using NSubstitute;
using Shouldly;
using Xunit;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Api.Tests.Services;

public class ConformanceStateSyncServiceTests
{
    [Fact]
    public async Task GivenRefreshFails_WhenSyncRuns_ThenItDoesNotRenewTheLease()
    {
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(EmptyEvents());
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(
            Events(new SourceEvent(
                1,
                "package:test@1.0.0",
                nameof(PackageActivated),
                new PackageActivated("test", "1.0.0", []),
                DateTimeOffset.UtcNow)));
        using var state = new ConformanceState();
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        var lease = TestConformanceLease.NotHeld();
        using var service = new TestSyncService(
            store,
            state,
            TestConformanceRefresher.FailingTenants(new InvalidOperationException("refresh failed")),
            lease);

        await Should.ThrowAsync<InvalidOperationException>(() => service.RunSyncAsync());

        lease.LeaseStartUtc.ShouldBeNull();
    }

    [Fact]
    public async Task GivenForcedRefreshFails_WhenNextSyncHasNoNewEvents_ThenItPublishesThePendingRefresh()
    {
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(EmptyEvents());
        using var state = new ConformanceState();
        state.ApplyAndTrack(new SourceEvent(
            1,
            "package:test@1",
            nameof(PackageActivated),
            new PackageActivated("test", "1", []),
            DateTimeOffset.UtcNow));
        var builds = 0;
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(_ => ++builds == 2
                ? ValueTask.FromException<IReadOnlyList<TenantConfiguration>>(new IOException("failed"))
                : new ValueTask<IReadOnlyList<TenantConfiguration>>([]));
        using var refresher = TestConformanceRefresher.Create(state, tenants: tenants);
        using var service = new TestSyncService(
            store,
            state,
            refresher,
            TestConformanceLease.NotHeld());

        await service.RunSyncAsync();
        await Should.ThrowAsync<ConformanceConsumerRefreshException>(() =>
            refresher.RefreshAsync(force: true, CancellationToken.None));
        await service.RunSyncAsync();

        builds.ShouldBe(3);
    }

    [Fact]
    public async Task GivenPendingParameterAfterTriggerFailure_WhenSyncRuns_ThenPeriodicReconciliationRequestsAJob()
    {
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(EmptyEvents());
        using var state = new ConformanceState();
        state.ApplyAndTrack(new SourceEvent(
            10,
            "package:custom@1.0.0",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                "http://example.org/SearchParameter/Patient-custom",
                "custom",
                "Patient",
                "Patient.name",
                SearchParamType.String,
                "custom@1.0.0",
                null,
                2,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow));
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(
                Arg.Any<CreateReindexJobCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(new ReindexJobCreatedResult("reconciled"));
        using var service = new TestSyncService(
            store,
            state,
            TestConformanceRefresher.Tenants(),
            TestConformanceLease.NotHeld(),
            CreateReindexTrigger(mediator, state, autoStart: true));

        await service.RunSyncAsync();

        await mediator.Received(1).SendAsync(
            Arg.Is<CreateReindexJobCommand>(command => command.Trigger == "Reconciliation"),
            CancellationToken.None);
    }

    [Fact]
    public async Task GivenReindexReconciliationFailsOperationally_WhenSyncRuns_ThenTheLeaseIsStillRenewed()
    {
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(EmptyEvents());
        using var state = new ConformanceState();
        var lease = TestConformanceLease.NotHeld();
        var jobs = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        jobs.GetActiveAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task<BackgroundJob<ReindexJobDefinition>?>>(_ => throw new IOException("Database unavailable."));
        using var service = new TestSyncService(
            store,
            state,
            TestConformanceRefresher.Tenants(),
            lease,
            CreateReindexTrigger(Substitute.For<IMediator>(), state, autoStart: true, jobs));

        await service.RunSyncAsync();

        lease.IsHeld.ShouldBeTrue();
    }

    private static async IAsyncEnumerable<SourceEvent> EmptyEvents()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<SourceEvent> Events(params SourceEvent[] events)
    {
        foreach (var sourceEvent in events)
        {
            yield return sourceEvent;
        }

        await Task.CompletedTask;
    }

    private static ReindexTrigger CreateReindexTrigger(
        IMediator mediator,
        ConformanceState state,
        bool autoStart,
        IBackgroundJobRepository<ReindexJobDefinition>? jobs = null)
    {
        jobs ??= Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        var availability = Substitute.For<IReindexAvailability>();
        availability.GetAvailabilityAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ReindexAvailability.Available));
        var options = Options.Create(new ReindexOptions { AutoStart = autoStart });
        return new ReindexTrigger(
            mediator,
            jobs,
            state,
            new ReindexJobReconciler(
                new TaskHubClient(Substitute.For<IOrchestrationServiceClient>()),
                jobs,
                new ReindexLifecycleEventWriter(Substitute.For<ISourceEventStore>(), state),
                Substitute.For<IReindexJobLock>(),
                options,
                TimeProvider.System,
                NullLogger<ReindexJobReconciler>.Instance),
            availability,
            options,
            NullLogger<ReindexTrigger>.Instance);
    }

    private sealed class TestSyncService : ConformanceStateSyncService
    {
        public TestSyncService(
            ISourceEventStore store,
            ConformanceState state,
            ITenantConfigurationStore refreshTenants,
            ConformanceLease lease,
            ReindexTrigger? reindexTrigger = null)
            : this(store, state, TestConformanceRefresher.Create(state, tenants: refreshTenants), lease, reindexTrigger)
        {
        }

        public TestSyncService(
            ISourceEventStore store,
            ConformanceState state,
            ConformanceRefresher refresher,
            ConformanceLease lease,
            ReindexTrigger? reindexTrigger = null)
            : base(
                store,
                state,
                refresher,
                lease,
                Options.Create(new ConformanceTransitionOptions()),
                reindexTrigger ?? CreateReindexTrigger(
                    Substitute.For<IMediator>(),
                    state,
                    autoStart: false),
                NullLogger<ConformanceStateSyncService>.Instance)
        {
        }

        public Task RunSyncAsync() => SyncAsync(CancellationToken.None);
    }
}
