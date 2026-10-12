using System.Text;
using System.Text.Json;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Import.Activities;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Tests.Features;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class ImportDefinitionsEventIdTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenAProductionImportRoute_WhenAllocatingItsBatch_ThenItUsesTheCurrentDefinitionsEventId(bool streaming)
    {
        const long DefinitionsEventId = 73;
        var repository = WritingRepository();

        var output = await RunAsync(streaming, TestWritePath.Versions(DefinitionsEventId), repository);

        output.SuccessCount.ShouldBe(1);
        await repository.Received(1).GetNextTransactionIdAsync(DefinitionsEventId, Arg.Any<CancellationToken>());
    }

    // Spec 5.4: a batch the barrier rejects is re-extracted with a fresh handle and retried once.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenTheBarrierRejectsTheFirstAllocation_WhenImporting_ThenTheBatchIsReextractedWithFreshDefinitions(bool streaming)
    {
        var indexer = Substitute.For<ISearchIndexer>();
        indexer.Extract(Arg.Any<IElement>()).Returns(Array.Empty<SearchIndexEntry>());
        var versions = TestWritePath.Versions(72, indexer);
        versions.GetDefinitionsHandle(FhirVersion.R4, 1).Returns(
            new DefinitionsHandle(indexer, new R4CoreSchemaProvider(), 72),
            new DefinitionsHandle(indexer, new R4CoreSchemaProvider(), 73));
        var repository = WritingRepository();
        repository.GetNextTransactionIdAsync(72, Arg.Any<CancellationToken>())
            .Returns<ValueTask<TransactionId>>(_ => throw new StaleConformanceDefinitionsException(101, 72, 73));

        var output = await RunAsync(streaming, versions, repository);

        output.SuccessCount.ShouldBe(1);
        await repository.Received(1).GetNextTransactionIdAsync(73, Arg.Any<CancellationToken>());
        indexer.Received(2).Extract(Arg.Any<IElement>());
    }

    private static IFhirRepository WritingRepository()
    {
        var repository = Substitute.For<IFhirRepository>();
        repository.GetNextTransactionIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new TransactionId(1));
        repository.BatchWriteAsync(
                Arg.Any<TransactionId>(),
                Arg.Any<IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)>>(),
                Arg.Any<CancellationToken>())
            .Returns([new ResourceKey("Patient", "p1")]);
        return repository;
    }

    private static async Task<ImportBatchOutput> RunAsync(bool streaming, IFhirVersionContext versions, IFhirRepository repository)
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>()).Returns(
            new TenantConfiguration { TenantId = 1, DisplayName = "Import stamp test", FhirVersion = "4.0" });
        var factory = TestWritePath.Repositories(repository);
        var blobs = Substitute.For<IBlobStorageClient>();
        blobs.ReadBlobAsync("input.ndjson", Arg.Any<CancellationToken>()).Returns(
            new MemoryStream(Encoding.UTF8.GetBytes("""{"resourceType":"Patient","id":"p1"}""" + "\n")));
        TaskActivity activity = streaming
            ? new StreamingImportFileActivity(
                factory,
                versions,
                tenants,
                blobs,
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Import:ConsumerCount"] = "1" })
                    .Build(),
                new FhirRequestContextAccessor(),
                TestConformanceBarrierRetryPolicy.Create(),
                NullLogger<StreamingImportFileActivity>.Instance)
            : new ImportBatchActivity(
                factory,
                versions,
                tenants,
                new FhirRequestContextAccessor(),
                TestConformanceBarrierRetryPolicy.Create(),
                NullLogger<ImportBatchActivity>.Instance);
        object input = streaming
            ? new StreamingImportFileInput
            {
                JobId = "job",
                TenantId = 1,
                FileUrl = "input.ndjson",
                ResourceType = "Patient",
                Mode = "IncrementalLoad",
                BatchSize = 1,
                ChannelCapacity = 1
            }
            : new ImportBatchInput
            {
                JobId = "job",
                TenantId = 1,
                ResourceType = "Patient",
                Mode = "IncrementalLoad",
                Resources = ["""{"resourceType":"Patient","id":"p1"}"""]
            };

        var json = await activity.RunAsync(
            new TaskContext(new OrchestrationInstance { InstanceId = "job" }),
            JsonSerializer.Serialize(new[] { input }));
        return JsonSerializer.Deserialize<ImportBatchOutput>(json)!;
    }
}
