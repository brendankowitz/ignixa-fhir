using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Conformance;

public class SearchParameterTransitionReconcilerTests
{
    [Fact]
    public async Task GivenTransitionWithClockSkewedHideTimestamp_WhenReconciled_ThenItSchedulesFullGrace()
    {
        using var state = CreateState(DateTimeOffset.UtcNow.AddDays(1));
        var scheduler = Substitute.For<ISearchParameterTransitionScheduler>();
        var grace = TimeSpan.FromMinutes(3);
        var reconciler = new SearchParameterTransitionReconciler(
            state,
            scheduler,
            Options.Create(new ConformanceTransitionOptions { TransitionGrace = grace }));

        await reconciler.ReconcileAsync(CancellationToken.None);

        await scheduler.Received(1).ScheduleAsync(20, grace, CancellationToken.None);
    }

    [Fact]
    public async Task GivenTransitionWithoutPersistedTimestamp_WhenReconciled_ThenItSchedulesFullGrace()
    {
        using var state = CreateState(DateTimeOffset.UtcNow.AddDays(-1));
        var scheduler = Substitute.For<ISearchParameterTransitionScheduler>();
        var grace = TimeSpan.FromMinutes(3);
        var reconciler = new SearchParameterTransitionReconciler(
            state,
            scheduler,
            Options.Create(new ConformanceTransitionOptions { TransitionGrace = grace }));

        await reconciler.ReconcileAsync(CancellationToken.None);

        await scheduler.Received(1).ScheduleAsync(20, grace, CancellationToken.None);
    }

    [Fact]
    public async Task GivenAnActivationHoldsTheProjectionLock_WhenReconciled_ThenItReadsTransitionsOnlyAfterTheLockIsReleased()
    {
        using var state = CreateState(DateTimeOffset.UtcNow);
        var scheduler = Substitute.For<ISearchParameterTransitionScheduler>();
        var reconciler = new SearchParameterTransitionReconciler(
            state,
            scheduler,
            Options.Create(new ConformanceTransitionOptions { TransitionGrace = TimeSpan.FromMinutes(3) }));

        Task reconciliation;
        using (await state.AcquireActivationLockAsync(CancellationToken.None))
        {
            reconciliation = reconciler.ReconcileAsync(CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            scheduler.ReceivedCalls().ShouldBeEmpty();
        }

        await reconciliation.WaitAsync(TimeSpan.FromSeconds(10));
        await scheduler.Received(1).ScheduleAsync(20, TimeSpan.FromMinutes(3), CancellationToken.None);
    }

    private static ConformanceState CreateState(DateTimeOffset hideTimestamp)
    {
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(10, "http://hl7.org/fhir/SearchParameter/Patient-identifier", null, hideTimestamp));
        state.ApplyAndTrack(Activation(20, "http://example.org/SearchParameter/Patient-identifier", "http://hl7.org/fhir/SearchParameter/Patient-identifier", hideTimestamp));
        return state;
    }

    private static SourceEvent Activation(long eventId, string canonical, string? overrides, DateTimeOffset timestamp) =>
        new(
            eventId,
            "transition-test",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                canonical,
                "identifier",
                "Patient",
                "Patient.identifier",
                SearchParamType.Token,
                "custom.package@1.0",
                overrides is null ? null : new OverrideInfo(overrides, 1),
                1,
                null,
                null,
                null,
                null),
            timestamp);
}
