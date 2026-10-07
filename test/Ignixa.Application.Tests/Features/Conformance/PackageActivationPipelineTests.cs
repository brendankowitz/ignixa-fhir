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
    public async Task GivenRefreshFailsAfterDurableOverrideActivation_WhenActivated_ThenItReportsSuccessSchedulesTransitionAndDoesNotRenewLease()
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
        cacheRefresher.RefreshAsync(Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("Injected refresh failure."));
        var lease = Substitute.For<IConformanceLease>();
        var leaseStart = new ConformanceLeaseStart(DateTimeOffset.UtcNow, 1);
        lease.CaptureStart().Returns(leaseStart);
        var logger = Substitute.For<ILogger<PackageActivationPipeline>>();
        var pipeline = new PackageActivationPipeline(
            packageRepository,
            eventStore,
            state,
            Substitute.For<IFhirVersionContext>(),
            Options.Create(new SearchParameterResolutionOptions()),
            transitionScheduler,
            Options.Create(new ConformanceTransitionOptions { TransitionGrace = TimeSpan.FromSeconds(1) }),
            cacheRefresher,
            lease,
            logger);

        var result = await pipeline.ActivateAsync(
            "test.override",
            "1.0.0",
            cancellationSource.Token);

        result.Success.ShouldBeTrue();
        persistedEvents.Count.ShouldBe(2);
        state.LastProcessedEventId.ShouldBe(persistedEvents[^1].EventId);
        state.FindByCanonical(OverrideCanonical)!.Status.ShouldBe(SearchParameterStatus.Staged);
        await transitionScheduler.Received(1).ScheduleAsync(
            persistedEvents[0].EventId,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);
        await cacheRefresher.Received(1).RefreshAsync(CancellationToken.None);
        lease.DidNotReceive().Renew(leaseStart);
        logger.ReceivedCalls()
            .Any(call => Equals(call.GetArguments()[0], LogLevel.Warning))
            .ShouldBeTrue();
    }

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
