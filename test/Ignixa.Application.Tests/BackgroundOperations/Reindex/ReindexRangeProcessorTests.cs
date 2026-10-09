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
        var store = Substitute.For<IFhirRepository, IReindexStore>();
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
        ((IReindexStore)store).ReadRangeAsync(
                "Patient",
                1,
                20,
                Arg.Any<int>(),
                Arg.Any<long?>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<long?>(4).HasValue ? [] : resources);
        var writeSizes = new List<int>();
        var writeAttempt = 0;
        ((IReindexStore)store).UpdateSearchIndicesAsync(
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

                return (count, 0);
            });
        var repositories = Substitute.For<IFhirRepositoryFactory>();
        repositories.GetRepositoryAsync(1, Arg.Any<CancellationToken>())
            .Returns((IFhirRepository)store);
        var indexer = Substitute.For<ISearchIndexer>();
        indexer.Extract(Arg.Any<IElement>()).Returns([]);
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetDefinitionsHandle(FhirVersion.R4, 1)
            .Returns(new DefinitionsHandle(indexer, FhirVersion.R4.GetSchemaProvider(), 42));
        var processor = new ReindexRangeProcessor(
            repositories,
            tenants,
            versions,
            Substitute.For<IConformanceDefinitionsSynchronizer>(),
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
        var repository = Substitute.For<IFhirRepository, IReindexStore>();
        var repositories = Substitute.For<IFhirRepositoryFactory>();
        repositories.GetRepositoryAsync(1, Arg.Any<CancellationToken>())
            .Returns((IFhirRepository)repository);
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
        var synchronizer = Substitute.For<IConformanceDefinitionsSynchronizer>();
        var processor = new ReindexRangeProcessor(
            repositories,
            tenants,
            versions,
            synchronizer,
            new FhirRequestContextAccessor());

        var result = await processor.ProcessAsync(
            new ReindexRangeInput("job", 1, "Patient", 1, 100, 42, 1000, 0),
            CancellationToken.None);

        result.ResourcesRead.ShouldBe(0);
        await synchronizer.Received(1).SynchronizeAsync(Arg.Any<CancellationToken>());
        versions.Received(2).GetDefinitionsHandle(FhirVersion.R4, 1);
    }
}
