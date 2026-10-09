using Ignixa.Api.Services;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Services;

public class ConformanceStateInitializerTests
{
    [Fact]
    public async Task GivenCompletedPreHostReplay_WhenHostedInitializerRuns_ThenItDoesNotReplayAgain()
    {
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(EmptyEvents());
        using var state = new ConformanceState();
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        using var service = new TestInitializer(store, state, CreateRefresher(), CreateLease());

        await service.RunAsync();

        state.IsInitialized.ShouldBeTrue();
        _ = store.Received(1).ReadAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenInitialLoad_WhenItSucceeds_ThenItRenewsTheConformanceLease()
    {
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(EmptyEvents());
        using var state = new ConformanceState();
        var lease = CreateLease();
        using var service = new TestInitializer(store, state, CreateRefresher(), lease);

        await service.RunAsync();

        lease.IsHeld.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenInitialLoad_WhenItSucceeds_ThenItRefreshesConsumersBeforeRenewingTheConformanceLease()
    {
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(EmptyEvents());
        using var state = new ConformanceState();
        var cacheRefresher = CreateRefresher();
        var lease = Substitute.For<IConformanceLease>();
        var leaseStart = new ConformanceLeaseStart(DateTimeOffset.UtcNow, 1);
        lease.CaptureStart().Returns(leaseStart);
        using var service = new TestInitializer(store, state, cacheRefresher, lease);

        await service.RunAsync();

        Received.InOrder(() =>
        {
            cacheRefresher.BuildSnapshotAsync(
                Arg.Any<ConformanceStateSnapshot>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>());
            lease.Renew(leaseStart);
        });
    }

    [Fact]
    public async Task GivenReplayFailsAfterApplyingAnEvent_WhenInitializationRetries_ThenItCatchesUpFromTheLastAppliedEvent()
    {
        var first = new SourceEvent(
            1,
            "package:first@1.0.0",
            nameof(PackageActivated),
            new PackageActivated("first", "1.0.0", []),
            DateTimeOffset.UtcNow);
        var second = new SourceEvent(
            2,
            "package:second@1.0.0",
            nameof(PackageActivated),
            new PackageActivated("second", "1.0.0", []),
            DateTimeOffset.UtcNow);
        var store = Substitute.For<ISourceEventStore>();
        var readAllCalls = 0;
        store.ReadAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => ++readAllCalls == 1
                ? EventsThenFailure(first)
                : Events(first, second));
        store.ReadFromAsync(1, Arg.Any<CancellationToken>()).Returns(Events(second));
        using var state = new ConformanceState();
        using var service = new TestInitializer(store, state, CreateRefresher(), CreateLease());

        await service.RunAsync();

        state.IsInitialized.ShouldBeTrue();
        state.LastProcessedEventId.ShouldBe(2);
        _ = store.Received(1).ReadAllAsync(Arg.Any<CancellationToken>());
        _ = store.Received(1).ReadFromAsync(1, Arg.Any<CancellationToken>());
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

    private static async IAsyncEnumerable<SourceEvent> EventsThenFailure(params SourceEvent[] events)
    {
        foreach (var sourceEvent in events)
        {
            yield return sourceEvent;
        }

        await Task.Yield();
        throw new IOException("event store failed");
    }

    private static IConformanceLease CreateLease() =>
        new ConformanceLease(
            Options.Create(new ConformanceTransitionOptions { MaxStaleness = TimeSpan.FromMinutes(1) }),
            TimeProvider.System,
            NullLogger<ConformanceLease>.Instance);

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

    private sealed class TestInitializer(
        ISourceEventStore store,
        ConformanceState state,
        IConformanceCacheRefresher cacheRefresher,
        IConformanceLease lease)
        : ConformanceStateInitializerService(
            store,
            state,
            new ConformanceRefreshPublisher(
                state,
                cacheRefresher,
                NullLogger<ConformanceRefreshPublisher>.Instance),
            lease,
            NullLogger<ConformanceStateInitializerService>.Instance)
    {
        public Task RunAsync() => ExecuteAsync(CancellationToken.None);

        protected override Task DelayBeforeRetryAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed record TestSnapshot(long Generation) : IConformanceConsumerSnapshot;
}
