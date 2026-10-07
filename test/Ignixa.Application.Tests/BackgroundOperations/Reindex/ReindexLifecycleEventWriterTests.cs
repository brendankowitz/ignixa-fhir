using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
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
        var target = new ReindexTarget(
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

    private static SourceEvent Activation() => new(
        1,
        "search",
        nameof(SearchParameterActivated),
        new SearchParameterActivated(
            "http://example.org/SearchParameter/patient-custom",
            "custom",
            "Patient",
            "Patient.id",
            SearchParamType.String,
            "example@1.0.0",
            null,
            17,
            null,
            null,
            null,
            null),
        DateTimeOffset.UtcNow);
}
