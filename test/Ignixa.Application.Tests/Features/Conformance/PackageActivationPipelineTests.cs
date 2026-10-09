using Ignixa.Abstractions;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Conformance;

public class PackageActivationPipelineTests
{
    private const string BaseCanonical = "http://hl7.org/fhir/SearchParameter/Patient-identifier";
    private const string OverrideCanonical = "http://example.org/SearchParameter/Patient-identifier";

    [Fact]
    public async Task GivenInProcessBaseCode_WhenShadowed_ThenBaseIsMaterialisedAndOverrideIsStaged()
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                "test.override",
                "1.0.0",
                Arg.Any<CancellationToken>())
            .Returns([CreateOverrideResource()]);
        var persistedEvents = new List<SourceEvent>();
        var eventStore = CreateEventStore(persistedEvents);
        using var state = new ConformanceState();
        var transitionScheduler = Substitute.For<ISearchParameterTransitionScheduler>();
        var pipeline = CreatePipeline(
            packageRepository,
            eventStore,
            state,
            transitionScheduler,
            TestConformanceRefresher.Tenants());

        var result = await pipeline.ActivateAsync("test.override", "1.0.0", CancellationToken.None);

        result.Success.ShouldBeTrue();
        var activations = persistedEvents.Select(row => row.Data).OfType<SearchParameterActivated>().ToArray();
        activations.Length.ShouldBe(2);
        activations[0].Canonical.ShouldBe(BaseCanonical);
        activations[0].SourcePackage.ShouldBe("hl7.fhir.r4.core@4.0.1");
        activations[1].Overrides!.OverridesCanonical.ShouldBe(BaseCanonical);
        activations[1].SearchParamId.ShouldBe(activations[0].SearchParamId);
        state.FindByCanonical(BaseCanonical)!.Status.ShouldBe(SearchParameterStatus.Disabling);
        state.FindByCanonical(OverrideCanonical)!.Status.ShouldBe(SearchParameterStatus.Staged);
        await transitionScheduler.Received(1).ScheduleAsync(
            Arg.Any<long>(),
            Arg.Any<TimeSpan>(),
            CancellationToken.None);
    }

    [Fact]
    public async Task GivenInProcessBaseCodeWithoutDerivedFrom_WhenShadowed_ThenItUsesTheBaseIdentity()
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                "test.override",
                "1.0.0",
                Arg.Any<CancellationToken>())
            .Returns([CreateOverrideResource(includeDerivedFrom: false)]);
        var persistedEvents = new List<SourceEvent>();
        using var state = new ConformanceState();
        var pipeline = CreatePipeline(
            packageRepository,
            CreateEventStore(persistedEvents),
            state,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            TestConformanceRefresher.Tenants());

        var result = await pipeline.ActivateAsync("test.override", "1.0.0", CancellationToken.None);

        result.Success.ShouldBeTrue();
        var activations = persistedEvents.Select(row => row.Data).OfType<SearchParameterActivated>().ToArray();
        activations.Length.ShouldBe(2);
        activations[1].Overrides!.OverridesCanonical.ShouldBe(BaseCanonical);
        activations[1].SearchParamId.ShouldBe(activations[0].SearchParamId);
    }

    [Fact]
    public async Task GivenBaseWasMaterialisedByEarlierShadowing_WhenShadowedAgain_ThenNoSecondBaseActivationIsEmitted()
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call => [CreateOverrideResource(
                packageId: call.ArgAt<string>(0),
                canonical: $"http://example.org/SearchParameter/{call.ArgAt<string>(0)}",
                derivedFrom: call.ArgAt<string>(0) == "test.second"
                    ? "http://example.org/SearchParameter/test.first"
                    : null)]);
        var persistedEvents = new List<SourceEvent>();
        using var state = new ConformanceState();
        var pipeline = CreatePipeline(
            packageRepository,
            CreateEventStore(persistedEvents),
            state,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            TestConformanceRefresher.Tenants());

        (await pipeline.ActivateAsync("test.first", "1.0.0", CancellationToken.None)).Success.ShouldBeTrue();
        (await pipeline.ActivateAsync("test.second", "1.0.0", CancellationToken.None)).Success.ShouldBeTrue();

        persistedEvents.Select(row => row.Data)
            .OfType<SearchParameterActivated>()
            .Count(activation => activation.Canonical == BaseCanonical)
            .ShouldBe(1);
    }

    [Fact]
    public async Task GivenOneBaseCanonicalOnTwoTypes_WhenEachTypeIsShadowed_ThenBothUseTheSameIdentity()
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                "test.override",
                "1.0.0",
                Arg.Any<CancellationToken>())
            .Returns([CreateOverrideResource(
                canonical: "http://example.org/SearchParameter/resource-tag",
                baseTypes: ["Patient", "Practitioner"],
                code: "_tag",
                expression: "Resource.meta.tag",
                includeDerivedFrom: false)]);
        var persistedEvents = new List<SourceEvent>();
        using var state = new ConformanceState();
        var pipeline = CreatePipeline(
            packageRepository,
            CreateEventStore(persistedEvents),
            state,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            TestConformanceRefresher.Tenants());

        var result = await pipeline.ActivateAsync("test.override", "1.0.0", CancellationToken.None);

        result.Success.ShouldBeTrue();
        var overrides = persistedEvents.Select(row => row.Data)
            .OfType<SearchParameterActivated>()
            .Where(activation => activation.SourcePackage == "test.override@1.0.0")
            .ToArray();
        overrides.Length.ShouldBe(2);
        overrides.Select(activation => activation.SearchParamId).Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GivenAnotherPackageShadowsDuringTransition_WhenActivated_ThenConflictMatchesPostCommitResult()
    {
        var duringTransition = await ActivateSecondShadowAsync(commitFirstTransition: false);
        var afterTransition = await ActivateSecondShadowAsync(commitFirstTransition: true);

        duringTransition.Success.ShouldBe(afterTransition.Success);
        duringTransition.Issues.Select(issue => issue.Code)
            .ShouldBe(afterTransition.Issues.Select(issue => issue.Code));
        duringTransition.Success.ShouldBeFalse();
        duringTransition.Issues.ShouldContain(issue => issue.Code == "SP_CONFLICT");
    }

    [Theory]
    [InlineData("Patient", "Binary")]
    [InlineData("Binary", "Patient")]
    public async Task GivenSomeBaseTypesHaveNoBaseCode_WhenActivated_ThenAllTypesShareTheResolvedBaseIdentity(
        string firstBaseType,
        string secondBaseType)
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                "test.multi-base",
                "1.0.0",
                Arg.Any<CancellationToken>())
            .Returns([CreateOverrideResource(
                packageId: "test.multi-base",
                canonical: "http://example.org/SearchParameter/multi-identifier",
                baseTypes: [firstBaseType, secondBaseType],
                includeDerivedFrom: false)]);
        var persistedEvents = new List<SourceEvent>();
        using var state = new ConformanceState();
        var pipeline = CreatePipeline(
            packageRepository,
            CreateEventStore(persistedEvents),
            state,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            TestConformanceRefresher.Tenants());

        var result = await pipeline.ActivateAsync("test.multi-base", "1.0.0", CancellationToken.None);

        result.Success.ShouldBeTrue();
        var activations = persistedEvents.Select(row => row.Data).OfType<SearchParameterActivated>().ToArray();
        var baseActivation = activations.Single(activation => activation.SourcePackage == "hl7.fhir.r4.core@4.0.1");
        var shadows = activations.Where(activation => activation.SourcePackage == "test.multi-base@1.0.0").ToArray();
        shadows.Length.ShouldBe(2);
        shadows.ShouldAllBe(activation => activation.SearchParamId == baseActivation.SearchParamId);
        shadows.ShouldAllBe(activation => activation.Overrides!.OverridesCanonical == BaseCanonical);
    }

    [Fact]
    public async Task GivenBaseTypesResolveDifferentCanonicals_WhenActivated_ThenMixedBaseShadowIsRejected()
    {
        var result = await ActivateMultiBaseAsync(["Patient", "Practitioner"]);

        result.Success.ShouldBeFalse();
        result.Issues.ShouldContain(issue => issue.Code == "SP_MIXED_BASE_SHADOW");
    }

    [Theory]
    [InlineData("Patient", "Practitioner")]
    [InlineData("Practitioner", "Patient")]
    public async Task GivenOneBaseTypeIsAlreadyOwned_WhenBaseOrderChanges_ThenMixedRootOutcomeIsStable(
        string firstBaseType,
        string secondBaseType)
    {
        var result = await ActivateMultiBaseAsync([firstBaseType, secondBaseType], preOwnPatient: true);

        result.Success.ShouldBeFalse();
        result.Issues.ShouldContain(issue => issue.Code == "SP_MIXED_BASE_SHADOW");
    }

    [Fact]
    public async Task GivenEnabledPackageIsRemoved_WhenAnotherPackageActivatesDuringRestoration_ThenResultMatchesPostCommit()
    {
        var duringRemoval = await ActivateSecondShadowDuringRemovalAsync(commitRestoration: false);
        var afterRemoval = await ActivateSecondShadowDuringRemovalAsync(commitRestoration: true);

        duringRemoval.Success.ShouldBe(
            afterRemoval.Success,
            string.Join("; ", duringRemoval.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        duringRemoval.Issues.Select(issue => issue.Code)
            .ShouldBe(afterRemoval.Issues.Select(issue => issue.Code));
        afterRemoval.Success.ShouldBeTrue(string.Join("; ", afterRemoval.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    [Fact]
    public async Task GivenUnknownFhirVersionWithSearchParameters_WhenActivated_ThenItIsRejected()
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                "test.future",
                "1.0.0",
                Arg.Any<CancellationToken>())
            .Returns([CreateOverrideResource(
                packageId: "test.future",
                canonical: "http://example.org/SearchParameter/future-identifier",
                includeDerivedFrom: false,
                fhirVersion: "99.0.0")]);
        var persistedEvents = new List<SourceEvent>();
        using var state = new ConformanceState();
        var pipeline = CreatePipeline(
            packageRepository,
            CreateEventStore(persistedEvents),
            state,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            TestConformanceRefresher.Tenants());

        var result = await pipeline.ActivateAsync("test.future", "1.0.0", CancellationToken.None);

        result.Success.ShouldBeFalse();
        result.Issues.ShouldContain(issue => issue.Code == "SP_UNKNOWN_FHIR_VERSION");
        persistedEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenUnavailableOwnerWithoutReplacement_WhenAnotherPackageActivates_ThenValidationIsSkipped()
    {
        var result = await ActivateAgainstDisabledLeftoverAsync(commitDeactivation: false);

        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenDisabledOwnerLeftInLookup_WhenAnotherPackageActivates_ThenRootResolutionIgnoresIt()
    {
        var result = await ActivateAgainstDisabledLeftoverAsync(commitDeactivation: true);

        result.Success.ShouldBeTrue();
        result.PendingReindex.ShouldContain("Patient");
    }

    [Fact]
    public async Task GivenActivationCreatesPendingParameter_WhenActivated_ThenAutomaticReindexOutcomeIsReturned()
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                "test.custom",
                "1.0.0",
                Arg.Any<CancellationToken>())
            .Returns([CreateCustomResource()]);
        var eventStore = Substitute.For<ISourceEventStore>();
        eventStore.AppendAsync(
                Arg.Any<IEnumerable<NewSourceEvent>>(),
                0,
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<NewSourceEvent>>()
                .Select((sourceEvent, index) => new SourceEvent(
                    index + 1,
                    sourceEvent.StreamId,
                    sourceEvent.EventType,
                    sourceEvent.Data,
                    DateTimeOffset.UtcNow))
                .ToArray());
        using var state = new ConformanceState();
        var trigger = Substitute.For<IReindexTrigger>();
        trigger.RequestReindexAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReindexTriggerResult("job-1", false, null));
        var refreshTenants = TestConformanceRefresher.Tenants();
        var pipeline = CreatePipeline(
            packageRepository,
            eventStore,
            state,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            refreshTenants,
            reindexTrigger: trigger);

        var result = await pipeline.ActivateAsync(
            "test.custom",
            "1.0.0",
            CancellationToken.None);

        result.PendingReindex.ShouldNotBeEmpty();
        result.ReindexJobId.ShouldBe("job-1");
        await trigger.Received(1).RequestReindexAsync(
            Arg.Is<string>(value => value.Contains("test.custom", StringComparison.Ordinal)),
            CancellationToken.None);
    }

    [Fact]
    public async Task GivenAutomaticReindexTriggerFailsAfterDurableActivation_WhenActivated_ThenActivationSucceedsDegraded()
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                "test.custom",
                "1.0.0",
                Arg.Any<CancellationToken>())
            .Returns([CreateCustomResource()]);
        var eventStore = Substitute.For<ISourceEventStore>();
        eventStore.AppendAsync(
                Arg.Any<IEnumerable<NewSourceEvent>>(),
                0,
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<NewSourceEvent>>()
                .Select((sourceEvent, index) => new SourceEvent(
                    index + 1,
                    sourceEvent.StreamId,
                    sourceEvent.EventType,
                    sourceEvent.Data,
                    DateTimeOffset.UtcNow))
                .ToArray());
        using var state = new ConformanceState();
        var trigger = Substitute.For<IReindexTrigger>();
        trigger.RequestReindexAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<ReindexTriggerResult>>(_ =>
                throw new ReindexTriggerUnavailableException(
                    "Injected trigger failure.",
                    new IOException("Database unavailable.")));
        var pipeline = CreatePipeline(
            packageRepository,
            eventStore,
            state,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            TestConformanceRefresher.Tenants(),
            reindexTrigger: trigger);

        var result = await pipeline.ActivateAsync(
            "test.custom",
            "1.0.0",
            CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.PendingReindex.ShouldNotBeEmpty();
        result.ReindexTriggerDeferred.ShouldBeTrue();
        result.ReindexMessage.ShouldContain("periodic reconciliation");
    }

    [Fact]
    public async Task GivenAutomaticReindexTriggerHasProgrammerError_WhenActivated_ThenTheFailurePropagates()
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                "test.custom",
                "1.0.0",
                Arg.Any<CancellationToken>())
            .Returns([CreateCustomResource()]);
        var eventStore = Substitute.For<ISourceEventStore>();
        eventStore.AppendAsync(
                Arg.Any<IEnumerable<NewSourceEvent>>(),
                0,
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<NewSourceEvent>>()
                .Select((sourceEvent, index) => new SourceEvent(
                    index + 1,
                    sourceEvent.StreamId,
                    sourceEvent.EventType,
                    sourceEvent.Data,
                    DateTimeOffset.UtcNow))
                .ToArray());
        using var state = new ConformanceState();
        var trigger = Substitute.For<IReindexTrigger>();
        trigger.RequestReindexAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<ReindexTriggerResult>>(_ =>
                throw new InvalidOperationException("Unsupported trigger result."));
        var pipeline = CreatePipeline(
            packageRepository,
            eventStore,
            state,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            TestConformanceRefresher.Tenants(),
            reindexTrigger: trigger);

        await Should.ThrowAsync<InvalidOperationException>(() => pipeline.ActivateAsync(
            "test.custom",
            "1.0.0",
            CancellationToken.None));
    }

    [Fact]
    public async Task GivenUnexpectedRefreshFailureAfterDurableOverrideActivation_WhenActivated_ThenItSurfacesTheFailure()
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                "test.override",
                "1.0.0",
                Arg.Any<CancellationToken>())
            .Returns([CreateOverrideResource()]);
        var eventStore = Substitute.For<ISourceEventStore>();
        var cancellationSource = new CancellationTokenSource();
        IReadOnlyList<SourceEvent> persistedEvents = [];
        eventStore.AppendAsync(
                Arg.Any<IEnumerable<NewSourceEvent>>(),
                1,
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                persistedEvents = callInfo.ArgAt<IEnumerable<NewSourceEvent>>(0)
                    .Select((sourceEvent, index) => new SourceEvent(
                        index + 2,
                        sourceEvent.StreamId,
                        sourceEvent.EventType,
                        sourceEvent.Data,
                        DateTimeOffset.UtcNow))
                    .ToArray();
                cancellationSource.Cancel();
                return Task.FromResult(persistedEvents);
            });
        using var state = new ConformanceState();
        state.ApplyAndTrack(CreateBaseActivation());
        var transitionScheduler = Substitute.For<ISearchParameterTransitionScheduler>();
        var refreshTenants = TestConformanceRefresher.FailingTenants(
            new InvalidOperationException("Injected refresh failure."));
        var lease = TestConformanceLease.NotHeld();
        var logger = Substitute.For<ILogger<PackageActivationPipeline>>();
        var pipeline = new PackageActivationPipeline(
            packageRepository,
            eventStore,
            state,
            Options.Create(new SearchParameterResolutionOptions()),
            transitionScheduler,
            Options.Create(new ConformanceTransitionOptions { TransitionGrace = TimeSpan.FromSeconds(1) }),
            TestConformanceRefresher.Create(state, tenants: refreshTenants),
            lease,
            CreateReindexTrigger(),
            CreateFhirVersionContext(),
            logger);

        await Should.ThrowAsync<InvalidOperationException>(() => pipeline.ActivateAsync(
            "test.override",
            "1.0.0",
            cancellationSource.Token));

        persistedEvents.Count.ShouldBe(2);
        state.LastProcessedEventId.ShouldBe(persistedEvents[^1].EventId);
        state.FindByCanonical(OverrideCanonical)!.Status.ShouldBe(SearchParameterStatus.Staged);
        await transitionScheduler.Received(1).ScheduleAsync(
            persistedEvents[0].EventId,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        await refreshTenants.Received(1).GetAllTenantsAsync(CancellationToken.None);
        lease.LeaseStartUtc.ShouldBeNull();
    }

    [Fact]
    public async Task GivenIndependentRefreshCancellationAfterDurableOverrideActivation_WhenActivated_ThenItSurfacesTheCancellation()
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                "test.override",
                "1.0.0",
                Arg.Any<CancellationToken>())
            .Returns([CreateOverrideResource()]);
        var eventStore = Substitute.For<ISourceEventStore>();
        eventStore.AppendAsync(
                Arg.Any<IEnumerable<NewSourceEvent>>(),
                1,
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<IReadOnlyList<SourceEvent>>(callInfo
                .ArgAt<IEnumerable<NewSourceEvent>>(0)
                .Select((sourceEvent, index) => new SourceEvent(
                    index + 2,
                    sourceEvent.StreamId,
                    sourceEvent.EventType,
                    sourceEvent.Data,
                    DateTimeOffset.UtcNow))
                .ToArray()));
        using var state = new ConformanceState();
        state.ApplyAndTrack(CreateBaseActivation());
        var refreshTenants = TestConformanceRefresher.FailingTenants(
            new OperationCanceledException("Independent cancellation."));
        var pipeline = CreatePipeline(
            packageRepository,
            eventStore,
            state,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            refreshTenants);

        await Should.ThrowAsync<OperationCanceledException>(() => pipeline.ActivateAsync(
            "test.override",
            "1.0.0",
            CancellationToken.None));
    }

    [Fact]
    public async Task GivenRecoverableRefreshFailureAfterDurableOverrideActivation_WhenActivated_ThenItReportsDeferredRefreshWithoutRenewingTheLease()
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                "test.override",
                "1.0.0",
                Arg.Any<CancellationToken>())
            .Returns([CreateOverrideResource()]);
        var eventStore = Substitute.For<ISourceEventStore>();
        eventStore.AppendAsync(
                Arg.Any<IEnumerable<NewSourceEvent>>(),
                1,
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<IReadOnlyList<SourceEvent>>(callInfo
                .ArgAt<IEnumerable<NewSourceEvent>>(0)
                .Select((sourceEvent, index) => new SourceEvent(
                    index + 2,
                    sourceEvent.StreamId,
                    sourceEvent.EventType,
                    sourceEvent.Data,
                    DateTimeOffset.UtcNow))
                .ToArray()));
        using var state = new ConformanceState();
        state.ApplyAndTrack(CreateBaseActivation());
        var refreshTenants = TestConformanceRefresher.FailingTenants(new IOException("Database unavailable."));
        var lease = TestConformanceLease.NotHeld();
        var pipeline = new PackageActivationPipeline(
            packageRepository,
            eventStore,
            state,
            Options.Create(new SearchParameterResolutionOptions()),
            Substitute.For<ISearchParameterTransitionScheduler>(),
            Options.Create(new ConformanceTransitionOptions { TransitionGrace = TimeSpan.FromSeconds(1) }),
            TestConformanceRefresher.Create(state, tenants: refreshTenants),
            lease,
            CreateReindexTrigger(),
            CreateFhirVersionContext(),
            Substitute.For<ILogger<PackageActivationPipeline>>());

        var result = await pipeline.ActivateAsync(
            "test.override",
            "1.0.0",
            CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.LocalRefreshDeferred.ShouldBeTrue();
        lease.LeaseStartUtc.ShouldBeNull();
    }

    [Fact]
    public async Task GivenSchedulingFailsAfterDurableOverrideActivation_WhenActivated_ThenItRemainsDurablySuccessful()
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                "test.override",
                "1.0.0",
                Arg.Any<CancellationToken>())
            .Returns([CreateOverrideResource()]);
        var eventStore = Substitute.For<ISourceEventStore>();
        eventStore.AppendAsync(
                Arg.Any<IEnumerable<NewSourceEvent>>(),
                1,
                Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromResult<IReadOnlyList<SourceEvent>>(callInfo
                .ArgAt<IEnumerable<NewSourceEvent>>(0)
                .Select((sourceEvent, index) => new SourceEvent(
                    index + 2,
                    sourceEvent.StreamId,
                    sourceEvent.EventType,
                    sourceEvent.Data,
                    DateTimeOffset.UtcNow))
                .ToArray()));
        using var state = new ConformanceState();
        state.ApplyAndTrack(CreateBaseActivation());
        var transitionScheduler = Substitute.For<ISearchParameterTransitionScheduler>();
        transitionScheduler.ScheduleAsync(
                Arg.Any<long>(),
                Arg.Any<TimeSpan>(),
                CancellationToken.None)
            .Returns(_ => throw new InvalidOperationException("Scheduling failed."));
        var pipeline = CreatePipeline(
            packageRepository,
            eventStore,
            state,
            transitionScheduler,
            TestConformanceRefresher.Tenants());

        var result = await pipeline.ActivateAsync(
            "test.override",
            "1.0.0",
            CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.TransitionSchedulingDeferred.ShouldBeTrue();
        await transitionScheduler.Received(3).ScheduleAsync(
            2,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
    }

    private static PackageActivationPipeline CreatePipeline(
        IPackageResourceRepository packageRepository,
        ISourceEventStore eventStore,
        ConformanceState state,
        ISearchParameterTransitionScheduler transitionScheduler,
        ITenantConfigurationStore refreshTenants,
        IReindexTrigger? reindexTrigger = null)
    {
        var lease = TestConformanceLease.NotHeld();
        return new PackageActivationPipeline(
            packageRepository,
            eventStore,
            state,
            Options.Create(new SearchParameterResolutionOptions()),
            transitionScheduler,
            Options.Create(new ConformanceTransitionOptions { TransitionGrace = TimeSpan.FromSeconds(1) }),
            TestConformanceRefresher.Create(state, tenants: refreshTenants),
            lease,
            reindexTrigger ?? CreateReindexTrigger(),
            CreateFhirVersionContext(),
            Substitute.For<ILogger<PackageActivationPipeline>>());
    }

    private static IFhirVersionContext CreateFhirVersionContext() =>
        new FhirVersionContext(
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions(),
            NullFhirBaseUriProvider.Instance);

    private static IReindexTrigger CreateReindexTrigger()
    {
        var trigger = Substitute.For<IReindexTrigger>();
        trigger.RequestReindexAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ReindexTriggerResult(null, false, null)));
        return trigger;
    }

    private static ISourceEventStore CreateEventStore(List<SourceEvent> persistedEvents)
    {
        var eventStore = Substitute.For<ISourceEventStore>();
        eventStore.AppendAsync(
                Arg.Any<IEnumerable<NewSourceEvent>>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var nextEventId = persistedEvents.Count + 1;
                SourceEvent[] appended = call.ArgAt<IEnumerable<NewSourceEvent>>(0)
                    .Select((sourceEvent, index) => new SourceEvent(
                        nextEventId + index,
                        sourceEvent.StreamId,
                        sourceEvent.EventType,
                        sourceEvent.Data,
                        DateTimeOffset.UtcNow))
                    .ToArray();
                persistedEvents.AddRange(appended);
                return Task.FromResult<IReadOnlyList<SourceEvent>>(appended);
            });
        return eventStore;
    }

    private static async Task<ActivationResult> ActivateSecondShadowAsync(bool commitFirstTransition)
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                Arg.Any<string>(),
                "1.0.0",
                Arg.Any<CancellationToken>())
            .Returns(call => [CreateOverrideResource(
                packageId: call.ArgAt<string>(0),
                canonical: $"http://example.org/SearchParameter/{call.ArgAt<string>(0)}",
                includeDerivedFrom: false)]);
        var persistedEvents = new List<SourceEvent>();
        using var state = new ConformanceState();
        var pipeline = CreatePipeline(
            packageRepository,
            CreateEventStore(persistedEvents),
            state,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            TestConformanceRefresher.Tenants());
        (await pipeline.ActivateAsync("test.first", "1.0.0", CancellationToken.None)).Success.ShouldBeTrue();

        if (commitFirstTransition)
        {
            var staged = state.FindByCanonical("http://example.org/SearchParameter/test.first")!;
            var outgoing = state.GetSearchParameter("Patient", "identifier")!;
            var committed = new SourceEvent(
                persistedEvents.Count + 1,
                "transition:test",
                nameof(SearchParameterTransitionCommitted),
                new SearchParameterTransitionCommitted(
                    staged.SearchParamId,
                    [staged.ActivationEventId],
                    [outgoing.DeactivationEventId!.Value]),
                DateTimeOffset.UtcNow);
            persistedEvents.Add(committed);
            state.ApplyAndTrack(committed);
        }

        return await pipeline.ActivateAsync("test.second", "1.0.0", CancellationToken.None);
    }

    private static async Task<ActivationResult> ActivateMultiBaseAsync(
        IReadOnlyList<string> baseTypes,
        bool preOwnPatient = false)
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                "test.multi-base",
                "1.0.0",
                Arg.Any<CancellationToken>())
            .Returns([CreateOverrideResource(
                packageId: "test.multi-base",
                canonical: "http://example.org/SearchParameter/multi-identifier",
                baseTypes: baseTypes,
                includeDerivedFrom: false)]);
        var persistedEvents = new List<SourceEvent>();
        using var state = new ConformanceState();
        if (preOwnPatient)
        {
            state.ApplyAndTrack(CreateBaseActivation());
            state.ApplyAndTrack(new SourceEvent(
                2,
                "package:existing.owner@1.0.0",
                nameof(SearchParameterActivated),
                new SearchParameterActivated(
                    "http://example.org/SearchParameter/existing-identifier",
                    "identifier",
                    "Patient",
                    "Patient.identifier",
                    SearchParamType.Token,
                    "existing.owner@1.0.0",
                    new OverrideInfo(BaseCanonical, 1),
                    1,
                    null,
                    null,
                    null,
                    null),
                DateTimeOffset.UtcNow));
            persistedEvents.AddRange([
                CreateBaseActivation(),
                new SourceEvent(
                    2,
                    "package:existing.owner@1.0.0",
                    nameof(SearchParameterActivated),
                    new SearchParameterActivated(
                        "http://example.org/SearchParameter/existing-identifier",
                        "identifier",
                        "Patient",
                        "Patient.identifier",
                        SearchParamType.Token,
                        "existing.owner@1.0.0",
                        new OverrideInfo(BaseCanonical, 1),
                        1,
                        null,
                        null,
                        null,
                        null),
                    DateTimeOffset.UtcNow)
            ]);
        }
        var pipeline = CreatePipeline(
            packageRepository,
            CreateEventStore(persistedEvents),
            state,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            TestConformanceRefresher.Tenants());

        return await pipeline.ActivateAsync("test.multi-base", "1.0.0", CancellationToken.None);
    }

    private static async Task<ActivationResult> ActivateSecondShadowDuringRemovalAsync(bool commitRestoration)
    {
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                Arg.Any<string>(),
                "1.0.0",
                Arg.Any<CancellationToken>())
            .Returns(call => [CreateOverrideResource(
                packageId: call.ArgAt<string>(0),
                canonical: $"http://example.org/SearchParameter/{call.ArgAt<string>(0)}",
                includeDerivedFrom: false)]);
        var persistedEvents = new List<SourceEvent>();
        using var state = new ConformanceState();
        var pipeline = CreatePipeline(
            packageRepository,
            CreateEventStore(persistedEvents),
            state,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            TestConformanceRefresher.Tenants());
        (await pipeline.ActivateAsync("test.first", "1.0.0", CancellationToken.None)).Success.ShouldBeTrue();
        var first = state.FindByCanonical("http://example.org/SearchParameter/test.first")!;
        var baseOwner = state.GetSearchParameter("Patient", "identifier")!;
        ApplyAndRecord(new SearchParameterTransitionCommitted(
            first.SearchParamId,
            [first.ActivationEventId],
            [baseOwner.DeactivationEventId!.Value]));
        ApplyAndRecord(new SearchParameterReindexStarted(
            first.Canonical, first.Code, first.ResourceType, "job", ["Patient"], first.ActivationEventId));
        ApplyAndRecord(new SearchParameterReindexCompleted(
            first.Canonical, first.Code, first.ResourceType, "job", 0, TimeSpan.Zero, first.ActivationEventId));
        ApplyAndRecord(new PackageDeactivated("test.first", "1.0.0", "test"));
        state.GetLatestNonDisabledActivation("Patient", "identifier")!.SourcePackage
            .ShouldBe("hl7.fhir.r4.core@4.0.1");

        if (commitRestoration)
        {
            var restored = state.FindByCanonical(BaseCanonical)!;
            var outgoing = state.GetSearchParameter("Patient", "identifier")!;
            ApplyAndRecord(new SearchParameterTransitionCommitted(
                restored.SearchParamId,
                [restored.ActivationEventId],
                [outgoing.DeactivationEventId!.Value]));
        }

        return await pipeline.ActivateAsync("test.second", "1.0.0", CancellationToken.None);

        void ApplyAndRecord(object data)
        {
            var sourceEvent = new SourceEvent(
                persistedEvents.Count + 1,
                "lifecycle:test",
                data.GetType().Name,
                data,
                DateTimeOffset.UtcNow);
            persistedEvents.Add(sourceEvent);
            state.ApplyAndTrack(sourceEvent);
        }
    }

    private static async Task<ActivationResult> ActivateAgainstDisabledLeftoverAsync(bool commitDeactivation)
    {
        const string oldCanonical = "http://example.org/SearchParameter/old-custom";
        var packageRepository = Substitute.For<IPackageResourceRepository>();
        packageRepository.GetResourcesForActivationAsync(
                "test.new",
                "1.0.0",
                Arg.Any<CancellationToken>())
            .Returns([CreateOverrideResource(
                packageId: "test.new",
                canonical: "http://example.org/SearchParameter/new-custom",
                code: "custom-code",
                expression: "Patient.active",
                includeDerivedFrom: false)]);
        var persistedEvents = new List<SourceEvent>();
        using var state = new ConformanceState();
        ApplyAndRecord(new SearchParameterActivated(
            oldCanonical,
            "custom-code",
            "Patient",
            "Patient.active",
            SearchParamType.Token,
            "test.old@1.0.0",
            null,
            1,
            null,
            null,
            null,
            null));
        ApplyAndRecord(new PackageActivated(
            "test.old",
            "1.0.0",
            [new ActivatedResource("Patient", oldCanonical)]));
        ApplyAndRecord(new PackageDeactivated("test.old", "1.0.0", "test"));
        if (commitDeactivation)
        {
            var outgoing = state.GetSearchParameter("Patient", "custom-code")!;
            ApplyAndRecord(new SearchParameterTransitionCommitted(
                outgoing.SearchParamId,
                [],
                [outgoing.DeactivationEventId!.Value]));
            state.GetSearchParameter("Patient", "custom-code")!.Status.ShouldBe(SearchParameterStatus.Disabled);
        }

        var pipeline = CreatePipeline(
            packageRepository,
            CreateEventStore(persistedEvents),
            state,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            TestConformanceRefresher.Tenants());
        return await pipeline.ActivateAsync("test.new", "1.0.0", CancellationToken.None);

        void ApplyAndRecord(object data)
        {
            var sourceEvent = new SourceEvent(
                persistedEvents.Count + 1,
                "lifecycle:test",
                data.GetType().Name,
                data,
                DateTimeOffset.UtcNow);
            persistedEvents.Add(sourceEvent);
            state.ApplyAndTrack(sourceEvent);
        }
    }

    private static PackageResource CreateOverrideResource(
        string packageId = "test.override",
        string canonical = OverrideCanonical,
        IReadOnlyList<string>? baseTypes = null,
        string code = "identifier",
        string expression = "Resource.identifier",
        bool includeDerivedFrom = true,
        string? derivedFrom = null,
        string fhirVersion = "4.0.1")
    {
        var resource = new System.Text.Json.Nodes.JsonObject
        {
            ["resourceType"] = "SearchParameter",
            ["id"] = "patient-identifier",
            ["url"] = canonical,
            ["code"] = code,
            ["base"] = new System.Text.Json.Nodes.JsonArray(
                (baseTypes ?? ["Patient"]).Select(type => System.Text.Json.Nodes.JsonValue.Create(type)).ToArray()),
            ["type"] = "token",
            ["expression"] = expression
        };
        if (includeDerivedFrom || derivedFrom is not null)
        {
            resource["derivedFrom"] = derivedFrom ?? BaseCanonical;
        }

        return new PackageResource
        {
            PackageId = packageId,
            PackageVersion = "1.0.0",
            ResourceType = "SearchParameter",
            ResourceId = "patient-identifier",
            Canonical = canonical,
            FhirVersion = fhirVersion,
            ResourceJson = resource.ToJsonString()
        };
    }

    private static PackageResource CreateCustomResource() =>
        new()
        {
            PackageId = "test.custom",
            PackageVersion = "1.0.0",
            ResourceType = "SearchParameter",
            ResourceId = "patient-custom",
            Canonical = "http://example.org/SearchParameter/patient-custom",
            FhirVersion = "4.0.1",
            ResourceJson = """
                {
                  "resourceType": "SearchParameter",
                  "id": "patient-custom",
                  "url": "http://example.org/SearchParameter/patient-custom",
                  "code": "custom",
                  "base": ["Patient"],
                  "type": "string",
                  "expression": "Patient.id"
                }
                """
        };

    private static SourceEvent CreateBaseActivation() =>
        new(
            1,
            "package:hl7.fhir.r4.core@4.0.1",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                BaseCanonical,
                "identifier",
                "Patient",
                "Patient.identifier",
                SearchParamType.Token,
                "hl7.fhir.r4.core@4.0.1",
                null,
                1,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow);
}
