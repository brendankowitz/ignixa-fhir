using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.BackgroundOperations.Reindex.Workers;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
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
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TenantConfiguration
            {
                TenantId = 1,
                DisplayName = "Tenant",
                FhirVersion = "4.0"
            });
        var store = Substitute.For<IReindexStore>();
        var resources = Enumerable.Range(1, 20)
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
        store.ReadRangeAsync(
                "Patient",
                1,
                20,
                Arg.Any<int>(),
                Arg.Any<long?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<long?>(4).HasValue ? [] : resources);
        var writeSizes = new List<int>();
        var writeAttempt = 0;
        store.UpdateSearchIndicesAsync(
                Arg.Any<IReadOnlyList<ReindexResource>>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var count = call.Arg<IReadOnlyList<ReindexResource>>().Count;
                writeSizes.Add(count);
                if (Interlocked.Increment(ref writeAttempt) == 1)
                {
                    throw new TimeoutException("SQL command timed out.");
                }

                return new SearchIndexUpdateResult(count, 0);
            });
        var stores = Substitute.For<IReindexStoreFactory>();
        stores.GetReindexStoreAsync(1, Arg.Any<CancellationToken>())
            .Returns(store);
        var indexer = Substitute.For<ISearchIndexer>();
        indexer.Extract(Arg.Any<IElement>()).Returns([]);
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetDefinitionsHandle(FhirVersion.R4, 1)
            .Returns(new DefinitionsHandle(indexer, FhirVersion.R4.GetSchemaProvider(), 42));
        var processor = new ReindexRangeProcessor(
            stores,
            tenants,
            versions,
            TestConformanceRefresher.Create(new ConformanceState()),
            new FhirRequestContextAccessor());

        var result = await processor.ProcessAsync(
            new ReindexRangeInput("job", 1, "Patient", 1, 20, 42, 20, 0),
            CancellationToken.None);

        writeSizes.ShouldBe([20, 10, 10]);
        result.ResourcesReindexed.ShouldBe(20);
    }

    [Fact]
    public async Task GivenDefinitionsBehindTargetEvent_WhenRangeIsProcessed_ThenDefinitionsAreRefreshed()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TenantConfiguration
            {
                TenantId = 1,
                DisplayName = "Tenant",
                FhirVersion = "4.0"
            });
        var repository = Substitute.For<IReindexStore>();
        var stores = Substitute.For<IReindexStoreFactory>();
        stores.GetReindexStoreAsync(1, Arg.Any<CancellationToken>())
            .Returns(repository);
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetDefinitionsHandle(Arg.Any<FhirVersion>(), 1)
            .Returns(
                new DefinitionsHandle(
                    Substitute.For<ISearchIndexer>(),
                    Substitute.For<IFhirSchemaProvider>(),
                    41),
                new DefinitionsHandle(
                    Substitute.For<ISearchIndexer>(),
                    Substitute.For<IFhirSchemaProvider>(),
                    42));
        var eventStore = TestConformanceRefresher.EmptyEventStore();
        var processor = new ReindexRangeProcessor(
            stores,
            tenants,
            versions,
            TestConformanceRefresher.Create(new ConformanceState(), eventStore),
            new FhirRequestContextAccessor());

        var result = await processor.ProcessAsync(
            new ReindexRangeInput("job", 1, "Patient", 1, 100, 42, 1000, 0),
            CancellationToken.None);

        result.ResourcesRead.ShouldBe(0);
        _ = eventStore.Received(1).ReadFromAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        versions.Received(2).GetDefinitionsHandle(FhirVersion.R4, 1);
    }

    [Fact]
    public async Task GivenRangeIsProcessed_WhenResourcesAreExtracted_ThenTheBackgroundRequestContextIsSetAndRestoredAfterwards()
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
        var processor = CreateProcessor(accessor, indexer, SinglePageStore());

        await processor.ProcessAsync(
            new ReindexRangeInput("job", 1, "Patient", 1, 20, 42, 20, 0),
            CancellationToken.None);

        var background = observed.ShouldHaveSingleItem().ShouldNotBeNull();
        background.ShouldNotBeSameAs(caller);
        background.IsBackgroundTask.ShouldBeTrue();
        background.TenantId.ShouldBe(1);
        background.FhirVersion.ShouldBe(FhirVersion.R4);
        background.ResourceType.ShouldBe("Patient");
        accessor.RequestContext.ShouldBeSameAs(caller);
    }

    [Fact]
    public async Task GivenReadingTheRangeThrows_WhenRangeIsProcessed_ThenTheCallersRequestContextIsRestored()
    {
        var caller = new FhirRequestContext { TenantId = 99 };
        var accessor = new RecordingContextAccessor { RequestContext = caller };
        var store = Substitute.For<IReindexStore>();
        store.ReadRangeAsync(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<long?>(),
                Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ReindexResource>>(_ => throw new InvalidOperationException("read failed"));
        var processor = CreateProcessor(accessor, Substitute.For<ISearchIndexer>(), store);

        await Should.ThrowAsync<InvalidOperationException>(() => processor.ProcessAsync(
            new ReindexRangeInput("job", 1, "Patient", 1, 20, 42, 20, 0),
            CancellationToken.None));

        accessor.RequestContext.ShouldBeSameAs(caller);
    }

    private static ReindexRangeProcessor CreateProcessor(
        IFhirRequestContextAccessor accessor,
        ISearchIndexer indexer,
        IReindexStore store)
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TenantConfiguration
            {
                TenantId = 1,
                DisplayName = "Tenant",
                FhirVersion = "4.0"
            });
        var stores = Substitute.For<IReindexStoreFactory>();
        stores.GetReindexStoreAsync(1, Arg.Any<CancellationToken>()).Returns(store);
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetDefinitionsHandle(FhirVersion.R4, 1)
            .Returns(new DefinitionsHandle(indexer, FhirVersion.R4.GetSchemaProvider(), 42));
        return new ReindexRangeProcessor(
            stores,
            tenants,
            versions,
            TestConformanceRefresher.Create(new ConformanceState()),
            accessor);
    }

    private static IReindexStore SinglePageStore()
    {
        var store = Substitute.For<IReindexStore>();
        var resource = new ReindexResource(
            new ResourceWrapper(
                "Patient",
                "p1",
                "1",
                DateTimeOffset.UtcNow,
                ResourceJsonNode.Parse("""{"resourceType":"Patient","id":"p1"}"""),
                new ResourceRequest("PUT", "Patient/p1")),
            1);
        store.ReadRangeAsync(
                "Patient",
                1,
                20,
                Arg.Any<int>(),
                Arg.Any<long?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<long?>(4).HasValue ? [] : [resource]);
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
