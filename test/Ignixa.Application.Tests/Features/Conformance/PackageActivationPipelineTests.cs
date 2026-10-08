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
            Substitute.For<IConformanceCacheRefresher>());

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
            Substitute.For<IConformanceCacheRefresher>());

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
            Substitute.For<IConformanceCacheRefresher>());

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
            Substitute.For<IConformanceCacheRefresher>());

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
    [InlineData("Practitioner")]
    [InlineData("Binary")]
    public async Task GivenMultiBaseParameterDoesNotShareOneBaseCanonical_WhenActivated_ThenItUsesDistinctIdentity(
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
                baseTypes: ["Patient", secondBaseType],
                includeDerivedFrom: false)]);
        var persistedEvents = new List<SourceEvent>();
        using var state = new ConformanceState();
        var pipeline = CreatePipeline(
            packageRepository,
            CreateEventStore(persistedEvents),
            state,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            Substitute.For<IConformanceCacheRefresher>());

        var result = await pipeline.ActivateAsync("test.multi-base", "1.0.0", CancellationToken.None);

        result.Success.ShouldBeTrue();
        var activations = persistedEvents.Select(row => row.Data).OfType<SearchParameterActivated>().ToArray();
        activations.ShouldAllBe(activation => activation.SourcePackage == "test.multi-base@1.0.0");
        activations.ShouldAllBe(activation => activation.Overrides == null);
        activations.Select(activation => activation.SearchParamId).Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GivenUnknownFhirVersion_WhenBaseCodeIsShadowed_ThenActivationSkipsBaseSynthesis()
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
            Substitute.For<IConformanceCacheRefresher>());

        var result = await pipeline.ActivateAsync("test.future", "1.0.0", CancellationToken.None);

        result.Success.ShouldBeTrue();
        persistedEvents.Select(row => row.Data).OfType<SearchParameterActivated>()
            .ShouldHaveSingleItem()
            .Overrides.ShouldBeNull();
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
        var cacheRefresher = Substitute.For<IConformanceCacheRefresher>();
        var pipeline = CreatePipeline(
            packageRepository,
            eventStore,
            state,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            cacheRefresher,
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
            Substitute.For<IConformanceCacheRefresher>(),
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
            Substitute.For<IConformanceCacheRefresher>(),
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
        var cacheRefresher = Substitute.For<IConformanceCacheRefresher>();
        cacheRefresher.BuildSnapshotAsync(
                Arg.Any<ConformanceStateSnapshot>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IConformanceConsumerSnapshot>(
                new InvalidOperationException("Injected refresh failure.")));
        var lease = Substitute.For<IConformanceLease>();
        var leaseStart = new ConformanceLeaseStart(DateTimeOffset.UtcNow, 1);
        lease.CaptureStart().Returns(leaseStart);
        var logger = Substitute.For<ILogger<PackageActivationPipeline>>();
        var pipeline = new PackageActivationPipeline(
            packageRepository,
            eventStore,
            state,
            Options.Create(new SearchParameterResolutionOptions()),
            transitionScheduler,
            Options.Create(new ConformanceTransitionOptions { TransitionGrace = TimeSpan.FromSeconds(1) }),
            CreateRefreshPublisher(state, cacheRefresher),
            lease,
            new NullReindexTrigger(),
            CreateFhirVersionContext(state),
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
        await cacheRefresher.Received(1).BuildSnapshotAsync(
            Arg.Any<ConformanceStateSnapshot>(),
            Arg.Any<long>(),
            CancellationToken.None);
        lease.DidNotReceive().Renew(leaseStart);
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
        var cacheRefresher = Substitute.For<IConformanceCacheRefresher>();
        cacheRefresher.BuildSnapshotAsync(
                Arg.Any<ConformanceStateSnapshot>(),
                Arg.Any<long>(),
                CancellationToken.None)
            .Returns(Task.FromException<IConformanceConsumerSnapshot>(
                new OperationCanceledException("Independent cancellation.")));
        var pipeline = CreatePipeline(
            packageRepository,
            eventStore,
            state,
            Substitute.For<ISearchParameterTransitionScheduler>(),
            cacheRefresher,
            configureSuccessfulRefresh: false);

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
        var cacheRefresher = Substitute.For<IConformanceCacheRefresher>();
        cacheRefresher.BuildSnapshotAsync(
                Arg.Any<ConformanceStateSnapshot>(),
                Arg.Any<long>(),
                CancellationToken.None)
            .Returns(Task.FromException<IConformanceConsumerSnapshot>(
                new ConformanceConsumerRefreshException(
                "Expected refresh failure.",
                new IOException("Database unavailable."))));
        var lease = Substitute.For<IConformanceLease>();
        var leaseStart = new ConformanceLeaseStart(DateTimeOffset.UtcNow, 1);
        lease.CaptureStart().Returns(leaseStart);
        var pipeline = new PackageActivationPipeline(
            packageRepository,
            eventStore,
            state,
            Options.Create(new SearchParameterResolutionOptions()),
            Substitute.For<ISearchParameterTransitionScheduler>(),
            Options.Create(new ConformanceTransitionOptions { TransitionGrace = TimeSpan.FromSeconds(1) }),
            CreateRefreshPublisher(state, cacheRefresher),
            lease,
            new NullReindexTrigger(),
            CreateFhirVersionContext(state),
            Substitute.For<ILogger<PackageActivationPipeline>>());

        var result = await pipeline.ActivateAsync(
            "test.override",
            "1.0.0",
            CancellationToken.None);

        result.Success.ShouldBeTrue();
        result.LocalRefreshDeferred.ShouldBeTrue();
        lease.DidNotReceive().Renew(leaseStart);
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
            Substitute.For<IConformanceCacheRefresher>());

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
        IConformanceCacheRefresher cacheRefresher,
        bool configureSuccessfulRefresh = true,
        IReindexTrigger? reindexTrigger = null)
    {
        var lease = Substitute.For<IConformanceLease>();
        lease.CaptureStart().Returns(new ConformanceLeaseStart(DateTimeOffset.UtcNow, 1));
        if (configureSuccessfulRefresh)
        {
            cacheRefresher.BuildSnapshotAsync(
                    Arg.Any<ConformanceStateSnapshot>(),
                    Arg.Any<long>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo => Task.FromResult<IConformanceConsumerSnapshot>(
                    new TestConsumerSnapshot(callInfo.ArgAt<long>(1))));
        }
        return new PackageActivationPipeline(
            packageRepository,
            eventStore,
            state,
            Options.Create(new SearchParameterResolutionOptions()),
            transitionScheduler,
            Options.Create(new ConformanceTransitionOptions { TransitionGrace = TimeSpan.FromSeconds(1) }),
            CreateRefreshPublisher(state, cacheRefresher),
            lease,
            reindexTrigger ?? new NullReindexTrigger(),
            CreateFhirVersionContext(state),
            Substitute.For<ILogger<PackageActivationPipeline>>());
    }

    private static IFhirVersionContext CreateFhirVersionContext(ConformanceState state) =>
        new FhirVersionContext(
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions(),
            NullFhirBaseUriProvider.Instance,
            conformanceState: state);

    private static ConformanceRefreshPublisher CreateRefreshPublisher(
        ConformanceState state,
        IConformanceCacheRefresher cacheRefresher) =>
        new(state, cacheRefresher, Microsoft.Extensions.Logging.Abstractions.NullLogger<ConformanceRefreshPublisher>.Instance);

    private sealed record TestConsumerSnapshot(long Generation) : IConformanceConsumerSnapshot;

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
            Substitute.For<IConformanceCacheRefresher>());
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
