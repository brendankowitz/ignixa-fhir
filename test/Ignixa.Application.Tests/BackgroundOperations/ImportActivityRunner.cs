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
using Ignixa.Domain.Models;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Ignixa.Application.Tests.BackgroundOperations;

/// <summary>Imports Patient <c>p1</c> into R4 tenant 1 through the streaming file route or the batch route.</summary>
internal static class ImportActivityRunner
{
    private const string Patient = """{"resourceType":"Patient","id":"p1"}""";

    /// <summary>A repository that allocates transaction 1 and stores every write as <c>Patient/p1</c>.</summary>
    public static IFhirRepository WritingRepository()
    {
        var repository = Substitute.For<IFhirRepository>();
        repository.GetNextTransactionIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(new TransactionId(1));
        repository.BatchWriteAsync(
                Arg.Any<TransactionId>(),
                Arg.Any<IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)>>(),
                Arg.Any<CancellationToken>())
            .Returns([new ResourceKey("Patient", "p1")]);
        return repository;
    }

    public static async Task<ImportBatchOutput> RunAsync(bool streaming, IFhirVersionContext versions, IFhirRepository repository) =>
        JsonSerializer.Deserialize<ImportBatchOutput>(await RunActivityAsync(streaming, versions, repository))!;

    /// <summary>Runs the activity; the streaming route reads <paramref name="fileLines"/> copies of the patient.</summary>
    public static Task<string> RunActivityAsync(bool streaming, IFhirVersionContext versions, IFhirRepository repository, int fileLines = 1)
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TenantConfiguration { TenantId = 1, DisplayName = "Import", FhirVersion = "4.0" });
        var repositories = TestWritePath.Repositories(repository);
        var context = new TaskContext(new OrchestrationInstance { InstanceId = "job" });
        if (!streaming)
        {
            return new ImportBatchActivity(repositories, versions, tenants, new FhirRequestContextAccessor(),
                    TestConformanceBarrierRetryPolicy.Create(), NullLogger<ImportBatchActivity>.Instance)
                .RunAsync(context, Serialize(new ImportBatchInput
                {
                    JobId = "job", TenantId = 1, ResourceType = "Patient", Mode = "IncrementalLoad", Resources = [Patient]
                }));
        }

        var blobs = Substitute.For<IBlobStorageClient>();
        blobs.ReadBlobAsync("input.ndjson", Arg.Any<CancellationToken>()).Returns(
            new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', Enumerable.Repeat(Patient, fileLines)) + "\n")));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Import:ConsumerCount"] = "1" })
            .Build();
        return new StreamingImportFileActivity(repositories, versions, tenants, blobs, configuration, new FhirRequestContextAccessor(),
                TestConformanceBarrierRetryPolicy.Create(), NullLogger<StreamingImportFileActivity>.Instance)
            .RunAsync(context, Serialize(new StreamingImportFileInput
            {
                JobId = "job", TenantId = 1, FileUrl = "input.ndjson", ResourceType = "Patient",
                Mode = "IncrementalLoad", BatchSize = 1, ChannelCapacity = 1
            }));
    }

    private static string Serialize(object input) => JsonSerializer.Serialize(new[] { input });
}
