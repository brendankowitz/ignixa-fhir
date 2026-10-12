using Ignixa.Api.Services;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
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
        using var service = CreateInitializer(store, state, TestConformanceRefresher.Tenants(), CreateLease());

        await RunAsync(service);

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
        using var service = CreateInitializer(store, state, TestConformanceRefresher.Tenants(), lease);

        await RunAsync(service);

        lease.IsHeld.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenInitialLoad_WhenItSucceeds_ThenItRefreshesConsumersBeforeRenewingTheConformanceLease()
    {
        var store = Substitute.For<ISourceEventStore>();
        store.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(EmptyEvents());
        using var state = new ConformanceState();
        var lease = CreateLease();
        bool? heldDuringRefresh = null;
        var refreshTenants = Substitute.For<ITenantConfigurationStore>();
        refreshTenants.GetAllTenantsAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                heldDuringRefresh = lease.IsHeld;
                return new ValueTask<IReadOnlyList<TenantConfiguration>>([]);
            });
        using var service = CreateInitializer(store, state, refreshTenants, lease);

        await RunAsync(service);

        heldDuringRefresh.ShouldBe(false);
        lease.IsHeld.ShouldBeTrue();
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
        var time = new FakeTimeProvider();
        using var service = CreateInitializer(store, state, TestConformanceRefresher.Tenants(), CreateLease(), time);

        await service.StartAsync(CancellationToken.None);
        await AdvanceUntilCompletedAsync(time, service.ExecuteTask!);

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

    private static ConformanceLease CreateLease() =>
        new ConformanceLease(
            Options.Create(new ConformanceTransitionOptions { MaxStaleness = TimeSpan.FromMinutes(1) }),
            TimeProvider.System,
            NullLogger<ConformanceLease>.Instance);

    private static ConformanceStateInitializerService CreateInitializer(
        ISourceEventStore store,
        ConformanceState state,
        ITenantConfigurationStore refreshTenants,
        ConformanceLease lease,
        TimeProvider? timeProvider = null) =>
        new(
            store,
            state,
            TestConformanceRefresher.Create(state, tenants: refreshTenants),
            lease,
            timeProvider ?? new FakeTimeProvider(),
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
