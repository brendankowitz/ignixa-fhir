using Ignixa.Api.Services;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
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
        using var service = new TestInitializer(store, state);

        await service.RunAsync();

        state.IsInitialized.ShouldBeTrue();
        _ = store.Received(1).ReadAllAsync(Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<SourceEvent> EmptyEvents()
    {
        await Task.CompletedTask;
        yield break;
    }

    private sealed class TestInitializer(ISourceEventStore store, ConformanceState state)
        : ConformanceStateInitializerService(store, state, NullLogger<ConformanceStateInitializerService>.Instance)
    {
        public Task RunAsync() => ExecuteAsync(CancellationToken.None);
    }
}
