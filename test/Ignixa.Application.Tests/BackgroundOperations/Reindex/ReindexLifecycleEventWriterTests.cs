using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Domain.Models;
using Ignixa.Specification.ValueSets.Normative;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexLifecycleEventWriterTests
{
    [Fact]
    public async Task GivenAppliedFailure_WhenLifecycleIsWritten_ThenItIsNotReportedAsIgnored()
    {
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation());
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<SourceEvent>());
        long nextEventId = 2;
        store.AppendAsync(
                Arg.Any<IEnumerable<NewSourceEvent>>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<NewSourceEvent>>()
                .Select(evt => new SourceEvent(
                    nextEventId++,
                    evt.StreamId,
                    evt.EventType,
                    evt.Data,
                    DateTimeOffset.UtcNow))
                .ToArray());
        var writer = new ReindexLifecycleEventWriter(store, state);
        var target = new ReindexParameterDefinition(
            "http://example.org/SearchParameter/patient-custom",
            "custom",
            "Patient",
            17,
            1,
            ["Patient"]);
        await writer.StartAsync("job", [target], CancellationToken.None);

        var ignored = await writer.CompleteAsync(
            "job",
            [new ReindexTargetCompletion(target, false, 0, TimeSpan.Zero, "failed")],
            CancellationToken.None);

        ignored.ShouldBeEmpty();
        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(
            Ignixa.Conformance.Events.Models.SearchParameterStatus.Pending);
    }

    [Fact]
    public async Task GivenMultiBaseCanonical_WhenLifecycleCompletes_ThenEachTargetUsesItsOwnOutcome()
    {
        const string canonical = "http://example.org/SearchParameter/shared-custom";
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical, "custom", "Patient", 1));
        state.ApplyAndTrack(Activation(canonical, "custom", "Observation", 2));
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable.Empty<SourceEvent>());
        long nextEventId = 3;
        store.AppendAsync(
                Arg.Any<IEnumerable<NewSourceEvent>>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<NewSourceEvent>>()
                .Select(evt => new SourceEvent(
                    nextEventId++,
                    evt.StreamId,
                    evt.EventType,
                    evt.Data,
                    DateTimeOffset.UtcNow))
                .ToArray());
        var writer = new ReindexLifecycleEventWriter(store, state);
        var patient = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        var observation = new ReindexParameterDefinition(canonical, "custom", "Observation", 18, 2, ["Observation"]);
        await writer.StartAsync("job", [patient, observation], CancellationToken.None);

        var ignored = await writer.CompleteAsync(
            "job",
            [
                new ReindexTargetCompletion(patient, true, 10, TimeSpan.Zero, null),
                new ReindexTargetCompletion(observation, true, 20, TimeSpan.Zero, null)
            ],
            CancellationToken.None);

        ignored.ShouldBeEmpty();
        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(
            Ignixa.Conformance.Events.Models.SearchParameterStatus.Enabled);
        state.GetSearchParameter("Observation", "custom")!.Status.ShouldBe(
            Ignixa.Conformance.Events.Models.SearchParameterStatus.Enabled);
    }

    private static SourceEvent Activation() => Activation(
        "http://example.org/SearchParameter/patient-custom",
        "custom",
        "Patient",
        1);

    private static SourceEvent Activation(
        string canonical,
        string code,
        string resourceType,
        long eventId) => new(
        eventId,
        "search",
        nameof(SearchParameterActivated),
        new SearchParameterActivated(
            canonical,
            code,
            resourceType,
            $"{resourceType}.id",
            SearchParamType.String,
            "example@1.0.0",
            null,
            16 + (int)eventId,
            null,
            null,
            null,
            null),
        DateTimeOffset.UtcNow);
}
