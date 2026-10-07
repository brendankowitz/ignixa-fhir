using Ignixa.Api.Services;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

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
        cacheRefresher.RefreshAsync(Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("refresh failed"));
        var lease = Substitute.For<IConformanceLease>();
        var leaseStart = new ConformanceLeaseStart(DateTimeOffset.UtcNow, 1);
        lease.CaptureStart().Returns(leaseStart);
        using var service = new TestSyncService(store, state, cacheRefresher, lease);

        await Should.ThrowAsync<InvalidOperationException>(() => service.RunSyncAsync());

        _ = lease.Received(1).CaptureStart();
        lease.DidNotReceive().Renew(Arg.Any<ConformanceLeaseStart>());
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

    private sealed class TestSyncService(
        ISourceEventStore store,
        ConformanceState state,
        IConformanceCacheRefresher cacheRefresher,
        IConformanceLease lease)
        : ConformanceStateSyncService(
            store,
            state,
            cacheRefresher,
            lease,
            NullLogger<ConformanceStateSyncService>.Instance,
            new ConfigurationBuilder().Build())
    {
        public Task RunSyncAsync() => SyncAsync(CancellationToken.None);
    }
}
