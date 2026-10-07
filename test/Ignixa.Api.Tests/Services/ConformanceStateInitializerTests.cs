using Ignixa.Api.Services;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
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
        using var service = new TestInitializer(store, state, CreateLease());

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
        using var service = new TestInitializer(store, state, lease);

        await service.RunAsync();

        lease.IsHeld.ShouldBeTrue();
    }

    private static async IAsyncEnumerable<SourceEvent> EmptyEvents()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static IConformanceLease CreateLease() =>
        new ConformanceLease(
            Options.Create(new ConformanceTransitionOptions { MaxStaleness = TimeSpan.FromMinutes(1) }),
            TimeProvider.System,
            NullLogger<ConformanceLease>.Instance);

    private sealed class TestInitializer(ISourceEventStore store, ConformanceState state, IConformanceLease lease)
        : ConformanceStateInitializerService(
            store,
            state,
            lease,
            NullLogger<ConformanceStateInitializerService>.Instance)
    {
        public Task RunAsync() => ExecuteAsync(CancellationToken.None);
    }
}
