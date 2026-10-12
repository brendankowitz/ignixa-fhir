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
using Ignixa.Specification.ValueSets.Normative;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexRangeProcessorTests
{
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
        var indexer = Substitute.For<ISearchIndexer>();
        indexer.Extract(Arg.Any<IElement>()).Returns([]);

        var result = await CreateProcessor(new FhirRequestContextAccessor(), indexer, store).ProcessAsync(
            new ReindexRangeInput("job", 1, "Patient", 1, 20, 42, 20, 0),
            CancellationToken.None);

        writeSizes.ShouldBe([20, 10, 10]);
        result.ResourcesReindexed.ShouldBe(20);
    }

    [Fact]
    public async Task GivenDefinitionsBehindTargetEvent_WhenRangeIsProcessed_ThenDefinitionsAreRefreshed()
    {
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetDefinitionsHandle(Arg.Any<FhirVersion>(), 1)
            .Returns(
                new DefinitionsHandle(Substitute.For<ISearchIndexer>(), Substitute.For<IFhirSchemaProvider>(), 41),
                new DefinitionsHandle(Substitute.For<ISearchIndexer>(), Substitute.For<IFhirSchemaProvider>(), 42));
        var eventStore = TestConformanceRefresher.EmptyEventStore();

        var result = await CreateProcessor(new FhirRequestContextAccessor(), Substitute.For<IReindexStore>(), versions, eventStore)
            .ProcessAsync(new ReindexRangeInput("job", 1, "Patient", 1, 100, 42, 1000, 0), CancellationToken.None);

        result.ResourcesReindexed.ShouldBe(0);
        _ = eventStore.Received(1).ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        versions.Received(2).GetDefinitionsHandle(FhirVersion.R4, 1);
    }

    [Fact]
    public async Task GivenDefinitionsStillBehindAfterRefresh_WhenRangeIsProcessed_ThenItReturnsNotReadyWithoutReadingOrThrowing()
    {
        var store = SinglePageStore();
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetDefinitionsHandle(FhirVersion.R4, 1)
            .Returns(new DefinitionsHandle(Substitute.For<ISearchIndexer>(), Substitute.For<IFhirSchemaProvider>(), 41));
        var processor = CreateProcessor(new FhirRequestContextAccessor(), store, versions);

        var result = await processor.ProcessAsync(
            new ReindexRangeInput("job", 1, "Patient", 1, 100, 42, 1000, 0),
            CancellationToken.None);

        // The not-ready signal must survive the converter DurableTask uses at the activity boundary.
        var crossed = JsonDataConverter.Default.Deserialize<ReindexRangeOutput>(JsonDataConverter.Default.Serialize(result));
        crossed.IsDefinitionsNotReady.ShouldBeTrue();
        crossed.StaleDefinitionsEventId.ShouldBe(41);
        await store.DidNotReceiveWithAnyArgs().ReadRangeAsync(default!, default, default, default, default, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenACallerContext_WhenARangeIsProcessedOrItsReadThrows_ThenExtractionUsesTheBackgroundContextAndTheCallersIsRestored(
        bool readThrows)
    {
        var caller = new FhirRequestContext { TenantId = 99 };
        var accessor = new RecordingContextAccessor { RequestContext = caller };
        var observed = new List<IFhirRequestContext?>();
        var indexer = Substitute.For<ISearchIndexer>();
        indexer.Extract(Arg.Any<IElement>()).Returns(_ =>
        {
            observed.Add(accessor.RequestContext);
            return [];
        });
        var store = SinglePageStore();
        if (readThrows)
        {
            store.ReadRangeAsync(default!, default, default, default, default, default)
                .ReturnsForAnyArgs<IReadOnlyList<ReindexResource>>(_ => throw new InvalidOperationException("read failed"));
        }

        var processor = CreateProcessor(accessor, indexer, store);
        var process = () => processor.ProcessAsync(
            new ReindexRangeInput("job", 1, "Patient", 1, 20, 42, 20, 0),
            CancellationToken.None);

        if (readThrows)
        {
            await Should.ThrowAsync<InvalidOperationException>(process);
            observed.ShouldBeEmpty();
        }
        else
        {
            await process();
            var background = observed.ShouldHaveSingleItem().ShouldNotBeNull();
            background.ShouldNotBeSameAs(caller);
            background.IsBackgroundTask.ShouldBeTrue();
            background.TenantId.ShouldBe(1);
            background.FhirVersion.ShouldBe(FhirVersion.R4);
            background.ResourceType.ShouldBe("Patient");
        }

        accessor.RequestContext.ShouldBeSameAs(caller);
    }

    // F11: an extraction failure is recorded and skipped; the rest of the page is still written.
    [Fact]
    public async Task GivenOneResourceFailsExtraction_WhenRangeIsProcessed_ThenItIsRecordedAndTheOthersAreWritten()
    {
        var store = PageStore(3);
        var indexer = Substitute.For<ISearchIndexer>();
        indexer.Extract(Arg.Any<IElement>()).Returns(call =>
            call.Arg<IElement>().Scalar("id") as string == "p2"
                ? throw new InvalidOperationException("bad expression")
                : []);

        var result = await CreateProcessor(new FhirRequestContextAccessor(), indexer, store).ProcessAsync(
            new ReindexRangeInput("job", 1, "Patient", 1, 20, 42, 20, 0),
            CancellationToken.None);

        result.ResourcesReindexed.ShouldBe(2);
        result.FailedResourceCount.ShouldBe(1);
        result.FailedResources.ShouldBe([new ReindexFailedResource("Patient", "p2", "bad expression")]);
        await store.Received(1).UpdateSearchIndicesAsync(
            Arg.Is<IReadOnlyList<ReindexResource>>(batch =>
                string.Join(",", batch.Select(resource => resource.Resource.ResourceId)) == "p1,p3"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenMoreFailuresThanTheSample_WhenRangeIsProcessed_ThenAllAreCountedAndTheSampleIsCapped()
    {
        var store = PageStore(ReindexTenantProgress.FailedResourceSampleSize + 1);
        var indexer = Substitute.For<ISearchIndexer>();
        indexer.Extract(Arg.Any<IElement>()).Returns(_ => throw new InvalidOperationException("bad expression"));

        var result = await CreateProcessor(new FhirRequestContextAccessor(), indexer, store).ProcessAsync(
            new ReindexRangeInput("job", 1, "Patient", 1, 20, 42, 1000, 0),
            CancellationToken.None);

        result.FailedResourceCount.ShouldBe(ReindexTenantProgress.FailedResourceSampleSize + 1);
        result.FailedResources.Count.ShouldBe(ReindexTenantProgress.FailedResourceSampleSize);
        await store.DidNotReceiveWithAnyArgs().UpdateSearchIndicesAsync(default!, default);
    }

    // Spec 8.4 step 5: with a query delay the worker waits after each page before reading the next one.
    [Fact]
    public async Task GivenAQueryDelay_WhenAPageIsWritten_ThenTheWorkerWaitsBeforeTheNextRead()
    {
        using var cancellation = new CancellationTokenSource();
        var store = PageStore(1);
        store.UpdateSearchIndicesAsync(Arg.Any<IReadOnlyList<ReindexResource>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                // Cancelling here makes the wait observable: it can only fail if it was started.
                cancellation.Cancel();
                return new SearchIndexUpdateResult(1, 0);
            });
        var indexer = Substitute.For<ISearchIndexer>();
        indexer.Extract(Arg.Any<IElement>()).Returns([]);

        await Should.ThrowAsync<OperationCanceledException>(() =>
            CreateProcessor(new FhirRequestContextAccessor(), indexer, store).ProcessAsync(
                new ReindexRangeInput("job", 1, "Patient", 1, 20, 42, 20, int.MaxValue),
                cancellation.Token));

        await store.Received(1).ReadRangeAsync(
            Arg.Any<string>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
    }

    private static ReindexRangeProcessor CreateProcessor(
        IFhirRequestContextAccessor accessor,
        ISearchIndexer indexer,
        IReindexStore store)
    {
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetDefinitionsHandle(FhirVersion.R4, 1)
            .Returns(new DefinitionsHandle(indexer, FhirVersion.R4.GetSchemaProvider(), 42));
        return CreateProcessor(accessor, store, versions);
    }

    private static ReindexRangeProcessor CreateProcessor(
        IFhirRequestContextAccessor accessor,
        IReindexStore store,
        IFhirVersionContext versions,
        ISourceEventStore? eventStore = null)
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>()).Returns(ReindexTestHelper.Tenant(1));
        var stores = Substitute.For<IReindexStoreFactory>();
        stores.GetReindexStoreAsync(1, Arg.Any<CancellationToken>()).Returns(store);
        return new ReindexRangeProcessor(
            stores,
            tenants,
            versions,
            TestConformanceRefresher.Create(new ConformanceState(), eventStore),
            accessor);
    }

    private static IReindexStore SinglePageStore() => PageStore(1);

    /// <summary>A store whose range 1..20 holds one page of <paramref name="count"/> patients, all written.</summary>
    private static IReindexStore PageStore(int count)
    {
        var store = Substitute.For<IReindexStore>();
        var resources = Enumerable.Range(1, count)
            .Select(index => new ReindexResource(
                new ResourceWrapper(
                    "Patient",
                    $"p{index}",
                    "1",
                    DateTimeOffset.UtcNow,
                    ResourceJsonNode.Parse($$"""{"resourceType":"Patient","id":"p{{index}}"}"""),
                    new ResourceRequest("PUT", $"Patient/p{index}")),
                index))
            .ToArray();
        store.ReadRangeAsync("Patient", 1, 20, Arg.Any<int>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<long?>(4).HasValue ? [] : resources);
        store.UpdateSearchIndicesAsync(Arg.Any<IReadOnlyList<ReindexResource>>(), Arg.Any<CancellationToken>())
            .Returns(call => new SearchIndexUpdateResult(call.Arg<IReadOnlyList<ReindexResource>>().Count, 0));
        return store;
    }

    // A plain property, unlike the AsyncLocal accessor: what the processor sets and restores is observable
    // from the test after the call returns.
    private sealed class RecordingContextAccessor : IFhirRequestContextAccessor
    {
        public IFhirRequestContext? RequestContext { get; set; }
    }
}
