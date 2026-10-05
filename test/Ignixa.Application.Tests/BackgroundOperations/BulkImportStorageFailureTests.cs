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
using Ignixa.Search.Definition;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class BulkImportStorageFailureTests
{
    [Theory]
    [InlineData(true, "allocate")]
    [InlineData(true, "write")]
    [InlineData(true, "commit")]
    [InlineData(false, "allocate")]
    [InlineData(false, "write")]
    [InlineData(false, "commit")]
    public async Task GivenStorageFailure_WhenImporting_ThenItStopsWithoutReportingStoredResourcesAsRejected(
        bool streaming, string phase)
    {
        using var versions = new FhirVersionContext(NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions(), NullFhirBaseUriProvider.Instance);
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>()).Returns(
            new TenantConfiguration { TenantId = 1, DisplayName = "Bulk failure test", FhirVersion = "4.0" });
        var repository = Substitute.For<IFhirRepository>();
        var stored = new List<string>();
        repository.GetNextTransactionIdAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            phase == "allocate" ? throw new IOException("allocate response unavailable") : new TransactionId(1));
        repository.BatchWriteAsync(Arg.Any<TransactionId>(),
            Arg.Any<IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)>>(),
            Arg.Any<CancellationToken>()).Returns(call =>
        {
            if (phase == "write")
            {
                throw new IOException("write response unavailable");
            }
            var operations = call.Arg<IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)>>();
            stored.AddRange(operations.Select(operation => operation.resourceId));
            return operations.Select(operation => new ResourceKey(operation.resourceType, operation.resourceId)).ToArray();
        });
        repository.CommitTransactionAsync(Arg.Any<TransactionId>(), Arg.Any<CancellationToken>())
            .Returns(_ => phase == "commit" ? throw new IOException("commit response unavailable") : ValueTask.CompletedTask);
        var factory = Substitute.For<IFhirRepositoryFactory>();
        factory.GetRepositoryAsync(1, Arg.Any<CancellationToken>()).Returns(repository);
        const string patient = """{"resourceType":"Patient","id":"p1"}""";
        var blobs = Substitute.For<IBlobStorageClient>();
        blobs.ReadBlobAsync("input.ndjson", Arg.Any<CancellationToken>()).Returns(
            new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', Enumerable.Repeat(patient, 20)))));
        TaskActivity activity = streaming
            ? new StreamingImportFileActivity(factory, versions, tenants, blobs,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Import:ConsumerCount"] = "1" }).Build(),
                new FhirRequestContextAccessor(), NullLogger<StreamingImportFileActivity>.Instance)
            : new ImportBatchActivity(factory, versions, tenants, new FhirRequestContextAccessor(), NullLogger<ImportBatchActivity>.Instance);
        object input = streaming
            ? new StreamingImportFileInput { JobId = "job", TenantId = 1, ResourceType = "Patient", FileUrl = "input.ndjson",
                Mode = "IncrementalLoad", BatchSize = 1, ChannelCapacity = 1 }
            : new ImportBatchInput { JobId = "job", TenantId = 1, ResourceType = "Patient", Mode = "IncrementalLoad", Resources = [patient] };

        var failure = await Should.ThrowAsync<Exception>(() => activity.RunAsync(
            new TaskContext(new OrchestrationInstance { InstanceId = "job" }),
            JsonSerializer.Serialize(new[] { input })).WaitAsync(TimeSpan.FromSeconds(10)));

        failure.ToString().ShouldContain($"{phase}");
        if (phase == "commit")
        {
            failure.ToString().ShouldContain("indeterminate");
            stored.ShouldBe(["p1"]);
        }
        else
        {
            stored.ShouldBeEmpty();
        }
    }
}
