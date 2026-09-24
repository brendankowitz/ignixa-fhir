using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Import.Activities;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.DataLayer.BlobStorage.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class ImportJobCompletionPersistenceTests
{
    [Theory]
    [InlineData(null, "Completed")]
    [InlineData("File download failed", "Failed")]
    public async Task GivenImportCompletion_WhenActivityRuns_ThenMetadataAndErrorLogArePersisted(string? failure, string expectedStatus)
    {
        var directory = Path.GetFullPath(Path.Combine("bulk-operation-test-blobs", Guid.NewGuid().ToString("N")));
        var jobId = Guid.NewGuid().ToString("N");
        try
        {
            var tenants = Substitute.For<ITenantConfigurationStore>();
            tenants.Mode.Returns(TenantMode.Isolated);
            var repository = new InMemoryBackgroundJobRepository<ImportJobDefinition>(
                tenants, NullLogger<InMemoryBackgroundJobRepository<ImportJobDefinition>>.Instance);
            var blobs = new LocalFileBlobClient(Options.Create(new LocalFileBlobStorageOptions { RootDirectory = directory }),
                NullLogger<LocalFileBlobClient>.Instance);
            await repository.CreateAsync(new BackgroundJob<ImportJobDefinition>
            {
                JobId = jobId, JobType = 2, Status = "Running",
                Definition = new ImportJobDefinition
                {
                    TenantId = 1, InputFormat = "application/fhir+ndjson", InputSource = "Patient",
                    Mode = "IncrementalLoad", InputFiles = [new InputFileInfo { Type = "Patient", Url = "input.ndjson" }]
                }
            }, CancellationToken.None);
            using var services = new ServiceCollection()
                .AddSingleton<IBackgroundJobRepository<ImportJobDefinition>>(repository)
                .AddSingleton<IBlobStorageClient>(blobs)
                .AddSingleton<ILogger<CompleteJobActivity>>(NullLogger<CompleteJobActivity>.Instance)
                .BuildServiceProvider();
            var activity = ActivatorUtilities.CreateInstance<CompleteJobActivity>(services);
            var input = $$"""
                [{"JobId":"{{jobId}}","TenantId":1,"TotalResources":2,"TotalErrors":1,
                  "ErrorMessage":{{JsonSerializer.Serialize(failure)}},
                  "ErrorLogEntries":[{"ResourceType":"Patient","ResourceId":"bad","ErrorCode":"InvalidResourceType",
                                     "ErrorMessage":"Expected Patient, got Observation","ResourceJson":"{}"}]}]
                """;

            await activity.RunAsync(new TaskContext(new OrchestrationInstance { InstanceId = jobId }), input);

            var job = (await repository.GetAsync(jobId, 1, CancellationToken.None))!;
            job.Status.ShouldBe(expectedStatus);
            job.EndDate.ShouldNotBeNull();
            job.ErrorMessage.ShouldBe(failure);
            var result = job.Result!.Deserialize<ImportJobResult>(JsonSerializerOptions.Web)!;
            result.TotalResources.ShouldBe(2);
            result.TotalErrors.ShouldBe(1);
            result.ErrorFileUrl.ShouldNotBeNull();
            await using var stream = await blobs.ReadBlobAsync(result.ErrorFileUrl!);
            var error = await JsonNode.ParseAsync(stream);
            error!["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
            error["issue"]![0]!["code"]!.GetValue<string>().ShouldBe("invalid");
            error["issue"]![0]!["diagnostics"]!.GetValue<string>().ShouldContain("Expected Patient");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
            // The exact-base implementation writes here instead of using the configured blob provider.
            var legacyPath = Path.Combine("import-errors", $"import-errors-{jobId}.ndjson");
            if (File.Exists(legacyPath))
            {
                File.Delete(legacyPath);
            }
        }
    }
}
