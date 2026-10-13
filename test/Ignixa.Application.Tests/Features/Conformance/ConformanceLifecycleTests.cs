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

    // Removing an override is two-phase: its hide event restages the base; the outgoing owner extracts until it commits.
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

    public enum ReindexEventKind
    {
        Started,
        Completed,
        Failed,
    }

    // Started / Completed / Failed guards: an event for another activation or job moves neither a Pending nor a running owner.
    [Theory]
    [InlineData(false, ReindexEventKind.Started, 9L, "wrong-activation")]
    [InlineData(true, ReindexEventKind.Started, 9L, "other")]
    [InlineData(true, ReindexEventKind.Started, null, "legacy-other-job")]
    [InlineData(true, ReindexEventKind.Completed, 10L, "wrong")]
    [InlineData(true, ReindexEventKind.Completed, 9L, "current")]
    [InlineData(true, ReindexEventKind.Completed, null, "legacy-other-job")]
    [InlineData(true, ReindexEventKind.Failed, 10L, "wrong")]
    [InlineData(true, ReindexEventKind.Failed, 9L, "current")]
    [InlineData(true, ReindexEventKind.Failed, null, "legacy-other-job")]
    public void GivenAPendingOrRunningOwner_WhenAReindexEventForAnotherActivationOrJobArrives_ThenItDoesNotMove(
        bool running,
        ReindexEventKind kind,
        long? activationEventId,
        string jobId)
    {
        using var state = new ConformanceState();
        state.Apply(Custom(10));
        if (running)
        {
            state.Apply(Started(20, 10, "current"));
        }

        state.Apply(kind switch
        {
            ReindexEventKind.Started => Started(30, activationEventId, jobId),
            ReindexEventKind.Completed => Completed(30, activationEventId, jobId),
            _ => At(30, new SearchParameterReindexFailed(OverrideCanonical, "custom", "Patient", jobId, "failed", activationEventId)),
        });

        (Custom(state).Status, Custom(state).ReindexJobId).ShouldBe(running ? (Reindexing, "current") : (Pending, (string?)null));
    }

    [Fact]
    public void GivenPersistedLegacyReindexJson_WhenDeserialized_ThenActivationIdDefaultsToNull()
    {
        const string json =
            """{"canonical":"http://example.org/SearchParameter/custom","code":"custom","resourceType":"Patient","jobId":"job","affectedResourceTypes":["Patient"]}""";

        var reindex = JsonSerializer.Deserialize<SearchParameterReindexStarted>(json, JsonSerializerOptions.Web);

        reindex.ShouldNotBeNull().ActivationEventId.ShouldBeNull();
    }

    // A different storage identity on an occupied code, or a replay of the enabled base, leaves the owner untouched.
    [Theory]
    [InlineData(OverrideCanonical, 8, BaseCanonical, CustomPackage)]
    [InlineData(BaseCanonical, 7, null, "hl7.fhir.r4.core@4.0.2")]
    public void GivenEnabledBaseOwner_WhenARefusedActivationArrives_ThenTheOwnerIsUnchanged(
        string canonical,
        int searchParamId,
        string? overrides,
        string package)
    {
        using var state = new ConformanceState();
        state.Apply(Base(10));

        state.Apply(At(20, Activated(canonical, "identifier", searchParamId, overrides, package)));

        var owner = state.GetSearchParameter("Patient", "identifier")!;
        (owner.Canonical, owner.SearchParamId, owner.Status, owner.ActivationEventId).ShouldBe((BaseCanonical, 7, Enabled, 10L));
        state.FindByCanonical(BaseCanonical).ShouldBeSameAs(owner);
        state.FindByCanonical(OverrideCanonical).ShouldBeNull();
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
