using Ignixa.Api.Services;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Tests;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;
using Xunit;
using static Ignixa.Application.Tests.Features.Conformance.ConformanceEvents;

namespace Ignixa.Api.Tests.Services;

/// <summary>Spec §4.5: the initial state load counts as a successful sync, so it refreshes consumers, then renews the lease.</summary>
public class ConformanceStateInitializerTests
{
    private readonly ConformanceLease _lease = TestConformanceLease.NotHeld();

    [Fact]
    public async Task GivenCompletedPreHostReplay_WhenHostedInitializerRuns_ThenItDoesNotReplayAgain()
    {
        var store = TestConformanceRefresher.EmptyEventStore();
        using var state = new ConformanceState();
        await state.InitializeFromEventsAsync(store, CancellationToken.None);
        using var service = CreateInitializer(store, state);

        await RunAsync(service);

        state.IsInitialized.ShouldBeTrue();
        _ = store.Received(1).ReadAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenInitialLoad_WhenItSucceeds_ThenItRefreshesConsumersBeforeRenewingTheConformanceLease()
    {
        using var state = new ConformanceState();
        bool? heldDuringRefresh = null;
        var refreshTenants = Substitute.For<ITenantConfigurationStore>();
        refreshTenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                heldDuringRefresh = _lease.IsHeld;
                return new ValueTask<IReadOnlyList<TenantConfiguration>>([]);
            });
        using var service = CreateInitializer(TestConformanceRefresher.EmptyEventStore(), state, refreshTenants);

        await RunAsync(service);

        heldDuringRefresh.ShouldBe(false);
        _lease.IsHeld.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenReplayFailsAfterApplyingAnEvent_WhenInitializationRetries_ThenItCatchesUpFromTheLastAppliedEvent()
    {
        var first = At(1, PackageActivated("first"));
        var second = At(2, PackageActivated("second"));
        var store = Substitute.For<ISourceEventStore>();
        var readAllCalls = 0;
        store.ReadAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ => ++readAllCalls == 1 ? EventsThenFailure(first) : TestConformanceRefresher.Stream(first, second));
        store.ReadFromAsync(1, Arg.Any<CancellationToken>()).Returns(TestConformanceRefresher.Stream(second));
        using var state = new ConformanceState();
        var time = new FakeTimeProvider();
        using var service = CreateInitializer(store, state, time: time);

        await service.StartAsync(CancellationToken.None);
        await AdvanceUntilCompletedAsync(time, service.ExecuteTask!);

        state.IsInitialized.ShouldBeTrue();
        state.LastProcessedEventId.ShouldBe(2);
        _ = store.Received(1).ReadAllAsync(Arg.Any<CancellationToken>());
        _ = store.Received(1).ReadFromAsync(1, Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<SourceEvent> EventsThenFailure(SourceEvent sourceEvent)
    {
        yield return sourceEvent;
        await Task.Yield();
        throw new IOException("event store failed");
    }

    private ConformanceStateInitializerService CreateInitializer(
        ISourceEventStore store,
        ConformanceState state,
        ITenantConfigurationStore? refreshTenants = null,
        TimeProvider? time = null) =>
        new(
            store,
            state,
            TestConformanceRefresher.Create(state, tenants: refreshTenants),
            _lease,
            time ?? new FakeTimeProvider(),
            NullLogger<ConformanceStateInitializerService>.Instance);

    private static async Task RunAsync(ConformanceStateInitializerService service)
    {
        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;
    }

    // The retry delay is scheduled asynchronously after the failed attempt, so keep advancing past it.
    private static async Task AdvanceUntilCompletedAsync(FakeTimeProvider time, Task execution)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!execution.IsCompleted && DateTime.UtcNow < deadline)
        {
            time.Advance(TimeSpan.FromSeconds(5));
            await Task.Delay(10);
        }

        execution.IsCompleted.ShouldBeTrue("initialization did not finish after its retry delays elapsed");
        await execution;
    }
}
