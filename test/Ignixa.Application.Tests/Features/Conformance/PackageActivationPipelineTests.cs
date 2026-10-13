using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using NSubstitute;
using Shouldly;
using static Ignixa.Application.Tests.Features.Conformance.ConformanceEvents;
using static Ignixa.Application.Tests.Features.Conformance.TestPackages;

namespace Ignixa.Application.Tests.Features.Conformance;

public class PackageActivationPipelineTests
{
    // B-L2: an override is staged under the base's storage identity, whether or not it names it in derivedFrom.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenInProcessBaseCode_WhenShadowed_ThenOverrideIsStagedUnderTheBaseIdentityAndTheHiddenCodeIsReported(bool derivedFrom)
    {
        using var harness = new Harness();
        var pipeline = harness.Pipeline(Shadow(derivedFrom: derivedFrom ? BaseIdentifier : null));

        var result = await ActivateAsync(pipeline, "test.override");

        result.Success.ShouldBeTrue();
        result.PendingReindex.ShouldBeEmpty();
        var hidden = result.Issues.ShouldHaveSingleItem();
        (hidden.Code, hidden.Severity).ShouldBe((PackageActivationPipeline.TransitionPendingCode, ActivationIssueSeverity.Warning));
        hidden.Message.ShouldContain("Patient.identifier");
        hidden.Message.ShouldContain(TestPackageActivationPipeline.TransitionGrace.ToString());
        hidden.Message.ShouldContain("reindex");
        var activations = harness.Activations.ToArray();
        activations.Length.ShouldBe(2);
        (activations[0].Canonical, activations[0].SourcePackage).ShouldBe((BaseIdentifier, CorePackage));
        (activations[1].Overrides!.OverridesCanonical, activations[1].SearchParamId).ShouldBe((BaseIdentifier, activations[0].SearchParamId));
        harness.State.FindByCanonical(BaseIdentifier)!.Status.ShouldBe(SearchParameterStatus.Disabling);
        harness.State.FindByCanonical(Canonical("test.override"))!.Status.ShouldBe(SearchParameterStatus.Staged);
        await harness.Scheduler.Received(1).ScheduleAsync(Arg.Any<long>(), Arg.Any<TimeSpan>(), CancellationToken.None);
    }

    // §4.4: the base owner is materialised once; a later shadow emits no second base activation.
    [Fact]
    public async Task GivenBaseWasMaterialisedByEarlierShadowing_WhenShadowedAgain_ThenNoSecondBaseActivationIsEmitted()
    {
        using var harness = new Harness();
        var pipeline = harness.Pipeline(
            Shadow("test.first", derivedFrom: null),
            Shadow("test.second", derivedFrom: Canonical("test.first")));

        (await ActivateAsync(pipeline, "test.first")).Success.ShouldBeTrue();
        (await ActivateAsync(pipeline, "test.second")).Success.ShouldBeTrue();

        harness.Activations.Count(activation => activation.Canonical == BaseIdentifier).ShouldBe(1);
    }

    // M-L3: a parameter on several base types resolves one storage identity, including types with no base code.
    [Theory]
    [InlineData("_tag", "Resource.meta.tag", "Patient", "Practitioner")]
    [InlineData("identifier", "Resource.identifier", "Patient", "Binary")]
    [InlineData("identifier", "Resource.identifier", "Binary", "Patient")]
    public async Task GivenAMultiBaseShadow_WhenActivated_ThenEveryBaseTypeSharesOneStorageIdentity(
        string code,
        string expression,
        string firstBaseType,
        string secondBaseType)
    {
        using var harness = new Harness();
        var pipeline = harness.Pipeline(SearchParameter("test.multi-base", Canonical("multi"), code, [firstBaseType, secondBaseType], expression));

        var result = await ActivateAsync(pipeline, "test.multi-base");

        result.Success.ShouldBeTrue();
        var shadows = harness.Activations.Where(activation => activation.SourcePackage == "test.multi-base@1.0.0").ToArray();
        shadows.Length.ShouldBe(2);
        var identity = shadows.Select(activation => activation.SearchParamId).Distinct().ShouldHaveSingleItem();
        harness.Activations.Where(activation => activation.SourcePackage == CorePackage)
            .ShouldAllBe(activation => activation.SearchParamId == identity);
    }

    // B-L2: when one base type is already owned (by the base or by an override of it) and another is not, the
    // unowned type's base and the package's activations all take the owned root's existing identity, not a fresh one.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenAMultiBaseShadowWithOnlyOneBaseTypeOwned_WhenActivated_ThenEveryActivationTakesTheOwnedIdentity(
        bool ownedByAnOverride)
    {
        const string baseTag = "http://hl7.org/fhir/SearchParameter/Resource-tag";
        const int ownedId = 7;
        using var harness = new Harness();
        harness.Seed(Activated(baseTag, "_tag", ownedId, package: CorePackage, expression: "Resource.meta.tag"));
        if (ownedByAnOverride)
        {
            harness.Seed(Activated(Canonical("existing"), "_tag", ownedId, baseTag, "existing.owner@1.0.0", expression: "Resource.meta.tag"));
        }

        var pipeline = harness.Pipeline(SearchParameter(
            "test.multi-base", Canonical("multi"), "_tag", ["Practitioner", "Patient"], "Resource.meta.tag", ownedByAnOverride ? Canonical("existing") : null));

        var result = await ActivateAsync(pipeline, "test.multi-base");

        result.Success.ShouldBeTrue(Describe(result));
        var practitionerBase = harness.Activations.Where(activation =>
            activation.SourcePackage == CorePackage && activation.ResourceType == "Practitioner").ShouldHaveSingleItem();
        practitionerBase.SearchParamId.ShouldBe(ownedId);
        var shadows = harness.Activations.Where(activation => activation.SourcePackage == "test.multi-base@1.0.0").ToArray();
        shadows.Select(activation => activation.ResourceType).ShouldBe(["Patient", "Practitioner"], ignoreOrder: true);
        shadows.ShouldAllBe(activation =>
            activation.SearchParamId == ownedId &&
            activation.Overrides!.OverridesCanonical == baseTag &&
            activation.Overrides.InheritedParamId == ownedId);
    }

    [Theory]
    [InlineData("Patient", "Practitioner", false)]
    [InlineData("Patient", "Practitioner", true)]
    [InlineData("Practitioner", "Patient", true)]
    public async Task GivenBaseTypesResolveDifferentStorageRoots_WhenActivatedInAnyOrder_ThenMixedBaseShadowIsRejected(
        string firstBaseType,
        string secondBaseType,
        bool patientAlreadyOverridden)
    {
        using var harness = new Harness();
        if (patientAlreadyOverridden)
        {
            harness.Seed(Activated(BaseIdentifier, "identifier", 1, package: CorePackage));
            harness.Seed(Activated(Canonical("existing"), "identifier", 1, BaseIdentifier, "existing.owner@1.0.0"));
        }

        var pipeline = harness.Pipeline(SearchParameter("test.multi-base", Canonical("multi"), baseTypes: [firstBaseType, secondBaseType]));

        var result = await ActivateAsync(pipeline, "test.multi-base");

        result.Success.ShouldBeFalse();
        result.Issues.ShouldContain(issue => issue.Code == "SP_MIXED_BASE_SHADOW");
    }

    // §4.4: validation uses the latest non-Disabled owner, so its result is independent of activation timing.
    [Fact]
    public async Task GivenAnotherPackageShadowsDuringTransition_WhenActivated_ThenConflictMatchesPostCommitResult()
    {
        var duringTransition = await ActivateSecondShadowAsync(removeFirst: false, commitPendingTransition: false);
        var afterTransition = await ActivateSecondShadowAsync(removeFirst: false, commitPendingTransition: true);

        duringTransition.Success.ShouldBeFalse();
        duringTransition.Issues.ShouldContain(issue => issue.Code == "SP_CONFLICT");
        afterTransition.Success.ShouldBe(duringTransition.Success);
        afterTransition.Issues.Select(issue => issue.Code).ShouldBe(duringTransition.Issues.Select(issue => issue.Code));
    }

    // §4.4: during an override removal, validation already matches the post-commit result.
    [Fact]
    public async Task GivenEnabledPackageIsRemoved_WhenAnotherPackageActivatesDuringRestoration_ThenResultMatchesPostCommit()
    {
        var duringRemoval = await ActivateSecondShadowAsync(removeFirst: true, commitPendingTransition: false);
        var afterRemoval = await ActivateSecondShadowAsync(removeFirst: true, commitPendingTransition: true);

        afterRemoval.Success.ShouldBeTrue(Describe(afterRemoval));
        duringRemoval.Success.ShouldBeTrue(Describe(duringRemoval));
        duringRemoval.Issues.Select(issue => issue.Code).ShouldBe(afterRemoval.Issues.Select(issue => issue.Code));
    }

    // N4: definitions are partitioned by FHIR version; one code cannot be owned by two versions.
    [Fact]
    public async Task GivenR4PackageOwnsACode_WhenAnR5PackageRedefinesIt_ThenActivationIsRejectedAsAVersionConflict()
    {
        using var harness = new Harness();
        var pipeline = harness.Pipeline(
            SearchParameter("test.r4", Canonical("test.r4")),
            SearchParameter("test.r5", Canonical("test.r5"), fhirVersion: "5.0.0"));
        (await ActivateAsync(pipeline, "test.r4")).Success.ShouldBeTrue();
        var eventCount = harness.Log.Events.Count;

        var result = await ActivateAsync(pipeline, "test.r5");

        result.Success.ShouldBeFalse();
        var issue = result.Issues.ShouldHaveSingleItem();
        issue.Code.ShouldBe("SP_FHIR_VERSION_CONFLICT");
        issue.Message.ShouldContain("FHIR 4.0");
        issue.Message.ShouldContain("FHIR 5.0");
        harness.Log.Events.Count.ShouldBe(eventCount);
        harness.Activations.ShouldAllBe(activation => activation.FhirVersion == "4.0");
    }

    // §4.4: shadowing a base code materialises the base under the base package key of the package's own version.
    [Theory]
    [InlineData("3.0.2", "hl7.fhir.r3.core@")]
    [InlineData("4.0.1", "hl7.fhir.r4.core@")]
    [InlineData("4.3.0", "hl7.fhir.r4b.core@")]
    [InlineData("5.0.0", "hl7.fhir.r5.core@")]
    public async Task GivenAPackageShadowingABaseCode_WhenActivated_ThenTheBaseIsMaterialisedUnderItsVersionsCorePackage(
        string fhirVersion,
        string expectedCorePackagePrefix)
    {
        using var harness = new Harness();
        var pipeline = harness.Pipeline(SearchParameter("test.override", Canonical("test.override"), fhirVersion: fhirVersion));

        var result = await ActivateAsync(pipeline, "test.override");

        result.Success.ShouldBeTrue(Describe(result));
        var materialised = harness.Activations.Single(activation => activation.Canonical == BaseIdentifier);
        materialised.SourcePackage.ShouldStartWith(expectedCorePackagePrefix);
        materialised.FhirVersion.ShouldBe(FhirSpecificationExtensions.FromVersionString(fhirVersion).ToVersionString());
    }

    // §4.4: a materialised base composite keeps its components: it stays the extracted owner until the transition.
    [Fact]
    public async Task GivenAPackageShadowingABaseComposite_WhenActivated_ThenTheMaterialisedBaseKeepsItsComponents()
    {
        using var harness = new Harness();
        var pipeline = harness.Pipeline(
            SearchParameter("test.composite", Canonical("x-code"), "x-code", ["Observation"], "Observation.code"),
            SearchParameter("test.composite", Canonical("x-value"), "x-value", ["Observation"], "Observation.value", type: "quantity"),
            Composite("test.composite", "code-value-quantity", "Observation", (Canonical("x-code"), "code"), (Canonical("x-value"), "value")));

        var result = await ActivateAsync(pipeline, "test.composite");

        result.Success.ShouldBeTrue(Describe(result));
        var materialised = harness.Activations.Single(activation =>
            activation.Canonical == "http://hl7.org/fhir/SearchParameter/Observation-code-value-quantity");
        materialised.Components.ShouldNotBeNull().Select(component => component.DefinitionUrl)
            .ShouldBe(["http://hl7.org/fhir/SearchParameter/clinical-code", "http://hl7.org/fhir/SearchParameter/Observation-value-quantity"]);
    }

    [Fact]
    public async Task GivenUnknownFhirVersionWithSearchParameters_WhenActivated_ThenItIsRejected()
    {
        using var harness = new Harness();
        var pipeline = harness.Pipeline(SearchParameter("test.future", Canonical("future"), fhirVersion: "99.0.0"));

        var result = await ActivateAsync(pipeline, "test.future");

        result.Success.ShouldBeFalse();
        result.Issues.ShouldContain(issue => issue.Code == "SP_UNKNOWN_FHIR_VERSION");
        harness.Log.Events.ShouldBeEmpty();
    }

    // §4.4: validation ignores a Disabled owner.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenADeactivatedOwnerLeftInTheLookup_WhenAnotherPackageClaimsItsCode_ThenRootResolutionIgnoresIt(
        bool deactivationCommitted)
    {
        const string oldCanonical = "http://example.org/SearchParameter/old-custom";
        using var harness = new Harness();
        harness.Seed(Activated(oldCanonical, "custom-code", 1, package: "test.old@1.0.0", expression: "Patient.active"));
        harness.Seed(PackageActivated("test.old", "1.0.0", oldCanonical));
        harness.Seed(PackageDeactivated("test.old", "1.0.0"));
        if (deactivationCommitted)
        {
            var outgoing = harness.State.GetSearchParameter("Patient", "custom-code")!;
            harness.Seed(new SearchParameterTransitionCommitted(outgoing.SearchParamId, [], [outgoing.DeactivationEventId!.Value]));
            harness.State.GetSearchParameter("Patient", "custom-code")!.Status.ShouldBe(SearchParameterStatus.Disabled);
        }

        var pipeline = harness.Pipeline(SearchParameter("test.new", Canonical("new-custom"), "custom-code", expression: "Patient.active"));

        var result = await ActivateAsync(pipeline, "test.new");

        result.Success.ShouldBeTrue(Describe(result));
        result.PendingReindex.ShouldBe(deactivationCommitted ? ["Patient"] : []);
    }

    public enum FollowUpFailure
    {
        ReindexTriggerDeferred,
        RefreshDependencyUnavailable,
        TransitionSchedulingFailed,
        ReindexTriggerProgrammerError,
        RefreshIndependentlyCancelled,
        TransitionSchedulerProgrammerError,
    }

    // N2: once the activation is durable, an operational follow-up failure is a warning, not a lost activation.
    [Theory]
    [InlineData(FollowUpFailure.ReindexTriggerDeferred, PackageActivationPipeline.ReindexTriggerDeferredCode)]
    [InlineData(FollowUpFailure.RefreshDependencyUnavailable, PackageActivationPipeline.RefreshDeferredCode)]
    [InlineData(FollowUpFailure.TransitionSchedulingFailed, PackageActivationPipeline.TransitionScheduleDeferredCode)]
    public async Task GivenOperationalFollowUpFailureAfterDurableActivation_WhenActivated_ThenItSucceedsWithAWarning(
        FollowUpFailure failure,
        string expectedCode)
    {
        using var harness = new Harness();
        var lease = TestConformanceLease.NotHeld();
        var (pipeline, packageId) = CreatePipelineFailingAfterAppend(harness, failure, lease);

        var result = await ActivateAsync(pipeline, packageId);

        result.Success.ShouldBeTrue();
        result.Issues.ShouldContain(issue => issue.Code == expectedCode && issue.Severity == ActivationIssueSeverity.Warning);
        harness.State.LastProcessedEventId.ShouldBeGreaterThan(0);
        lease.IsHeld.ShouldBe(failure != FollowUpFailure.RefreshDependencyUnavailable);
    }

    // N2: an unexpected follow-up failure is surfaced, never reported as success.
    [Theory]
    [InlineData(FollowUpFailure.ReindexTriggerProgrammerError, typeof(InvalidOperationException))]
    [InlineData(FollowUpFailure.RefreshIndependentlyCancelled, typeof(OperationCanceledException))]
    [InlineData(FollowUpFailure.TransitionSchedulerProgrammerError, typeof(ArgumentOutOfRangeException))]
    public async Task GivenUnexpectedFollowUpFailureAfterDurableActivation_WhenActivated_ThenTheFailurePropagates(
        FollowUpFailure failure,
        Type expectedException)
    {
        using var harness = new Harness();
        var (pipeline, packageId) = CreatePipelineFailingAfterAppend(harness, failure, TestConformanceLease.NotHeld());

        var exception = await Record.ExceptionAsync(() => ActivateAsync(pipeline, packageId));

        exception.ShouldBeOfType(expectedException);
        harness.State.LastProcessedEventId.ShouldBeGreaterThan(0);
    }

    // N2, §4.4: a durable hide event schedules its transition even when the caller cancels and the refresh fails.
    [Fact]
    public async Task GivenTheCallerCancelsAfterTheDurableAppend_WhenTheRefreshFails_ThenTheTransitionIsStillScheduledAndTheFailureSurfaces()
    {
        using var harness = new Harness();
        harness.Seed(Activated(BaseIdentifier, "identifier", 1, package: CorePackage));
        using var caller = new CancellationTokenSource();
        harness.Log.AfterAppend = caller.Cancel;
        var lease = TestConformanceLease.NotHeld();
        var pipeline = harness.Pipeline([Shadow()], tenants: TestConformanceRefresher.FailingTenants(new InvalidOperationException("Injected refresh failure.")), lease: lease);

        await Should.ThrowAsync<InvalidOperationException>(() => pipeline.ActivateAsync("test.override", "1.0.0", caller.Token));

        harness.State.LastProcessedEventId.ShouldBe(harness.Log.LastEventId);
        harness.State.FindByCanonical(Canonical("test.override"))!.Status.ShouldBe(SearchParameterStatus.Staged);
        await harness.Scheduler.Received(1).ScheduleAsync(harness.Log.LastEventId, TestPackageActivationPipeline.TransitionGrace, CancellationToken.None);
        lease.IsHeld.ShouldBeFalse();
    }

    // §9: an append lost to another writer catches up and retries against the winner, a bounded number of times.
    [Theory]
    [InlineData(1, true, 2)]
    [InlineData(5, false, 3)]
    public async Task GivenOtherWritersWinTheAppend_WhenActivated_ThenItCatchesUpAndRetriesUpToTheBoundBeforeAConformanceConflict(
        int lostAppends,
        bool expectActivated,
        int expectedAttempts)
    {
        using var harness = new Harness();
        foreach (var index in Enumerable.Range(1, lostAppends))
        {
            harness.Log.OtherWriters.Enqueue(PackageActivated($"other-{index}"));
        }

        var result = await ActivateAsync(harness.Pipeline(Custom()), "test.custom");

        result.Success.ShouldBe(expectActivated, Describe(result));
        result.Issues.Any(issue => issue.Code == "CONFORMANCE_CONFLICT").ShouldBe(!expectActivated);
        harness.State.Packages.ShouldContainKey("other-1@1.0.0");
        harness.State.Packages.ContainsKey("test.custom@1.0.0").ShouldBe(expectActivated);
        harness.Log.AppendAttempts.ShouldBe(expectedAttempts);
    }

    // N3: activating a package that is already active (a restart reloading it) appends nothing and starts no reindex.
    [Fact]
    public async Task GivenAnActivePackage_WhenActivatedAgain_ThenNothingIsAppendedOrReindexed()
    {
        using var harness = new Harness();
        var pipeline = harness.Pipeline(Custom());
        (await ActivateAsync(pipeline, "test.custom")).PendingReindex.ShouldBe(["Patient"]);
        var eventCount = harness.Log.Events.Count;

        var again = await ActivateAsync(pipeline, "test.custom");

        (again.Success, again.PendingReindex.Count).ShouldBe((true, 0));
        harness.Log.Events.Count.ShouldBe(eventCount);
    }

    // N6: one package activation is one phase-two transition, however many codes it hides.
    [Fact]
    public async Task GivenPackageShadowsSeventyCodes_WhenActivated_ThenOneTransitionIsScheduledForThePackageEvent()
    {
        const int shadowCount = 70;
        using var harness = new Harness();
        var shadows = Enumerable.Range(1, shadowCount).Select(index =>
        {
            harness.Seed(Activated(Canonical($"seed-{index}"), $"code-{index}", index, package: "seed@1.0.0", expression: "Patient.identifier"));
            return SearchParameter("test.shadows", Canonical($"shadow-{index}"), $"code-{index}", expression: "Patient.identifier", derivedFrom: Canonical($"seed-{index}"));
        });

        var pipeline = harness.Pipeline([.. shadows]);

        var result = await ActivateAsync(pipeline, "test.shadows");

        result.Success.ShouldBeTrue(Describe(result));
        var packageEventId = harness.Log.Events.Single(row => row.Data is PackageActivated { PackageId: "test.shadows" }).EventId;
        harness.Scheduler.ReceivedCalls().ShouldHaveSingleItem();
        await harness.Scheduler.Received(1).ScheduleAsync(packageEventId, TestPackageActivationPipeline.TransitionGrace, CancellationToken.None);
        harness.State.GetTransitionCandidates(packageEventId).Count.ShouldBe(shadowCount);
    }

    // §7: package load reports PendingReindex with the job id, or why no job started.
    [Theory]
    [InlineData("job-1", false, null, null, null)]
    [InlineData("active-job", true, null, "REINDEX_QUEUED", ActivationIssueSeverity.Information)]
    [InlineData(null, false, "Automatic reindex is disabled.", "REINDEX_NOT_STARTED", ActivationIssueSeverity.Warning)]
    public async Task GivenPendingParameters_WhenActivated_ThenTheReindexTriggerOutcomeIsReported(
        string? jobId,
        bool queuedBehindActiveJob,
        string? reason,
        string? expectedCode,
        ActivationIssueSeverity? expectedSeverity)
    {
        using var harness = new Harness();
        var trigger = Substitute.For<IReindexTrigger>();
        trigger.RequestReindexAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReindexTriggerResult(jobId, queuedBehindActiveJob, reason));
        var pipeline = harness.Pipeline([Custom()], trigger);

        var result = await ActivateAsync(pipeline, "test.custom");

        result.Success.ShouldBeTrue();
        result.PendingReindex.ShouldBe(["Patient"]);
        result.ReindexJobId.ShouldBe(jobId);
        result.Issues.Select(issue => (issue.Code, (ActivationIssueSeverity?)issue.Severity))
            .ShouldBe(expectedCode is null ? [] : [(expectedCode, expectedSeverity)]);
    }

    private static Task<ActivationResult> ActivateAsync(PackageActivationPipeline pipeline, string packageId) =>
        pipeline.ActivateAsync(packageId, "1.0.0", CancellationToken.None);

    private static string Describe(ActivationResult result) =>
        string.Join("; ", result.Issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    // Custom parameters become Pending and reach the reindex trigger; overrides become Staged and reach the
    // transition scheduler. Either way the activation is durable before the injected failure.
    private static (PackageActivationPipeline Pipeline, string PackageId) CreatePipelineFailingAfterAppend(
        Harness harness,
        FollowUpFailure failure,
        ConformanceLease lease)
    {
        var trigger = TestPackageActivationPipeline.NoJobTrigger();
        var tenants = TestConformanceRefresher.Tenants();
        switch (failure)
        {
            case FollowUpFailure.ReindexTriggerDeferred:
                trigger.RequestReindexAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(new ReindexTriggerResult(null, false, "Reconciliation will retry.", Deferred: true));
                break;
            case FollowUpFailure.ReindexTriggerProgrammerError:
                trigger.RequestReindexAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns<Task<ReindexTriggerResult>>(_ => throw new InvalidOperationException("Unsupported trigger result."));
                break;
            case FollowUpFailure.RefreshDependencyUnavailable:
                tenants = TestConformanceRefresher.FailingTenants(new IOException("Database unavailable."));
                break;
            case FollowUpFailure.RefreshIndependentlyCancelled:
                tenants = TestConformanceRefresher.FailingTenants(new OperationCanceledException("Independent cancellation."));
                break;
            case FollowUpFailure.TransitionSchedulingFailed:
                harness.Scheduler.ScheduleAsync(Arg.Any<long>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
                    .Returns(_ => throw new InvalidOperationException("Scheduling failed."));
                break;
            case FollowUpFailure.TransitionSchedulerProgrammerError:
                harness.Scheduler.ScheduleAsync(Arg.Any<long>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
                    .Returns(_ => throw new ArgumentOutOfRangeException(nameof(failure), "Transition grace out of range."));
                break;
        }

        var custom = failure is FollowUpFailure.ReindexTriggerDeferred or FollowUpFailure.ReindexTriggerProgrammerError;
        return (harness.Pipeline([Custom(), Shadow()], trigger, tenants, lease), custom ? "test.custom" : "test.override");
    }

    // Activates test.first's shadow (optionally enabling and then removing it, which restages the base), optionally
    // commits the transition still pending, then activates test.second's shadow of the same code.
    private static async Task<ActivationResult> ActivateSecondShadowAsync(bool removeFirst, bool commitPendingTransition)
    {
        using var harness = new Harness();
        var pipeline = harness.Pipeline(Shadow("test.first", derivedFrom: null), Shadow("test.second", derivedFrom: null));
        (await ActivateAsync(pipeline, "test.first")).Success.ShouldBeTrue();
        if (removeFirst)
        {
            var first = harness.CommitTransitionOf(Canonical("test.first"));
            harness.Seed(ReindexStarted(first.Canonical, first.Code, first.ActivationEventId, "job"));
            harness.Seed(ReindexCompleted(first.Canonical, first.Code, first.ActivationEventId, "job"));
            harness.Seed(PackageDeactivated("test.first", "1.0.0"));
            harness.State.GetLatestNonDisabledActivation("Patient", "identifier")!.SourcePackage.ShouldBe(CorePackage);
        }

        if (commitPendingTransition)
        {
            harness.CommitTransitionOf(removeFirst ? BaseIdentifier : Canonical("test.first"));
        }

        return await ActivateAsync(pipeline, "test.second");
    }

    /// <summary>A real pipeline over an in-memory event log, with a substituted transition scheduler.</summary>
    private sealed class Harness : IDisposable
    {
        public InMemoryEventLog Log { get; } = new();

        public ConformanceState State { get; } = new();

        public ISearchParameterTransitionScheduler Scheduler { get; } = Substitute.For<ISearchParameterTransitionScheduler>();

        public IEnumerable<SearchParameterActivated> Activations =>
            Log.Events.Select(row => row.Data).OfType<SearchParameterActivated>();

        public long Seed(object data) => Log.Seed(State, data);

        /// <summary>Commits the transition that swaps Patient.identifier's owner for <paramref name="incoming"/>.</summary>
        public ActiveSearchParameter CommitTransitionOf(string incoming)
        {
            var staged = State.FindByCanonical(incoming)!;
            var outgoing = State.GetSearchParameter("Patient", "identifier")!;
            Seed(new SearchParameterTransitionCommitted(staged.SearchParamId, [staged.ActivationEventId], [outgoing.DeactivationEventId!.Value]));
            return staged;
        }

        public PackageActivationPipeline Pipeline(params PackageResource[] resources) => Pipeline(resources, null);

        public PackageActivationPipeline Pipeline(
            PackageResource[] resources,
            IReindexTrigger? trigger = null,
            ITenantConfigurationStore? tenants = null,
            ConformanceLease? lease = null) =>
            TestPackageActivationPipeline.Create(Repository(resources), Log, State, Scheduler, tenants, trigger, lease);

        public void Dispose() => State.Dispose();
    }
}
