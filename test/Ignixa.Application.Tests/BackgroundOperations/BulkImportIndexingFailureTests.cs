using System.Text;
using System.Text.Json;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Import.Activities;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class BulkImportIndexingFailureTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenIndexExtractionFailure_WhenImportRuns_ThenResourceIsReportedAsAnError(bool streaming)
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>()).Returns(
            new TenantConfiguration { TenantId = 1, DisplayName = "Bulk import test tenant", FhirVersion = "4.0" });
        var indexer = Substitute.For<ISearchIndexer>();
        indexer.Extract(Arg.Any<IElement>()).Returns(_ => throw new InvalidOperationException("Index extraction failed"));
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetSchemaProvider(FhirVersion.R4, 1).Returns(new R4CoreSchemaProvider());
        versions.GetSearchIndexer(FhirVersion.R4, 1).Returns(indexer);
        var repository = Substitute.For<IFhirRepository>();
        repository.GetNextTransactionIdAsync(Arg.Any<CancellationToken>()).Returns(new TransactionId(1));
        repository.BatchWriteAsync(Arg.Any<TransactionId>(),
            Arg.Any<IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)>>(),
            Arg.Any<CancellationToken>()).Returns(new[] { new ResourceKey("Patient", "p1") });
        var factory = Substitute.For<IFhirRepositoryFactory>();
        factory.GetRepositoryAsync(1, Arg.Any<CancellationToken>()).Returns(repository);
        var blobs = Substitute.For<IBlobStorageClient>();
        blobs.ReadBlobAsync("input.ndjson", Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes("{\"resourceType\":\"Patient\",\"id\":\"p1\"}\n")));
        TaskActivity activity = streaming
            ? new StreamingImportFileActivity(factory, versions, tenants, blobs,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Import:ConsumerCount"] = "1" }).Build(),
                new FhirRequestContextAccessor(), NullLogger<StreamingImportFileActivity>.Instance)
            : new ImportBatchActivity(factory, versions, tenants, new FhirRequestContextAccessor(), NullLogger<ImportBatchActivity>.Instance);
        object input = streaming
            ? new StreamingImportFileInput
            {
                JobId = "job", TenantId = 1, FileUrl = "input.ndjson", ResourceType = "Patient",
                Mode = "IncrementalLoad", BatchSize = 1, ChannelCapacity = 1
            }
            : new ImportBatchInput
            {
                JobId = "job", TenantId = 1, ResourceType = "Patient", Mode = "IncrementalLoad",
                Resources = ["{\"resourceType\":\"Patient\",\"id\":\"p1\"}"]
            };

        var json = await activity.RunAsync(new TaskContext(new OrchestrationInstance { InstanceId = "job" }),
            JsonSerializer.Serialize(new[] { input }));

        var result = JsonSerializer.Deserialize<ImportBatchOutput>(json)!;
        result.SuccessCount.ShouldBe(0);
        result.ErrorCount.ShouldBe(1);
        result.Errors.Single().ErrorMessage.ShouldContain("Index extraction failed");
    }
}
