using DurableTask.Core;
using Ignixa.Api.Services;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Tests;
using Ignixa.Application.Tests.BackgroundOperations.Reindex;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Medino;
using NSubstitute;
using Shouldly;
using Xunit;
using static Ignixa.Application.Tests.Features.Conformance.ConformanceEvents;

namespace Ignixa.Api.Tests.Services;

/// <summary>Spec §4.5 and §7: a sync renews the lease only after its refresh succeeds, then reconciles reindex.</summary>
public sealed class ConformanceStateSyncServiceTests : IDisposable
{
    private readonly ISourceEventStore _store = TestConformanceRefresher.EmptyEventStore();
    private readonly ConformanceState _state = new();
    private readonly ConformanceLease _lease = TestConformanceLease.NotHeld();

    [Fact]
    public async Task GivenRefreshFails_WhenSyncRuns_ThenItDoesNotRenewTheLease()
    {
        await _state.InitializeFromEventsAsync(_store, CancellationToken.None);
        _store.ReadFromAsync(0, Arg.Any<CancellationToken>()).Returns(TestConformanceRefresher.Stream(At(1, PackageActivated("test"))));
        using var service = CreateService(TestConformanceRefresher.FailingTenants(new InvalidOperationException("refresh failed")));

        await Should.ThrowAsync<InvalidOperationException>(() => service.RunSyncAsync());

        _lease.IsHeld.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenPendingParameterAfterTriggerFailure_WhenSyncRuns_ThenPeriodicReconciliationRequestsAJob()
    {
        _state.ApplyAndTrack(ReindexTestHelper.Activation(eventId: 10));
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<CreateReindexJobCommand>(), Arg.Any<CancellationToken>()).Returns(new ReindexJobCreatedResult("reconciled"));
        using var service = CreateService(trigger: CreateReindexTrigger(mediator));

        await service.RunSyncAsync();

        await mediator.Received(1).SendAsync(
            Arg.Is<CreateReindexJobCommand>(command => command.Trigger == ReindexTriggerKind.Reconciliation),
            CancellationToken.None);
    }

    [Fact]
    public async Task GivenReindexReconciliationFailsOperationally_WhenSyncRuns_ThenTheLeaseIsStillRenewed()
    {
        var jobs = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        jobs.GetActiveAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task<BackgroundJob<ReindexJobDefinition>?>>(_ => throw new IOException("Database unavailable."));
        using var service = CreateService(trigger: CreateReindexTrigger(Substitute.For<IMediator>(), jobs));

        await service.RunSyncAsync();

        _lease.IsHeld.ShouldBeTrue();
    }

    public void Dispose() => _state.Dispose();

    private TestSyncService CreateService(ITenantConfigurationStore? refreshTenants = null, ReindexTrigger? trigger = null) =>
        new(
            _state,
            TestConformanceRefresher.Create(_state, _store, refreshTenants),
            _lease,
            trigger ?? CreateReindexTrigger(Substitute.For<IMediator>(), autoStart: false));

    private ReindexTrigger CreateReindexTrigger(
        IMediator mediator,
        IBackgroundJobRepository<ReindexJobDefinition>? jobs = null,
        bool autoStart = true)
    {
        jobs ??= Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        var options = Options.Create(new ReindexOptions { AutoStart = autoStart });
        return new ReindexTrigger(
            mediator,
            jobs,
            _state,
            new ReindexJobReconciler(
                new TaskHubClient(Substitute.For<IOrchestrationServiceClient>()),
                jobs,
                new ReindexLifecycleEventWriter(Substitute.For<ISourceEventStore>(), _state),
                Substitute.For<IReindexJobLock>(),
                options,
                TimeProvider.System,
                NullLogger<ReindexJobReconciler>.Instance),
            ReindexTestHelper.CreateRepositoryFactory(),
            options,
            NullLogger<ReindexTrigger>.Instance);
    }

    private sealed class TestSyncService(
        ConformanceState state,
        ConformanceRefresher refresher,
        ConformanceLease lease,
        ReindexTrigger reindexTrigger)
        : ConformanceStateSyncService(
            state,
            refresher,
            lease,
            Options.Create(new ConformanceTransitionOptions()),
            reindexTrigger,
            NullLogger<ConformanceStateSyncService>.Instance)
    {
        public Task RunSyncAsync() => SyncAsync(CancellationToken.None);
    }
}
