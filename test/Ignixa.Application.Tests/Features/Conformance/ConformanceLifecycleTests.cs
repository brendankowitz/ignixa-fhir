using System.Text.Json;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Shouldly;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Conformance;

public class ConformanceLifecycleTests
{
    private const string BaseCanonical = "http://hl7.org/fhir/SearchParameter/Patient-identifier";
    private const string OverrideCanonical = "http://example.org/SearchParameter/Patient-identifier";
    private static readonly JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void GivenBrandNewCustomParameter_WhenActivated_ThenItIsPendingAndTracksItsActivation()
    {
        using var state = new ConformanceState();

        state.Apply(Activation(10, OverrideCanonical, "custom", 7));

        var parameter = state.GetSearchParameter("Patient", "custom");
        parameter.ShouldNotBeNull();
        parameter.Status.ShouldBe(SearchParameterStatus.Pending);
        parameter.ActivationEventId.ShouldBe(10);
    }

    [Fact]
    public void GivenEnabledBaseParameter_WhenOverrideIsActivated_ThenBaseDisablesAndOverrideIsStaged()
    {
        using var state = new ConformanceState();
        state.Apply(Activation(10, BaseCanonical, "identifier", 7, sourcePackage: "hl7.fhir.r4.core@4.0.1"));

        state.Apply(Activation(
            20,
            OverrideCanonical,
            "identifier",
            7,
            new OverrideInfo(BaseCanonical, 7)));

        state.GetSearchParameter("Patient", "identifier")!.Canonical.ShouldBe(BaseCanonical);
        state.FindByCanonical(BaseCanonical)!.Status.ShouldBe(SearchParameterStatus.Disabling);
        state.FindByCanonical(OverrideCanonical)!.Status.ShouldBe(SearchParameterStatus.Staged);
    }

    [Fact]
    public void GivenStagedOverride_WhenMatchingTransitionIsCommitted_ThenOverrideBecomesPending()
    {
        using var state = CreateStateWithStagedOverride();

        state.Apply(Transition(30, 7, [20], [20]));

        state.FindByCanonical(BaseCanonical)!.Status.ShouldBe(SearchParameterStatus.Disabled);
        state.FindByCanonical(OverrideCanonical)!.Status.ShouldBe(SearchParameterStatus.Pending);
        state.GetSearchParameter("Patient", "identifier")!.Canonical.ShouldBe(OverrideCanonical);
    }

    [Fact]
    public void GivenStagedOverride_WhenStaleTransitionIsCommitted_ThenTransitionIsIgnored()
    {
        using var state = CreateStateWithStagedOverride();

        state.Apply(Transition(30, 7, [19], [19]));

        state.FindByCanonical(BaseCanonical)!.Status.ShouldBe(SearchParameterStatus.Disabling);
        state.FindByCanonical(OverrideCanonical)!.Status.ShouldBe(SearchParameterStatus.Staged);
        state.GetSearchParameter("Patient", "identifier")!.Canonical.ShouldBe(BaseCanonical);
    }

    [Fact]
    public void GivenStagedOverride_WhenTransitionHasCorrectActivationAndWrongDeactivation_ThenNothingChanges()
    {
        using var state = CreateStateWithStagedOverride();

        state.Apply(Transition(30, 7, [20], [19]));

        state.FindByCanonical(BaseCanonical)!.Status.ShouldBe(SearchParameterStatus.Disabling);
        state.FindByCanonical(OverrideCanonical)!.Status.ShouldBe(SearchParameterStatus.Staged);
        state.GetSearchParameter("Patient", "identifier")!.Canonical.ShouldBe(BaseCanonical);
    }

    [Fact]
    public void GivenStagedOverride_WhenTransitionHasWrongActivationAndCorrectDeactivation_ThenNothingChanges()
    {
        using var state = CreateStateWithStagedOverride();

        state.Apply(Transition(30, 7, [19], [20]));

        state.FindByCanonical(BaseCanonical)!.Status.ShouldBe(SearchParameterStatus.Disabling);
        state.FindByCanonical(OverrideCanonical)!.Status.ShouldBe(SearchParameterStatus.Staged);
        state.GetSearchParameter("Patient", "identifier")!.Canonical.ShouldBe(BaseCanonical);
    }

    [Fact]
    public void GivenNewerStagedOverride_WhenOlderTransitionIsCommitted_ThenOlderOverrideIsNotPromoted()
    {
        using var state = CreateStateWithStagedOverride();
        const string newerCanonical = "http://example.org/SearchParameter/Patient-identifier-newer";
        state.Apply(Activation(
            30,
            newerCanonical,
            "identifier",
            7,
            new OverrideInfo(BaseCanonical, 7)));

        state.Apply(Transition(40, 7, [20], [20]));

        state.FindByCanonical(OverrideCanonical)!.Status.ShouldBe(SearchParameterStatus.Disabled);
        state.FindByCanonical(newerCanonical)!.Status.ShouldBe(SearchParameterStatus.Staged);
        state.FindByCanonical(BaseCanonical)!.Status.ShouldBe(SearchParameterStatus.Disabling);
        state.GetSearchParameter("Patient", "identifier")!.Canonical.ShouldBe(BaseCanonical);
    }

    [Fact]
    public void GivenActiveOverride_WhenDeactivated_ThenOverrideDisablesAndBaseIsStagedForRestoration()
    {
        using var state = CreateStateWithEnabledOverride();

        state.Apply(Deactivation(40, OverrideCanonical));

        state.FindByCanonical(OverrideCanonical)!.Status.ShouldBe(SearchParameterStatus.Disabling);
        state.FindByCanonical(BaseCanonical)!.Status.ShouldBe(SearchParameterStatus.Staged);
        state.FindByCanonical(BaseCanonical)!.ActivationEventId.ShouldBe(40);
        state.GetSearchParameter("Patient", "identifier")!.Canonical.ShouldBe(OverrideCanonical);
    }

    [Fact]
    public void GivenRestoredBase_WhenMatchingTransitionIsCommitted_ThenBaseBecomesPending()
    {
        using var state = CreateStateWithEnabledOverride();
        state.Apply(Deactivation(40, OverrideCanonical));

        state.Apply(Transition(50, 7, [40], [40]));

        state.FindByCanonical(OverrideCanonical)!.Status.ShouldBe(SearchParameterStatus.Disabled);
        state.FindByCanonical(BaseCanonical)!.Status.ShouldBe(SearchParameterStatus.Pending);
        state.GetSearchParameter("Patient", "identifier")!.Canonical.ShouldBe(BaseCanonical);
    }

    [Fact]
    public void GivenStagedOverride_WhenItIsDeactivatedBeforeCommit_ThenBaseIsRestagedAndOldTransitionIsIgnored()
    {
        using var state = CreateStateWithStagedOverride();

        state.Apply(Deactivation(30, OverrideCanonical));
        state.Apply(Transition(40, 7, [20], [20]));

        state.FindByCanonical(OverrideCanonical)!.Status.ShouldBe(SearchParameterStatus.Disabled);
        state.FindByCanonical(BaseCanonical)!.Status.ShouldBe(SearchParameterStatus.Staged);
        state.FindByCanonical(BaseCanonical)!.ActivationEventId.ShouldBe(30);
        state.GetSearchParameter("Patient", "identifier")!.Status.ShouldBe(SearchParameterStatus.Disabling);
    }

    [Fact]
    public void GivenStagedOverridePackage_WhenPackageIsDeactivatedBeforeCommit_ThenBaseIsRestaged()
    {
        using var state = CreateStateWithStagedOverride();

        state.Apply(new SourceEvent(
            30,
            "lifecycle-test",
            nameof(PackageDeactivated),
            new PackageDeactivated("custom.package", "1.0", "test"),
            DateTimeOffset.UtcNow));

        state.FindByCanonical(OverrideCanonical)!.Status.ShouldBe(SearchParameterStatus.Disabled);
        state.FindByCanonical(BaseCanonical)!.Status.ShouldBe(SearchParameterStatus.Staged);
        state.FindByCanonical(BaseCanonical)!.ActivationEventId.ShouldBe(30);
    }

    [Fact]
    public void GivenThreeLevelOverride_WhenMiddlePackageIsDeactivatedBeforeTop_ThenMiddleIsNotRestored()
    {
        using var state = CreateStateWithEnabledThreeLevelOverride();

        state.Apply(PackageDeactivation(50, "middle.package"));
        state.Apply(Deactivation(60, TopCanonical));

        state.FindByCanonical(TopCanonical)!.Status.ShouldBe(SearchParameterStatus.Disabling);
        var restored = state.FindByCanonical(BaseCanonical)!;
        restored.Status.ShouldBe(SearchParameterStatus.Staged);
        restored.ActivationEventId.ShouldBe(60);
    }

    [Fact]
    public void GivenThreeLevelOverride_WhenLowerPackageIsDeactivatedBeforeHigherDefinitions_ThenItIsNeverRestored()
    {
        using var state = CreateStateWithEnabledThreeLevelOverride();

        state.Apply(PackageDeactivation(50, "hl7.fhir.r4.core", "4.0.1"));
        state.Apply(Deactivation(60, TopCanonical));
        state.Apply(Transition(61, 7, [60], [60]));
        state.Apply(ReindexStarted(62, MiddleCanonical, activationEventId: 60, jobId: "middle-job"));
        state.Apply(ReindexCompleted(63, MiddleCanonical, activationEventId: 60, jobId: "middle-job"));
        state.Apply(Deactivation(70, MiddleCanonical));

        state.FindByCanonical(MiddleCanonical)!.Status.ShouldBe(SearchParameterStatus.Disabling);
        state.FindByCanonical(BaseCanonical)!.Status.ShouldBe(SearchParameterStatus.Disabled);
        state.GetSearchParameter("Patient", "identifier")!.Canonical.ShouldBe(MiddleCanonical);
    }

    [Fact]
    public void GivenThreeLevelOverride_WhenTopPackageIsDeactivated_ThenMiddleIsRestored()
    {
        using var state = CreateStateWithEnabledThreeLevelOverride();

        state.Apply(PackageDeactivation(50, "top.package"));

        state.FindByCanonical(TopCanonical)!.Status.ShouldBe(SearchParameterStatus.Disabling);
        var restored = state.FindByCanonical(MiddleCanonical)!;
        restored.Status.ShouldBe(SearchParameterStatus.Staged);
        restored.ActivationEventId.ShouldBe(50);
    }

    [Fact]
    public void GivenPlainParameter_WhenDeactivatedAndCommitted_ThenItMovesThroughDisablingToDisabled()
    {
        using var state = new ConformanceState();
        state.Apply(Activation(10, OverrideCanonical, "custom", 7));

        state.Apply(Deactivation(20, OverrideCanonical, "custom"));

        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Disabling);

        state.Apply(Transition(30, 7, [], [20]));

        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Disabled);
    }

    [Fact]
    public void GivenNewerActivation_WhenStaleReindexEventsArrive_ThenTheyAreIgnored()
    {
        using var state = new ConformanceState();
        state.Apply(Activation(10, OverrideCanonical, "custom", 7));
        state.Apply(Activation(20, OverrideCanonical, "custom", 7));

        state.Apply(ReindexStarted(30, activationEventId: 10, jobId: "stale"));
        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Disabling);
        state.FindByCanonical(OverrideCanonical)!.Status.ShouldBe(SearchParameterStatus.Staged);

        state.Apply(Transition(31, 7, [20], [20]));
        state.Apply(ReindexStarted(32, activationEventId: 20, jobId: "current"));
        state.Apply(ReindexCompleted(33, activationEventId: 20, jobId: "other"));

        var current = state.GetSearchParameter("Patient", "custom")!;
        current.Status.ShouldBe(SearchParameterStatus.Reindexing);
        current.ReindexJobId.ShouldBe("current");
    }

    [Fact]
    public void GivenPendingOwner_WhenReindexStartHasWrongActivation_ThenOwnerRemainsPending()
    {
        using var state = new ConformanceState();
        state.Apply(Activation(10, OverrideCanonical, "custom", 7));

        state.Apply(ReindexStarted(20, activationEventId: 9, jobId: "wrong-activation"));

        var parameter = state.GetSearchParameter("Patient", "custom")!;
        parameter.Status.ShouldBe(SearchParameterStatus.Pending);
        parameter.ReindexJobId.ShouldBeNull();
    }

    [Fact]
    public void GivenMatchingActivationAndJob_WhenReindexCompletes_ThenParameterIsEnabled()
    {
        using var state = new ConformanceState();
        state.Apply(Activation(10, OverrideCanonical, "custom", 7));
        state.Apply(ReindexStarted(20, activationEventId: 10, jobId: "job"));

        state.Apply(ReindexCompleted(30, activationEventId: 10, jobId: "job"));

        var parameter = state.GetSearchParameter("Patient", "custom")!;
        parameter.Status.ShouldBe(SearchParameterStatus.Enabled);
        parameter.ReindexJobId.ShouldBeNull();
    }

    [Fact]
    public void GivenMatchingActivationAndWrongJob_WhenReindexCompletes_ThenCompletionIsIgnored()
    {
        using var state = new ConformanceState();
        state.Apply(Activation(10, OverrideCanonical, "custom", 7));
        state.Apply(ReindexStarted(20, activationEventId: 10, jobId: "current"));

        state.Apply(ReindexCompleted(30, activationEventId: 10, jobId: "wrong"));

        var parameter = state.GetSearchParameter("Patient", "custom")!;
        parameter.Status.ShouldBe(SearchParameterStatus.Reindexing);
        parameter.ReindexJobId.ShouldBe("current");
    }

    [Fact]
    public void GivenMatchingActivationAndWrongJob_WhenReindexFails_ThenFailureIsIgnored()
    {
        using var state = new ConformanceState();
        state.Apply(Activation(10, OverrideCanonical, "custom", 7));
        state.Apply(ReindexStarted(20, activationEventId: 10, jobId: "current"));

        state.Apply(ReindexFailed(30, activationEventId: 10, jobId: "wrong"));

        var parameter = state.GetSearchParameter("Patient", "custom")!;
        parameter.Status.ShouldBe(SearchParameterStatus.Reindexing);
        parameter.ReindexJobId.ShouldBe("current");
    }

    [Fact]
    public void GivenMismatchedActivationOrJob_WhenReindexFails_ThenFailureIsIgnored()
    {
        using var state = new ConformanceState();
        state.Apply(Activation(10, OverrideCanonical, "custom", 7));
        state.Apply(ReindexStarted(20, activationEventId: 10, jobId: "current"));

        state.Apply(ReindexFailed(30, activationEventId: 9, jobId: "current"));
        state.Apply(ReindexFailed(31, activationEventId: 10, jobId: "other"));

        var parameter = state.GetSearchParameter("Patient", "custom")!;
        parameter.Status.ShouldBe(SearchParameterStatus.Reindexing);
        parameter.ReindexJobId.ShouldBe("current");
    }

    [Fact]
    public void GivenParameterIsDisabling_WhenReindexEventsArrive_ThenTheyCannotResurrectIt()
    {
        using var state = new ConformanceState();
        state.Apply(Activation(10, OverrideCanonical, "custom", 7));
        state.Apply(Deactivation(20, OverrideCanonical, "custom"));

        state.Apply(ReindexStarted(30, activationEventId: 10, jobId: "job"));
        state.Apply(ReindexCompleted(31, activationEventId: 10, jobId: "job"));

        var parameter = state.GetSearchParameter("Patient", "custom")!;
        parameter.Status.ShouldBe(SearchParameterStatus.Disabling);
        parameter.ReindexJobId.ShouldBeNull();
    }

    [Fact]
    public void GivenLegacyReindexEventWithoutActivationId_WhenApplied_ThenItRetainsLegacyUnguardedBehavior()
    {
        using var state = new ConformanceState();
        state.Apply(Activation(10, OverrideCanonical, "custom", 7));
        state.Apply(ReindexStarted(20, activationEventId: 10, jobId: "current"));

        state.Apply(ReindexCompleted(30, activationEventId: null, jobId: "legacy-other-job"));

        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Enabled);
    }

    [Fact]
    public void GivenPersistedLegacyReindexJson_WhenDeserialized_ThenActivationIdDefaultsToNull()
    {
        const string json =
            """{"canonical":"http://example.org/SearchParameter/custom","code":"custom","resourceType":"Patient","jobId":"job","affectedResourceTypes":["Patient"]}""";
        var reindex = JsonSerializer.Deserialize<SearchParameterReindexStarted>(json, CamelCaseJsonOptions);

        reindex.ShouldNotBeNull();
        reindex.ActivationEventId.ShouldBeNull();
    }

    [Fact]
    public void GivenOccupiedCode_WhenReplacementUsesDifferentSearchParamId_ThenActivationIsRejectedWithoutMutation()
    {
        using var state = new ConformanceState();
        state.Apply(Activation(10, BaseCanonical, "identifier", 7, sourcePackage: "hl7.fhir.r4.core@4.0.1"));

        state.Apply(Activation(
            20,
            OverrideCanonical,
            "identifier",
            8,
            new OverrideInfo(BaseCanonical, 8)));

        var owner = state.GetSearchParameter("Patient", "identifier")!;
        owner.Canonical.ShouldBe(BaseCanonical);
        owner.SearchParamId.ShouldBe(7);
        owner.Status.ShouldBe(SearchParameterStatus.Enabled);
        state.FindByCanonical(OverrideCanonical).ShouldBeNull();
    }

    [Fact]
    public void GivenEnabledBaseParameter_WhenSameCanonicalBaseActivationReplays_ThenOwnerRemainsEnabled()
    {
        using var state = new ConformanceState();
        state.Apply(Activation(10, BaseCanonical, "identifier", 7, sourcePackage: "hl7.fhir.r4.core@4.0.1"));

        state.Apply(Activation(20, BaseCanonical, "identifier", 7, sourcePackage: "hl7.fhir.r4.core@4.0.2"));

        var owner = state.GetSearchParameter("Patient", "identifier")!;
        owner.Status.ShouldBe(SearchParameterStatus.Enabled);
        owner.ActivationEventId.ShouldBe(10);
        state.FindByCanonical(BaseCanonical).ShouldBeSameAs(owner);
    }

    private const string MiddleCanonical = "http://example.org/SearchParameter/Patient-identifier-middle";
    private const string TopCanonical = "http://example.org/SearchParameter/Patient-identifier-top";

    private static ConformanceState CreateStateWithStagedOverride()
    {
        var state = new ConformanceState();
        state.Apply(Activation(10, BaseCanonical, "identifier", 7, sourcePackage: "hl7.fhir.r4.core@4.0.1"));
        state.Apply(Activation(
            20,
            OverrideCanonical,
            "identifier",
            7,
            new OverrideInfo(BaseCanonical, 7)));
        return state;
    }

    private static ConformanceState CreateStateWithEnabledOverride()
    {
        var state = CreateStateWithStagedOverride();
        state.Apply(Transition(30, 7, [20], [20]));
        state.Apply(ReindexStarted(31, activationEventId: 20, jobId: "job"));
        state.Apply(ReindexCompleted(32, activationEventId: 20, jobId: "job"));
        return state;
    }

    private static ConformanceState CreateStateWithEnabledThreeLevelOverride()
    {
        var state = new ConformanceState();
        state.Apply(Activation(10, BaseCanonical, "identifier", 7, sourcePackage: "hl7.fhir.r4.core@4.0.1"));
        state.Apply(Activation(
            20,
            MiddleCanonical,
            "identifier",
            7,
            new OverrideInfo(BaseCanonical, 7),
            "middle.package@1.0"));
        state.Apply(Transition(21, 7, [20], [20]));
        state.Apply(ReindexStarted(22, MiddleCanonical, activationEventId: 20, jobId: "middle-job"));
        state.Apply(ReindexCompleted(23, MiddleCanonical, activationEventId: 20, jobId: "middle-job"));
        state.Apply(Activation(
            30,
            TopCanonical,
            "identifier",
            7,
            new OverrideInfo(BaseCanonical, 7),
            "top.package@1.0"));
        state.Apply(Transition(31, 7, [30], [30]));
        state.Apply(ReindexStarted(32, TopCanonical, activationEventId: 30, jobId: "top-job"));
        state.Apply(ReindexCompleted(33, TopCanonical, activationEventId: 30, jobId: "top-job"));
        return state;
    }

    private static SourceEvent Activation(
        long eventId,
        string canonical,
        string code,
        int searchParamId,
        OverrideInfo? overrides = null,
        string sourcePackage = "custom.package@1.0") =>
        new(
            eventId,
            "lifecycle-test",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                canonical,
                code,
                "Patient",
                $"Patient.{code}",
                SearchParamType.Token,
                sourcePackage,
                overrides,
                searchParamId,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow);

    private static SourceEvent Deactivation(long eventId, string canonical, string code = "identifier") =>
        new(
            eventId,
            "lifecycle-test",
            nameof(SearchParameterDeactivated),
            new SearchParameterDeactivated(canonical, code, "Patient", "test"),
            DateTimeOffset.UtcNow);

    private static SourceEvent PackageDeactivation(long eventId, string packageId, string version = "1.0") =>
        new(
            eventId,
            "lifecycle-test",
            nameof(PackageDeactivated),
            new PackageDeactivated(packageId, version, "test"),
            DateTimeOffset.UtcNow);

    private static SourceEvent Transition(
        long eventId,
        int searchParamId,
        IReadOnlyList<long> activationEventIds,
        IReadOnlyList<long> deactivationEventIds) =>
        new(
            eventId,
            "lifecycle-test",
            nameof(SearchParameterTransitionCommitted),
            new SearchParameterTransitionCommitted(searchParamId, activationEventIds, deactivationEventIds),
            DateTimeOffset.UtcNow);

    private static SourceEvent ReindexStarted(
        long eventId,
        long? activationEventId,
        string jobId) =>
        ReindexStarted(eventId, OverrideCanonical, activationEventId, jobId);

    private static SourceEvent ReindexStarted(
        long eventId,
        string canonical,
        long? activationEventId,
        string jobId) =>
        new(
            eventId,
            "lifecycle-test",
            nameof(SearchParameterReindexStarted),
            new SearchParameterReindexStarted(
                canonical,
                canonical == OverrideCanonical ? "custom" : "identifier",
                "Patient",
                jobId,
                ["Patient"],
                activationEventId),
            DateTimeOffset.UtcNow);

    private static SourceEvent ReindexCompleted(
        long eventId,
        long? activationEventId,
        string jobId) =>
        ReindexCompleted(eventId, OverrideCanonical, activationEventId, jobId);

    private static SourceEvent ReindexCompleted(
        long eventId,
        string canonical,
        long? activationEventId,
        string jobId) =>
        new(
            eventId,
            "lifecycle-test",
            nameof(SearchParameterReindexCompleted),
            new SearchParameterReindexCompleted(
                canonical,
                canonical == OverrideCanonical ? "custom" : "identifier",
                "Patient",
                jobId,
                1,
                TimeSpan.FromSeconds(1),
                activationEventId),
            DateTimeOffset.UtcNow);

    private static SourceEvent ReindexFailed(long eventId, long? activationEventId, string jobId) =>
        new(
            eventId,
            "lifecycle-test",
            nameof(SearchParameterReindexFailed),
            new SearchParameterReindexFailed(
                OverrideCanonical,
                "custom",
                "Patient",
                jobId,
                "failed",
                activationEventId),
            DateTimeOffset.UtcNow);
}
