using Ignixa.Api.Services;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        var cacheRefresher = Substitute.For<IConformanceCacheRefresher>();
        cacheRefresher.BuildSnapshotAsync(
                Arg.Any<ConformanceStateSnapshot>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IConformanceConsumerSnapshot>(
                new InvalidOperationException("refresh failed")));
        var lease = Substitute.For<IConformanceLease>();
        var leaseStart = new ConformanceLeaseStart(DateTimeOffset.UtcNow, 1);
        lease.CaptureStart().Returns(leaseStart);
        using var service = new TestSyncService(
            store,
            state,
            cacheRefresher,
            lease,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            TimeProvider.System,
            TimeSpan.FromMinutes(3));

        await Should.ThrowAsync<InvalidOperationException>(() => service.RunSyncAsync());

        _ = lease.Received(1).CaptureStart();
        lease.DidNotReceive().Renew(Arg.Any<ConformanceLeaseStart>());
    }

    [Fact]
    public async Task GivenUncommittedTransitionObservedPastTwiceGrace_WhenSyncRuns_ThenTheWatchdogSchedulesAFullGraceReconciliation()
    {
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(EmptyEvents());
        using var state = CreateUncommittedTransitionState();
        var scheduler = Substitute.For<ISearchParameterTransitionScheduler>();
        var clock = new ManualTimeProvider();
        var grace = TimeSpan.FromMinutes(3);
        using var service = new TestSyncService(
            store,
            state,
            CreateRefresher(),
            Substitute.For<IConformanceLease>(),
            scheduler,
            clock,
            grace);

        await service.RunSyncAsync();
        await scheduler.DidNotReceive().ScheduleReconciliationAsync(
            Arg.Any<long>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());

        clock.Advance(grace + grace);
        await service.RunSyncAsync();

        await scheduler.Received(1).ScheduleReconciliationAsync(
            20,
            grace,
            CancellationToken.None);
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

    private static ConformanceState CreateUncommittedTransitionState()
    {
        var state = new ConformanceState();
        state.ApplyAndTrack(new SourceEvent(
            10,
            "package:hl7.fhir.r4.core@4.0.1",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                "http://hl7.org/fhir/SearchParameter/Patient-identifier",
                "identifier",
                "Patient",
                "Patient.identifier",
                SearchParamType.Token,
                "hl7.fhir.r4.core@4.0.1",
                null,
                1,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow));
        state.ApplyAndTrack(new SourceEvent(
            20,
            "package:custom@1.0.0",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                "http://example.org/SearchParameter/Patient-identifier",
                "identifier",
                "Patient",
                "Patient.identifier",
                SearchParamType.Token,
                "custom@1.0.0",
                new OverrideInfo("http://hl7.org/fhir/SearchParameter/Patient-identifier", 1),
                1,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow));
        return state;
    }

    private static IConformanceCacheRefresher CreateRefresher()
    {
        var refresher = Substitute.For<IConformanceCacheRefresher>();
        refresher.BuildSnapshotAsync(
                Arg.Any<ConformanceStateSnapshot>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<IConformanceConsumerSnapshot>(
                new TestSnapshot(callInfo.ArgAt<long>(1))));
        return refresher;
    }

    private sealed class TestSyncService(
        ISourceEventStore store,
        ConformanceState state,
        IConformanceCacheRefresher cacheRefresher,
        IConformanceLease lease,
        ISearchParameterTransitionScheduler transitionScheduler,
        TimeProvider timeProvider,
        TimeSpan transitionGrace)
        : ConformanceStateSyncService(
            store,
            state,
            new ConformanceRefreshPublisher(
                state,
                cacheRefresher,
                NullLogger<ConformanceRefreshPublisher>.Instance),
            lease,
            transitionScheduler,
            Options.Create(new ConformanceTransitionOptions { TransitionGrace = transitionGrace }),
            timeProvider,
            NullLogger<ConformanceStateSyncService>.Instance,
            new ConfigurationBuilder().Build())
    {
        public Task RunSyncAsync() => SyncAsync(CancellationToken.None);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
    }

    private sealed record TestSnapshot(long Generation) : IConformanceConsumerSnapshot;
}
