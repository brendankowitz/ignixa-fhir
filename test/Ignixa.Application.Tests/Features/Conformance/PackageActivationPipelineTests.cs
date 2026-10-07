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
        bool configureSuccessfulRefresh = true)
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
            Substitute.For<ILogger<PackageActivationPipeline>>());
    }

    private static ConformanceRefreshPublisher CreateRefreshPublisher(
        ConformanceState state,
        IConformanceCacheRefresher cacheRefresher) =>
        new(state, cacheRefresher, Microsoft.Extensions.Logging.Abstractions.NullLogger<ConformanceRefreshPublisher>.Instance);

    private sealed record TestConsumerSnapshot(long Generation) : IConformanceConsumerSnapshot;

    private static PackageResource CreateOverrideResource() =>
        new()
        {
            PackageId = "test.override",
            PackageVersion = "1.0.0",
            ResourceType = "SearchParameter",
            ResourceId = "patient-identifier",
            Canonical = OverrideCanonical,
            FhirVersion = "4.0.1",
            ResourceJson = $$"""
                {
                  "resourceType": "SearchParameter",
                  "id": "patient-identifier",
                  "url": "{{OverrideCanonical}}",
                  "code": "identifier",
                  "base": ["Patient"],
                  "type": "token",
                  "expression": "Patient.identifier",
                  "derivedFrom": "{{BaseCanonical}}"
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
