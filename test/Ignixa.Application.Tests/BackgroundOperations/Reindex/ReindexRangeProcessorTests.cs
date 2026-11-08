using DurableTask.Core.Serializing;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Workers;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Indexing;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexRangeProcessorTests
{
    private static readonly ReindexRangeInput Range = new("job", 1, "Patient", 1, 20, 42, 20, 0);

    // §8.4: a timed-out write halves its batch size and retries.
    [Fact]
    public async Task GivenWriteTimeout_WhenRangeIsProcessed_ThenBatchSizeIsHalvedAndRetried()
    {
        var store = PageStore(20);
        var writeSizes = new List<int>();
        store.UpdateSearchIndicesAsync(Arg.Any<IReadOnlyList<ReindexResource>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var count = call.Arg<IReadOnlyList<ReindexResource>>().Count;
                writeSizes.Add(count);
                return writeSizes.Count == 1
                    ? throw new TimeoutException("SQL command timed out.")
                    : new SearchIndexUpdateResult(count, 0);
            });

        var result = await CreateProcessor(store).ProcessAsync(Range, CancellationToken.None);

        writeSizes.ShouldBe([20, 10, 10]);
        result.ResourcesReindexed.ShouldBe(20);
    }

    // M5, §8.4: a worker still behind after one refresh returns a typed not-ready output without reading.
    [Fact]
    public async Task GivenDefinitionsStillBehindAfterRefresh_WhenRangeIsProcessed_ThenItReturnsNotReadyWithoutReadingOrThrowing()
    {
        var store = PageStore(1);
        var versions = Versions(Substitute.For<ISearchIndexer>(), 41, 41);
        var eventStore = TestConformanceRefresher.EmptyEventStore();

        var result = await CreateProcessor(store, versions: versions, eventStore: eventStore).ProcessAsync(Range, CancellationToken.None);

        var crossed = JsonDataConverter.Default.Deserialize<ReindexRangeOutput>(JsonDataConverter.Default.Serialize(result));
        crossed.IsDefinitionsNotReady.ShouldBeTrue();
        crossed.StaleDefinitionsEventId.ShouldBe(41);
        _ = eventStore.Received(1).ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        versions.Received(2).GetDefinitionsHandle(FhirVersion.R4, 1);
        await store.DidNotReceiveWithAnyArgs().ReadRangeAsync(default!, default, default, default, default, default);
    }

    // M5, §8.4 steps 3 and 6 (M10, F11): refreshed definitions, background context, failures counted. p1, p103 extract.
    [Fact]
    public async Task GivenACallerContextAndMoreFailuresThanTheSample_WhenRangeIsProcessed_ThenItExtractsInTheBackgroundAndCountsEveryFailure()
    {
        var caller = new FhirRequestContext { TenantId = 99 };
        var accessor = new RecordingContextAccessor { RequestContext = caller };
        var observed = new List<IFhirRequestContext?>();
        var store = PageStore(ReindexTenantProgress.FailedResourceSampleSize + 3);
        var indexer = Indexer(resource =>
        {
            observed.Add(accessor.RequestContext);
            if (resource.Scalar("id") is not ("p1" or "p103"))
            {
                throw new InvalidOperationException("bad expression");
            }
        });

        var result = await CreateProcessor(store, versions: Versions(indexer, 41, 42), accessor: accessor).ProcessAsync(Range, CancellationToken.None);

        var background = observed.Distinct().ShouldHaveSingleItem().ShouldNotBeNull();
        background.ShouldNotBeSameAs(caller);
        background.IsBackgroundTask.ShouldBeTrue();
        background.TenantId.ShouldBe(1);
        background.FhirVersion.ShouldBe(FhirVersion.R4);
        background.ResourceType.ShouldBe("Patient");
        accessor.RequestContext.ShouldBeSameAs(caller);
        result.ResourcesReindexed.ShouldBe(2);
        result.FailedResourceCount.ShouldBe(ReindexTenantProgress.FailedResourceSampleSize + 1);
        result.FailedResources.Count.ShouldBe(ReindexTenantProgress.FailedResourceSampleSize);
        result.FailedResources[0].ShouldBe(new ReindexFailedResource("Patient", "p2", "bad expression"));
        await store.Received(1).UpdateSearchIndicesAsync(
            Arg.Is<IReadOnlyList<ReindexResource>>(batch =>
                string.Join(",", batch.Select(resource => resource.Resource.ResourceId)) == "p1,p103" &&
                batch.All(resource => resource.Resource.DefinitionsEventId == 42)),
            Arg.Any<CancellationToken>());
    }

    // §8.4 step 3 (M10): a range whose read throws extracts nothing and still restores the caller's context.
    [Fact]
    public async Task GivenACallerContext_WhenTheRangeReadThrows_ThenTheCallersContextIsRestored()
    {
        var accessor = new RecordingContextAccessor { RequestContext = new FhirRequestContext { TenantId = 99 } };
        var caller = accessor.RequestContext;
        var extractions = 0;
        var store = PageStore(1);
        store.ReadRangeAsync(default!, default, default, default, default, default)
            .ReturnsForAnyArgs<IReadOnlyList<ReindexResource>>(_ => throw new InvalidOperationException("read failed"));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            CreateProcessor(store, Indexer(_ => extractions++), accessor: accessor).ProcessAsync(Range, CancellationToken.None));

        extractions.ShouldBe(0);
        accessor.RequestContext.ShouldBeSameAs(caller);
    }

    // Spec §8.4 step 5: with a query delay the worker waits after each page before reading the next one.
    [Fact]
    public async Task GivenAQueryDelay_WhenAPageIsWritten_ThenTheWorkerWaitsBeforeTheNextRead()
    {
        using var cancellation = new CancellationTokenSource();
        var store = PageStore(1);
        store.UpdateSearchIndicesAsync(Arg.Any<IReadOnlyList<ReindexResource>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                // Cancelling here makes the wait observable: it can only fail if it was started.
                cancellation.Cancel();
                return new SearchIndexUpdateResult(1, 0);
            });

        await Should.ThrowAsync<OperationCanceledException>(() => CreateProcessor(store)
            .ProcessAsync(Range with { QueryDelayIntervalInMilliseconds = int.MaxValue }, cancellation.Token));

        await store.ReceivedWithAnyArgs(1).ReadRangeAsync(default!, default, default, default, default, default);
    }

    /// <summary>An indexer that runs <paramref name="onExtract"/> for each resource and extracts nothing.</summary>
    private static ISearchIndexer Indexer(Action<IElement> onExtract)
    {
        var indexer = Substitute.For<ISearchIndexer>();
        indexer.Extract(Arg.Any<IElement>()).Returns(call =>
        {
            onExtract(call.Arg<IElement>());
            return [];
        });
        return indexer;
    }

    /// <summary>Definitions for tenant 1 at each successive event id, one per lookup.</summary>
    private static IFhirVersionContext Versions(ISearchIndexer indexer, long definitionsEventId, params long[] laterEventIds)
    {
        var versions = Substitute.For<IFhirVersionContext>();
        var first = Handle(definitionsEventId);
        var later = laterEventIds.Select(Handle).ToArray();
        versions.GetDefinitionsHandle(FhirVersion.R4, 1).Returns(first, later);
        return versions;

        DefinitionsHandle Handle(long eventId) => new(indexer, FhirVersion.R4.GetSchemaProvider(), eventId);
    }

    /// <summary>A processor for tenant 1 whose definitions are at the target event 42 unless <paramref name="versions"/> says otherwise.</summary>
    private static ReindexRangeProcessor CreateProcessor(
        IReindexStore store,
        ISearchIndexer? indexer = null,
        IFhirVersionContext? versions = null,
        ISourceEventStore? eventStore = null,
        IFhirRequestContextAccessor? accessor = null)
    {
        versions ??= Versions(indexer ?? Indexer(_ => { }), 42);
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>()).Returns(ReindexTestHelper.Tenant(1));
        var stores = Substitute.For<IReindexStoreFactory>();
        stores.GetReindexStoreAsync(1, Arg.Any<CancellationToken>()).Returns(store);
        var refresher = TestConformanceRefresher.Create(new ConformanceState(), eventStore);
        return new ReindexRangeProcessor(stores, tenants, versions, refresher, accessor ?? new FhirRequestContextAccessor());
    }

    /// <summary>A store whose range 1..20 holds one page of <paramref name="count"/> patients, all written.</summary>
    private static IReindexStore PageStore(int count)
    {
        var store = Substitute.For<IReindexStore>();
        var resources = Enumerable.Range(1, count)
            .Select(index => new ReindexResource(
                new ResourceWrapper("Patient", $"p{index}", "1", DateTimeOffset.UtcNow,
                    ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"p{{index}}"}"""), new ResourceRequest("PUT", $"Patient/p{index}")),
                index))
            .ToArray();
        store.ReadRangeAsync("Patient", 1, 20, Arg.Any<int>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<long?>(4).HasValue ? [] : resources);
        store.UpdateSearchIndicesAsync(Arg.Any<IReadOnlyList<ReindexResource>>(), Arg.Any<CancellationToken>())
            .Returns(call => new SearchIndexUpdateResult(call.Arg<IReadOnlyList<ReindexResource>>().Count, 0));
        return store;
    }

    // A plain property, unlike the AsyncLocal accessor, so what the processor restores is observable afterwards.
    private sealed class RecordingContextAccessor : IFhirRequestContextAccessor
    {
        public IFhirRequestContext? RequestContext { get; set; }
    }
}
