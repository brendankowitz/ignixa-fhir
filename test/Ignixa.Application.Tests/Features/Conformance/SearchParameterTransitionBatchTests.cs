using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;
using Ignixa.Conformance.Events.Models;
using Ignixa.Search.Definition;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using SearchParamType = Ignixa.Specification.ValueSets.Normative.SearchParamType;

namespace Ignixa.Application.Tests.Features.Conformance;

/// <summary>
/// One package activation is one phase-two transition, however many codes it hides.
/// </summary>
public class SearchParameterTransitionBatchTests
{
    private const int ShadowCount = 70;

    [Fact]
    public async Task GivenSeventyShadowsInOneActivation_WhenTheBatchIsCommitted_ThenOneAppendOneReindexRequestAndOneRefreshCommitThemAll()
    {
        var log = new List<SourceEvent>();
        using var state = new ConformanceState();
        Seed(state, log, "seed", "seed", derivedFrom: null);
        var batchId = Seed(state, log, "shadow", "seed", derivedFrom: "seed");
        var store = new CountingEventStore(log);
        var trigger = Substitute.For<IReindexTrigger>();
        var tenants = TestConformanceRefresher.Tenants();
        var committer = new SearchParameterTransitionCommitter(
            store,
            state,
            trigger,
            TestConformanceRefresher.Create(state, tenants: tenants),
            NullLogger<SearchParameterTransitionCommitter>.Instance);

        var committed = await committer.CommitAsync(batchId, CancellationToken.None);

        committed.ShouldBeTrue();
        store.Appends.ShouldHaveSingleItem().Count.ShouldBe(ShadowCount);
        Enumerable.Range(1, ShadowCount).ShouldAllBe(index =>
            state.GetSearchParameter("Patient", Code(index))!.Canonical == Canonical("shadow", index) &&
            state.GetSearchParameter("Patient", Code(index))!.Status == SearchParameterStatus.Pending);
        await trigger.Received(1).RequestReindexAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await tenants.Received(1).GetAllTenantsAsync(Arg.Any<CancellationToken>());
        state.GetTransitionHideEventIds().ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenSeventyShadowsAwaitingTheirTransition_WhenReconciled_ThenOneTransitionIsScheduledForTheBatch()
    {
        var log = new List<SourceEvent>();
        using var state = new ConformanceState();
        Seed(state, log, "seed", "seed", derivedFrom: null);
        var batchId = Seed(state, log, "shadow", "seed", derivedFrom: "seed");
        var scheduler = Substitute.For<ISearchParameterTransitionScheduler>();
        var grace = TimeSpan.FromMinutes(3);
        var reconciler = new SearchParameterTransitionReconciler(
            state,
            scheduler,
            Options.Create(new ConformanceTransitionOptions { TransitionGrace = grace }));

        await reconciler.ReconcileAsync(CancellationToken.None);

        scheduler.ReceivedCalls().ShouldHaveSingleItem();
        await scheduler.Received(1).ScheduleAsync(batchId, grace, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenALaterActivationShadowsACodeOfAnUncommittedBatch_WhenBothBatchesCommit_ThenEachCommitsOnlyTheCodesItStillHides(
        bool laterBatchCommitsFirst)
    {
        var log = new List<SourceEvent>();
        using var state = new ConformanceState();
        Seed(state, log, "seed", "seed", derivedFrom: null, count: 2);
        var firstBatch = Seed(state, log, "first", "seed", derivedFrom: "seed", count: 2);
        var secondBatch = Seed(state, log, "second", "seed", derivedFrom: "first", count: 1);
        var store = new CountingEventStore(log);
        var committer = new SearchParameterTransitionCommitter(
            store,
            state,
            Substitute.For<IReindexTrigger>(),
            TestConformanceRefresher.Create(state),
            NullLogger<SearchParameterTransitionCommitter>.Instance);

        state.GetTransitionParameters(firstBatch).Select(parameter => parameter.Code).Distinct()
            .ShouldBe([Code(2)]);
        state.GetTransitionParameters(secondBatch).Select(parameter => parameter.Code).Distinct()
            .ShouldBe([Code(1)]);
        foreach (var batch in laterBatchCommitsFirst ? new[] { secondBatch, firstBatch } : [firstBatch, secondBatch])
        {
            (await committer.CommitAsync(batch, CancellationToken.None)).ShouldBeTrue();
        }

        store.Appends.Count.ShouldBe(2);
        store.Appends.ShouldAllBe(append => append.Count == 1);
        state.GetSearchParameter("Patient", Code(1))!.Canonical.ShouldBe(Canonical("second", 1));
        state.GetSearchParameter("Patient", Code(2))!.Canonical.ShouldBe(Canonical("first", 2));
        state.GetSearchParameter("Patient", Code(1))!.Status.ShouldBe(SearchParameterStatus.Pending);
        state.GetSearchParameter("Patient", Code(2))!.Status.ShouldBe(SearchParameterStatus.Pending);
        state.FindByCanonical(Canonical("first", 1))!.Status.ShouldBe(SearchParameterStatus.Disabled);
        state.GetTransitionHideEventIds().ShouldBeEmpty();
    }

    /// <summary>
    /// Applies one package activation of <paramref name="count"/> codes and returns its PackageActivated event id.
    /// </summary>
    private static long Seed(
        ConformanceState state,
        List<SourceEvent> log,
        string package,
        string storagePackage,
        string? derivedFrom,
        int count = ShadowCount)
    {
        var packageKey = $"{package}@1.0.0";
        for (var index = 1; index <= count; index++)
        {
            Append(state, log, packageKey, new SearchParameterActivated(
                Canonical(package, index),
                Code(index),
                "Patient",
                "Patient.identifier",
                SearchParamType.Token,
                packageKey,
                derivedFrom is null ? null : new OverrideInfo(Canonical(storagePackage, index), index),
                index,
                null,
                null,
                null,
                null));
        }

        return Append(state, log, packageKey, new PackageActivated(
            package,
            "1.0.0",
            Enumerable.Range(1, count)
                .Select(index => new ActivatedResource("Patient", Canonical(package, index)))
                .ToList()));
    }

    private static long Append(ConformanceState state, List<SourceEvent> log, string packageKey, object data)
    {
        var sourceEvent = new SourceEvent(
            log.Count + 1,
            $"package:{packageKey}",
            data.GetType().Name,
            data,
            DateTimeOffset.UtcNow);
        log.Add(sourceEvent);
        state.ApplyAndTrack(sourceEvent);
        return sourceEvent.EventId;
    }

    private static string Canonical(string package, int index) =>
        $"http://example.org/SearchParameter/{package}-{index}";

    private static string Code(int index) => $"code-{index}";

    private sealed class CountingEventStore(List<SourceEvent> log) : ISourceEventStore
    {
        public List<IReadOnlyList<SourceEvent>> Appends { get; } = [];

        public Task<IReadOnlyList<SourceEvent>> AppendAsync(
            IEnumerable<NewSourceEvent> events,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Conformance writers append at an expected position.");

        public IAsyncEnumerable<SourceEvent> ReadStreamAsync(string streamId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Conformance writers do not read single streams.");

        public Task<IReadOnlyList<SourceEvent>> AppendAsync(
            IEnumerable<NewSourceEvent> events,
            long expectedLastEventId,
            CancellationToken cancellationToken)
        {
            if (expectedLastEventId != log.Count)
            {
                throw new SourceEventConcurrencyException(expectedLastEventId, log.Count);
            }

            SourceEvent[] appended = events
                .Select((sourceEvent, index) => new SourceEvent(
                    log.Count + index + 1,
                    sourceEvent.StreamId,
                    sourceEvent.EventType,
                    sourceEvent.Data,
                    DateTimeOffset.UtcNow))
                .ToArray();
            log.AddRange(appended);
            Appends.Add(appended);
            return Task.FromResult<IReadOnlyList<SourceEvent>>(appended);
        }

        public async IAsyncEnumerable<SourceEvent> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            foreach (var sourceEvent in log.ToArray())
            {
                yield return sourceEvent;
            }
        }

        public async IAsyncEnumerable<SourceEvent> ReadFromAsync(
            long afterEventId,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            foreach (var sourceEvent in log.Where(sourceEvent => sourceEvent.EventId > afterEventId).ToArray())
            {
                yield return sourceEvent;
            }
        }
    }
}
