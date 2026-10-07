using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Search.Definition;
using NSubstitute;
using Shouldly;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Conformance;

public class SearchParameterTransitionCommitterTests
{
    [Fact]
    public async Task GivenMatchingStagedTransition_WhenCommitted_ThenItPromotesTheParameterAndRequestsReindex()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(Activation(10, "http://hl7.org/fhir/SearchParameter/Patient-identifier", null, "hl7.fhir.r4.core@4.0.1"));
        state.ApplyAndTrack(Activation(20, "http://example.org/SearchParameter/Patient-identifier", "http://hl7.org/fhir/SearchParameter/Patient-identifier"));
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(EmptyEvents());
        store.AppendAsync(Arg.Any<IEnumerable<NewSourceEvent>>(), 20, Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<SourceEvent>>(
                [new SourceEvent(
                    30,
                    "transition:20",
                    nameof(SearchParameterTransitionCommitted),
                    call.Arg<IEnumerable<NewSourceEvent>>().Single().Data,
                    DateTimeOffset.UtcNow)]));
        var trigger = Substitute.For<IReindexTrigger>();
        var committer = new SearchParameterTransitionCommitter(
            store,
            state,
            trigger,
            Substitute.For<IFhirVersionContext>());

        var committed = await committer.CommitAsync(20, CancellationToken.None);

        committed.ShouldBeTrue();
        state.GetSearchParameter("Patient", "identifier")!.Canonical.ShouldBe("http://example.org/SearchParameter/Patient-identifier");
        state.GetSearchParameter("Patient", "identifier")!.Status.ShouldBe(SearchParameterStatus.Pending);
        await trigger.Received(1).RequestReindexAsync("Search parameter transition 20 committed", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenAlreadyCommittedTransition_WhenCommittedAgain_ThenItCompletesWithoutAppendingOrTriggering()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(Activation(10, "http://hl7.org/fhir/SearchParameter/Patient-identifier", null, "hl7.fhir.r4.core@4.0.1"));
        state.ApplyAndTrack(Activation(20, "http://example.org/SearchParameter/Patient-identifier", "http://hl7.org/fhir/SearchParameter/Patient-identifier"));
        state.ApplyAndTrack(new SourceEvent(
            30,
            "transition:20",
            nameof(SearchParameterTransitionCommitted),
            new SearchParameterTransitionCommitted(1, [20], [20]),
            DateTimeOffset.UtcNow));
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(EmptyEvents());
        var trigger = Substitute.For<IReindexTrigger>();
        var committer = new SearchParameterTransitionCommitter(
            store,
            state,
            trigger,
            Substitute.For<IFhirVersionContext>());

        var committed = await committer.CommitAsync(20, CancellationToken.None);

        committed.ShouldBeFalse();
        await store.DidNotReceive().AppendAsync(
            Arg.Any<IEnumerable<NewSourceEvent>>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
        await trigger.DidNotReceive().RequestReindexAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenUnrelatedEventWinsFirstAppend_WhenCommitted_ThenItCatchesUpAndRetries()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(Activation(10, "http://hl7.org/fhir/SearchParameter/Patient-identifier", null, "hl7.fhir.r4.core@4.0.1"));
        state.ApplyAndTrack(Activation(20, "http://example.org/SearchParameter/Patient-identifier", "http://hl7.org/fhir/SearchParameter/Patient-identifier"));
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(
                EmptyEvents(),
                EmptyEvents(),
                Events(new SourceEvent(30, "unrelated", "Unrelated", new object(), DateTimeOffset.UtcNow)));
        store.AppendAsync(Arg.Any<IEnumerable<NewSourceEvent>>(), 20, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<SourceEvent>>(new SourceEventConcurrencyException(20, 30)));
        store.AppendAsync(Arg.Any<IEnumerable<NewSourceEvent>>(), 30, Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<SourceEvent>>(
                [new SourceEvent(
                    40,
                    "transition:20",
                    nameof(SearchParameterTransitionCommitted),
                    call.Arg<IEnumerable<NewSourceEvent>>().Single().Data,
                    DateTimeOffset.UtcNow)]));
        var trigger = Substitute.For<IReindexTrigger>();
        var committer = new SearchParameterTransitionCommitter(
            store,
            state,
            trigger,
            Substitute.For<IFhirVersionContext>());

        var committed = await committer.CommitAsync(20, CancellationToken.None);

        committed.ShouldBeTrue();
        await store.Received(1).AppendAsync(Arg.Any<IEnumerable<NewSourceEvent>>(), 30, Arg.Any<CancellationToken>());
        await trigger.Received(1).RequestReindexAsync("Search parameter transition 20 committed", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenNewerTransitionMakesCandidateObsolete_WhenCommitted_ThenItStopsWithoutRetryingAppend()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(Activation(10, "http://hl7.org/fhir/SearchParameter/Patient-identifier", null, "hl7.fhir.r4.core@4.0.1"));
        state.ApplyAndTrack(Activation(20, "http://example.org/SearchParameter/Patient-identifier", "http://hl7.org/fhir/SearchParameter/Patient-identifier"));
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(
                EmptyEvents(),
                EmptyEvents(),
                Events(new SourceEvent(
                    30,
                    "transition:20",
                    nameof(SearchParameterTransitionCommitted),
                    new SearchParameterTransitionCommitted(1, [20], [20]),
                    DateTimeOffset.UtcNow)));
        store.AppendAsync(Arg.Any<IEnumerable<NewSourceEvent>>(), 20, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<SourceEvent>>(new SourceEventConcurrencyException(20, 30)));
        var trigger = Substitute.For<IReindexTrigger>();
        var committer = new SearchParameterTransitionCommitter(
            store,
            state,
            trigger,
            Substitute.For<IFhirVersionContext>());

        var committed = await committer.CommitAsync(20, CancellationToken.None);

        committed.ShouldBeFalse();
        await store.Received(1).AppendAsync(Arg.Any<IEnumerable<NewSourceEvent>>(), 20, Arg.Any<CancellationToken>());
        await trigger.DidNotReceive().RequestReindexAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenPersistentConcurrencyConflicts_WhenCommitted_ThenItThrowsAfterBoundedRetries()
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(Activation(10, "http://hl7.org/fhir/SearchParameter/Patient-identifier", null, "hl7.fhir.r4.core@4.0.1"));
        state.ApplyAndTrack(Activation(20, "http://example.org/SearchParameter/Patient-identifier", "http://hl7.org/fhir/SearchParameter/Patient-identifier"));
        var store = Substitute.For<ISourceEventStore>();
        store.ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(EmptyEvents());
        store.AppendAsync(Arg.Any<IEnumerable<NewSourceEvent>>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<SourceEvent>>(new SourceEventConcurrencyException(20, 30)));
        var committer = new SearchParameterTransitionCommitter(
            store,
            state,
            Substitute.For<IReindexTrigger>(),
            Substitute.For<IFhirVersionContext>());

        await Should.ThrowAsync<SourceEventConcurrencyException>(() => committer.CommitAsync(20, CancellationToken.None));

        await store.Received(3).AppendAsync(
            Arg.Any<IEnumerable<NewSourceEvent>>(),
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
    }

    private static SourceEvent Activation(long eventId, string canonical, string? overrides, string sourcePackage = "custom.package@1.0") =>
        new(
            eventId,
            "transition-test",
            nameof(SearchParameterActivated),
            new SearchParameterActivated(
                canonical,
                "identifier",
                "Patient",
                "Patient.identifier",
                SearchParamType.Token,
                sourcePackage,
                overrides is null ? null : new OverrideInfo(overrides, 1),
                1,
                null,
                null,
                null,
                null),
            DateTimeOffset.UtcNow);

    private static async IAsyncEnumerable<SourceEvent> EmptyEvents()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<SourceEvent> Events(params SourceEvent[] events)
    {
        foreach (var evt in events)
        {
            yield return evt;
        }

        await Task.CompletedTask;
    }
}
