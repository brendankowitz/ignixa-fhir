using System.Text.Json;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Shouldly;
using static Ignixa.Application.Tests.Features.Conformance.ConformanceEvents;
using static Ignixa.Conformance.Events.Models.SearchParameterStatus;

namespace Ignixa.Application.Tests.Features.Conformance;

/// <summary>Spec §4.1–§4.4: lifecycle states, guarded reindex events and two-phase transitions.</summary>
public class ConformanceLifecycleTests
{
    private const string BaseCanonical = TestPackages.BaseIdentifier;
    private const string OverrideCanonical = "http://example.org/SearchParameter/Patient-identifier";
    private const string MiddleCanonical = "http://example.org/SearchParameter/Patient-identifier-middle";
    private const string TopCanonical = "http://example.org/SearchParameter/Patient-identifier-top";
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // TransitionCommitted guard: it must name both the staging and the hiding event.
    [Theory]
    [InlineData(19, 19)]
    [InlineData(20, 19)]
    [InlineData(19, 20)]
    public void GivenStagedOverride_WhenTransitionDoesNotMatchBothHideEvents_ThenNothingChanges(
        long activationEventId,
        long deactivationEventId)
    {
        using var state = CreateStateWithStagedOverride();

        state.Apply(At(30, new SearchParameterTransitionCommitted(7, [activationEventId], [deactivationEventId])));

        Statuses(state, BaseCanonical, OverrideCanonical).ShouldBe([Disabling, Staged]);
        Owner(state).ShouldBe(BaseCanonical);
    }

    [Fact]
    public void GivenNewerStagedOverride_WhenOlderTransitionIsCommitted_ThenOlderOverrideIsNotPromoted()
    {
        const string newerCanonical = "http://example.org/SearchParameter/Patient-identifier-newer";
        using var state = CreateStateWithStagedOverride();
        state.Apply(Override(30, newerCanonical));

        state.Apply(Commit(40, 20));

        Statuses(state, OverrideCanonical, newerCanonical, BaseCanonical).ShouldBe([Disabled, Staged, Disabling]);
        Owner(state).ShouldBe(BaseCanonical);
    }

    // Removing an override is itself two-phase: the base is restaged by the new hide event, and the outgoing owner
    // keeps extraction until that event's transition commits.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void GivenAnOverride_WhenItOrItsPackageIsDeactivatedAndCommitted_ThenTheBaseIsRestagedThenRestoredAsPending(
        bool overrideEnabled,
        bool packageDeactivated)
    {
        using var state = overrideEnabled ? CreateStateWithEnabledOverride() : CreateStateWithStagedOverride();

        state.Apply(packageDeactivated ? At(40, PackageDeactivated("custom.package")) : Hide(40, OverrideCanonical));

        (state.FindByCanonical(BaseCanonical)!.Status, state.FindByCanonical(BaseCanonical)!.ActivationEventId).ShouldBe((Staged, 40L));
        Owner(state).ShouldBe(overrideEnabled ? OverrideCanonical : BaseCanonical);

        state.Apply(Commit(50, 40));

        Statuses(state, OverrideCanonical, BaseCanonical).ShouldBe([Disabled, Pending]);
        Owner(state).ShouldBe(BaseCanonical);
    }

    [Fact]
    public void GivenThreeLevelOverride_WhenLowerPackageIsDeactivatedBeforeHigherDefinitions_ThenItIsNeverRestored()
    {
        using var state = CreateStateWithEnabledThreeLevelOverride();

        state.Apply(At(50, PackageDeactivated("hl7.fhir.r4.core", "4.0.1")));
        state.Apply(Hide(60, TopCanonical));
        state.Apply(Commit(61, 60));
        state.Apply(Started(62, 60, "middle-job", MiddleCanonical));
        state.Apply(Completed(63, 60, "middle-job", MiddleCanonical));
        state.Apply(Hide(70, MiddleCanonical));

        Statuses(state, MiddleCanonical, BaseCanonical).ShouldBe([Disabling, Disabled]);
        Owner(state).ShouldBe(MiddleCanonical);
    }

    // Removing the top of base <- middle <- top restores the highest definition still installed.
    [Theory]
    [InlineData(false, MiddleCanonical, 50)]
    [InlineData(true, BaseCanonical, 60)]
    public void GivenThreeLevelOverride_WhenTheTopIsRemoved_ThenTheHighestRemainingDefinitionIsRestaged(
        bool middlePackageRemovedFirst,
        string expectedRestored,
        long expectedActivationEventId)
    {
        using var state = CreateStateWithEnabledThreeLevelOverride();

        if (middlePackageRemovedFirst)
        {
            state.Apply(At(50, PackageDeactivated("middle.package")));
            state.Apply(Hide(60, TopCanonical));
        }
        else
        {
            state.Apply(At(50, PackageDeactivated("top.package")));
        }

        Statuses(state, TopCanonical, expectedRestored).ShouldBe([Disabling, Staged]);
        state.FindByCanonical(expectedRestored)!.ActivationEventId.ShouldBe(expectedActivationEventId);
    }

    // Disabling -> Disabled: a repeated deactivation keeps the original hide event, which the commit must name.
    [Fact]
    public void GivenPlainParameter_WhenDeactivatedTwiceAndCommitted_ThenItMovesThroughDisablingToDisabledOnTheFirstHideEvent()
    {
        using var state = new ConformanceState();
        state.Apply(Custom(10));

        state.Apply(Hide(20, OverrideCanonical, "custom"));
        state.Apply(Hide(30, OverrideCanonical, "custom"));

        (Custom(state).Status, Custom(state).DeactivationEventId).ShouldBe((Disabling, 20L));
        state.GetTransitionCandidates(20).Count.ShouldBe(1);
        state.GetTransitionCandidates(30).ShouldBeEmpty();

        state.Apply(At(40, new SearchParameterTransitionCommitted(7, [], [20])));

        Custom(state).Status.ShouldBe(Disabled);
    }

    [Fact]
    public void GivenAnotherParameterSharesThePredecessorEventId_WhenAnOverrideIsRemoved_ThenTheMatchingCodeIsRestored()
    {
        using var state = new ConformanceState();
        state.Apply(Base(10));
        state.Apply(At(10, Activated(
            "http://hl7.org/fhir/SearchParameter/Observation-subject", "subject", 8, package: CorePackage, resourceType: "Observation")));
        state.Apply(Override(20));
        state.Apply(Commit(30, 20));
        state.Apply(Started(31, 20, "job"));
        state.Apply(Completed(32, 20, "job"));

        state.Apply(Hide(40, OverrideCanonical));

        var restored = state.FindByCanonical(BaseCanonical)!;
        (restored.ResourceType, restored.Code, restored.Status, restored.ActivationEventId).ShouldBe(("Patient", "identifier", Staged, 40L));
    }

    // Started guard: a Pending owner only starts a reindex for its own activation.
    [Fact]
    public void GivenPendingOwner_WhenReindexStartHasWrongActivation_ThenOwnerRemainsPending()
    {
        using var state = new ConformanceState();
        state.Apply(Custom(10));

        state.Apply(Started(20, 9, "wrong-activation"));

        (Custom(state).Status, Custom(state).ReindexJobId).ShouldBe((Pending, (string?)null));
    }

    public enum ReindexEventKind
    {
        Started,
        Completed,
        Failed,
    }

    // Completed / Failed guards (and Started on a running job): an event for another activation or job never moves it.
    [Theory]
    [InlineData(ReindexEventKind.Started, 9L, "other")]
    [InlineData(ReindexEventKind.Started, null, "legacy-other-job")]
    [InlineData(ReindexEventKind.Completed, 10L, "wrong")]
    [InlineData(ReindexEventKind.Completed, 9L, "current")]
    [InlineData(ReindexEventKind.Completed, null, "legacy-other-job")]
    [InlineData(ReindexEventKind.Failed, 10L, "wrong")]
    [InlineData(ReindexEventKind.Failed, 9L, "current")]
    [InlineData(ReindexEventKind.Failed, null, "legacy-other-job")]
    public void GivenRunningReindex_WhenAnEventForAnotherActivationOrJobArrives_ThenTheRunningJobIsKept(
        ReindexEventKind kind,
        long? activationEventId,
        string jobId)
    {
        using var state = new ConformanceState();
        state.Apply(Custom(10));
        state.Apply(Started(20, 10, "current"));

        state.Apply(kind switch
        {
            ReindexEventKind.Started => Started(30, activationEventId, jobId),
            ReindexEventKind.Completed => Completed(30, activationEventId, jobId),
            _ => At(30, ReindexFailed(OverrideCanonical, "custom", activationEventId, jobId)),
        });

        (Custom(state).Status, Custom(state).ReindexJobId).ShouldBe((Reindexing, "current"));
    }

    [Fact]
    public void GivenPersistedLegacyReindexJson_WhenDeserialized_ThenActivationIdDefaultsToNull()
    {
        const string json =
            """{"canonical":"http://example.org/SearchParameter/custom","code":"custom","resourceType":"Patient","jobId":"job","affectedResourceTypes":["Patient"]}""";

        var reindex = JsonSerializer.Deserialize<SearchParameterReindexStarted>(json, CamelCase);

        reindex.ShouldNotBeNull().ActivationEventId.ShouldBeNull();
    }

    [Fact]
    public void GivenOccupiedCode_WhenReplacementUsesDifferentSearchParamId_ThenActivationIsRejectedWithoutMutation()
    {
        using var state = new ConformanceState();
        state.Apply(Base(10));

        state.Apply(At(20, Activated(OverrideCanonical, "identifier", 8, BaseCanonical)));

        var owner = state.GetSearchParameter("Patient", "identifier")!;
        (owner.Canonical, owner.SearchParamId, owner.Status).ShouldBe((BaseCanonical, 7, Enabled));
        state.FindByCanonical(OverrideCanonical).ShouldBeNull();
    }

    [Fact]
    public void GivenEnabledBaseParameter_WhenSameCanonicalBaseActivationReplays_ThenOwnerRemainsEnabled()
    {
        using var state = new ConformanceState();
        state.Apply(Base(10));

        state.Apply(At(20, Activated(BaseCanonical, "identifier", 7, package: "hl7.fhir.r4.core@4.0.2")));

        var owner = state.GetSearchParameter("Patient", "identifier")!;
        (owner.Status, owner.ActivationEventId).ShouldBe((Enabled, 10L));
        state.FindByCanonical(BaseCanonical).ShouldBeSameAs(owner);
    }

    private static SearchParameterStatus[] Statuses(ConformanceState state, params string[] canonicals) =>
        [.. canonicals.Select(canonical => state.FindByCanonical(canonical)!.Status)];

    private static string Owner(ConformanceState state) => state.GetSearchParameter("Patient", "identifier")!.Canonical;

    private static ActiveSearchParameter Custom(ConformanceState state) => state.GetSearchParameter("Patient", "custom")!;

    private static ConformanceState CreateStateWithStagedOverride()
    {
        var state = new ConformanceState();
        state.Apply(Base(10));
        state.Apply(Override(20));
        return state;
    }

    private static ConformanceState CreateStateWithEnabledOverride()
    {
        var state = CreateStateWithStagedOverride();
        state.Apply(Commit(30, 20));
        state.Apply(Started(31, 20, "job"));
        state.Apply(Completed(32, 20, "job"));
        return state;
    }

    private static ConformanceState CreateStateWithEnabledThreeLevelOverride()
    {
        var state = new ConformanceState();
        state.Apply(Base(10));
        state.Apply(Override(20, MiddleCanonical, "middle.package@1.0"));
        state.Apply(Commit(21, 20));
        state.Apply(Started(22, 20, "middle-job", MiddleCanonical));
        state.Apply(Completed(23, 20, "middle-job", MiddleCanonical));
        state.Apply(Override(30, TopCanonical, "top.package@1.0"));
        state.Apply(Commit(31, 30));
        state.Apply(Started(32, 30, "top-job", TopCanonical));
        state.Apply(Completed(33, 30, "top-job", TopCanonical));
        return state;
    }

    private static SourceEvent Base(long eventId) =>
        At(eventId, Activated(BaseCanonical, "identifier", 7, package: CorePackage));

    private static SourceEvent Override(long eventId, string canonical = OverrideCanonical, string package = CustomPackage) =>
        At(eventId, Activated(canonical, "identifier", 7, BaseCanonical, package));

    private static SourceEvent Custom(long eventId) => At(eventId, Activated(OverrideCanonical, "custom", 7));

    private static SourceEvent Hide(long eventId, string canonical, string code = "identifier") =>
        At(eventId, Deactivated(canonical, code));

    private static SourceEvent Commit(long eventId, long hideEventId) => At(eventId, Committed(7, hideEventId));

    // Reindex events for OverrideCanonical carry the "custom" code; the guards match on canonical and activation.
    private static SourceEvent Started(long eventId, long? activationEventId, string jobId, string canonical = OverrideCanonical) =>
        At(eventId, ReindexStarted(canonical, ReindexCode(canonical), activationEventId, jobId));

    private static SourceEvent Completed(long eventId, long? activationEventId, string jobId, string canonical = OverrideCanonical) =>
        At(eventId, ReindexCompleted(canonical, ReindexCode(canonical), activationEventId, jobId));

    private static string ReindexCode(string canonical) => canonical == OverrideCanonical ? "custom" : "identifier";
}
