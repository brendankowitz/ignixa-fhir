using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Api.Endpoints;
using Ignixa.Application.BackgroundOperations.Export.Activities;
using Ignixa.Application.BackgroundOperations.Export;
using Ignixa.Application.BackgroundOperations.Export.Models;
using Ignixa.Application.BackgroundOperations.Export.Orchestrations;
using Ignixa.Application.BackgroundOperations.Import;
using Ignixa.Application.BackgroundOperations.Jobs;
using Ignixa.Application.Features.Search;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.DataLayer.BlobStorage;
using Ignixa.DataLayer.BlobStorage.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Specification.Extensions;
using Medino;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests;

public sealed class BulkOperationJobStatusEndpointTests : IAsyncLifetime
{
    private readonly string _blobDirectory = Path.GetFullPath(Path.Combine("bulk-operation-test-blobs", Guid.NewGuid().ToString("N")));
    private readonly InMemoryBackgroundJobRepository<ExportJobDefinition> _exports;
    private readonly InMemoryBackgroundJobRepository<ImportJobDefinition> _imports;
    private readonly IOrchestrationServiceClient _orchestrations = Substitute.For<IOrchestrationServiceClient>();
    private readonly WebApplication _app;
    private readonly LocalFileBlobClient _blobs;
    private readonly ITenantConfigurationStore _tenants;

    public BulkOperationJobStatusEndpointTests()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        _tenants = tenants;
        tenants.Mode.Returns(TenantMode.Isolated);
        _exports = new(tenants, NullLogger<InMemoryBackgroundJobRepository<ExportJobDefinition>>.Instance);
        _imports = new(tenants, NullLogger<InMemoryBackgroundJobRepository<ImportJobDefinition>>.Instance);
        _blobs = new(Options.Create(new LocalFileBlobStorageOptions { RootDirectory = _blobDirectory }), NullLogger<LocalFileBlobClient>.Instance);
        var client = new TaskHubClient(_orchestrations);
        var statusHandler = new GetJobStatusHandler(client, _imports, _exports);
        var importHandler = new CreateImportJobHandler(client, _imports);
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TenantConfiguration { TenantId = 1, DisplayName = "Export test", FhirVersion = "4.0" });
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetSchemaProvider(FhirVersion.R4, 1).Returns(FhirVersion.R4.GetSchemaProvider());
        var exportHandler = new CreateExportJobHandler(client, _exports, tenants, versions,
            new ExportGroupResolver(Substitute.For<IFhirRepositoryFactory>(), NullFhirBaseUriProvider.Instance));
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<GetJobStatusQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => statusHandler.HandleAsync(call.Arg<GetJobStatusQuery>(), call.Arg<CancellationToken>()));
        mediator.SendAsync(Arg.Any<CreateImportJobCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => importHandler.HandleAsync(call.Arg<CreateImportJobCommand>(), call.Arg<CancellationToken>()));
        mediator.SendAsync(Arg.Any<CreateExportJobCommand>(), Arg.Any<CancellationToken>())
            .Returns(call => exportHandler.HandleAsync(call.Arg<CreateExportJobCommand>(), call.Arg<CancellationToken>()));
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(mediator);
        builder.Services.AddSingleton<IBlobStorageClient>(_blobs);
        builder.Services.AddSingleton(client);
        builder.Services.AddSingleton<IBackgroundJobRepository<ExportJobDefinition>>(_exports);
        builder.Services.AddSingleton<IBackgroundJobRepository<ImportJobDefinition>>(_imports);
        _app = builder.Build();
        _app.MapExportEndpoints();
        _app.MapImportEndpoints();
    }

    [Fact]
    public async Task GivenPersistedWorkerCompletion_WhenPollingExport_ThenEveryPartitionIsReadable()
    {
        await AddExportAsync("Running");
        const string first = "partition/1/export/job/Patient-1-9.ndjson";
        const string second = "partition/1/export/job/Patient-10-19.ndjson";
        await _blobs.WriteBlobAsync(first, new MemoryStream(Encoding.UTF8.GetBytes("{\"resourceType\":\"Patient\",\"id\":\"p1\"}\n")));
        await _blobs.WriteBlobAsync(second, new MemoryStream(Encoding.UTF8.GetBytes("{\"resourceType\":\"Patient\",\"id\":\"p2\"}\n")));
        var activity = new CompleteJobActivity(_exports, NullLogger<CompleteJobActivity>.Instance);
        var completion = JsonSerializer.SerializeToNode(new CompleteJobInput("job", 1, true,
            new Dictionary<string, string> { ["Patient-1-9"] = first, ["Patient-10-19"] = second }, 2, null))!;
        completion["ExportedFileCounts"] = JsonNode.Parse("""{"Patient-1-9":1,"Patient-10-19":1}""");
        await activity.RunAsync(new TaskContext(new OrchestrationInstance { InstanceId = "job" }),
            new JsonArray(completion).ToJsonString());

        var response = await SendAsync("GetExportStatus");

        response.StatusCode.ShouldBe(200);
        var output = response.Body["output"]!.AsArray();
        output.Count.ShouldBe(2);
        foreach (var entry in output)
        {
            entry!["type"]!.GetValue<string>().ShouldBe("Patient");
            entry["count"]!.GetValue<long>().ShouldBe(1);
            File.Exists(new Uri(entry["url"]!.GetValue<string>()).LocalPath).ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task GivenFilteredPartitions_WhenRealWritersFinish_ThenManifestOmitsOnlyConfirmedEmptyOutputs(
        bool allEmpty, int expectedFiles)
    {
        await AddExportAsync("Running");
        var context = Substitute.For<OrchestrationContext>();
        context.ScheduleTask<GetExportRangesOutput>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(new GetExportRangesOutput("Patient", [(1, 9), (10, 19)]));
        var writers = new BlobStorageExportStreamWriterFactory(_blobs, NullLoggerFactory.Instance);
        context.ScheduleTask<ExportWorkerOutput>(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(async call =>
        {
            var input = (ExportWorkerInput)call.Arg<object[]>()[0];
            await using var writer = await writers.CreateAsync(1, input.OutputPath);
            var count = !allEmpty && input.StartSurrogateId == 1 ? 1 : 0;
            if (count != 0)
            {
                await writer.WriteResourceAsync(new SearchEntryResult("Patient", "p1", "1", DateTimeOffset.UtcNow,
                    Encoding.UTF8.GetBytes("""{"resourceType":"Patient","id":"p1"}""")));
            }
            await writer.FlushAsync();
            return new ExportWorkerOutput("Patient", input.StartSurrogateId, input.EndSurrogateId, count, writer.BytesWritten);
        });
        var complete = new CompleteJobActivity(_exports, NullLogger<CompleteJobActivity>.Instance);
        context.ScheduleTask<bool>(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(async call =>
            JsonSerializer.Deserialize<bool>(await complete.RunAsync(
                new TaskContext(new OrchestrationInstance { InstanceId = "job" }),
                JsonSerializer.Serialize(call.Arg<object[]>()))));

        var result = await new ExportOrchestration().RunTask(context, new ExportCoordinatorInput("job", 1, ["Patient"]));
        var response = await SendAsync("GetExportStatus");

        result.Success.ShouldBeTrue();
        response.StatusCode.ShouldBe(200);
        response.Body["output"]!.AsArray().Count.ShouldBe(expectedFiles);
        (await _blobs.BlobExistsAsync("partition/1/export/job/Patient-10-19.ndjson")).ShouldBeFalse();
        var stored = (await _exports.GetAsync("job", 1, CancellationToken.None))!;
        var metadata = stored.Result!.Deserialize<ExportJobResult>(JsonSerializerOptions.Web)!;
        metadata.TotalResources.ShouldBe(expectedFiles);
        metadata.ExportedFiles.Count.ShouldBe(expectedFiles);
        stored.Result!.AsObject().First(property => property.Key.Equals("exportedFileCounts", StringComparison.OrdinalIgnoreCase))
            .Value!.AsObject().Count.ShouldBe(expectedFiles);
    }

    [Fact]
    public async Task GivenMissingOutputBlob_WhenPollingExport_ThenFailureIsExplicit()
    {
        await AddExportAsync("Completed", """{"totalResources":1,"exportedFiles":{"Patient":"does-not-exist.ndjson"}}""");
        var response = await SendAsync("GetExportStatus");
        response.StatusCode.ShouldBe(500);
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
    }

    [Theory]
    [InlineData(1, TenantMode.Isolated)]
    [InlineData(2, TenantMode.Distributed)]
    public async Task GivenLegacyCompletedExport_WhenPolling_ThenExistingWorkerPartitionsRemainReadable(
        int requestingTenantId, TenantMode tenantMode)
    {
        _tenants.Mode.Returns(tenantMode);
        await AddExportAsync("Completed", """
            {"totalResources":2,"exportedFiles":{
              "Patient-1-9":"tenant/1/export/job/Patient-1-9.ndjson",
              "Patient-10-19":"tenant/1/export/job/Patient-10-19.ndjson"
            }}
            """);
        await _blobs.WriteBlobAsync("partition/1/export/job/Patient-1-9.ndjson",
            new MemoryStream(Encoding.UTF8.GetBytes("{\"resourceType\":\"Patient\",\"id\":\"p1\"}\n")));
        await _blobs.WriteBlobAsync("partition/1/export/job/Patient-10-19.ndjson",
            new MemoryStream(Encoding.UTF8.GetBytes("{\"resourceType\":\"Patient\",\"id\":\"p2\"}\n")));

        var response = await SendAsync("GetExportStatus", tenantId: requestingTenantId);

        response.StatusCode.ShouldBe(200);
        response.Body["request"]!.GetValue<string>().ShouldBe("http://localhost/tenant/1/$export");
        var files = response.Body["output"]!.AsArray();
        files.Count.ShouldBe(2);
        foreach (var file in files)
        {
            file!["type"]!.GetValue<string>().ShouldBe("Patient");
            File.Exists(new Uri(file["url"]!.GetValue<string>()).LocalPath).ShouldBeTrue();
            file["count"].ShouldBeNull();
        }
    }

    [Fact]
    public async Task GivenExportRequest_WhenPollingCompletion_ThenOriginalAbsoluteRequestIsPreserved()
    {
        var start = await SendAsync("StartExport", body: "", query: "?_type=Patient");
        start.StatusCode.ShouldBe(202);
        var jobId = start.Body["jobId"]!.GetValue<string>();
        var job = (await _exports.GetAsync(jobId, 1, CancellationToken.None))!;
        job.Status = "Completed";
        job.Result = JsonNode.Parse("""{"totalResources":0,"exportedFiles":{}}""");
        await _exports.UpdateAsync(job, 1, CancellationToken.None);
        var response = await SendAsync("GetExportStatus", jobId: jobId);
        response.StatusCode.ShouldBe(200);
        response.Body["request"]!.GetValue<string>().ShouldBe("http://localhost/tenant/1/$export?_type=Patient");
    }

    [Theory]
    [InlineData("Export")]
    [InlineData("Import")]
    public async Task GivenFastWorker_WhenJobCreationReturns_ThenCompletedMetadataIsNotOverwritten(string jobType)
    {
        _orchestrations.CreateTaskOrchestrationAsync(Arg.Any<TaskMessage>(), Arg.Any<OrchestrationStatus[]>())
            .Returns(async call =>
            {
                var id = call.Arg<TaskMessage>().OrchestrationInstance.InstanceId;
                if (jobType == "Export")
                {
                    var original = (await _exports.GetAsync(id, 1, CancellationToken.None))!;
                    var completed = JsonSerializer.Deserialize<BackgroundJob<ExportJobDefinition>>(JsonSerializer.Serialize(original))!;
                    completed.Status = "Completed";
                    completed.Result = JsonNode.Parse("""{"totalResources":0,"exportedFiles":{}}""");
                    await _exports.UpdateAsync(completed, 1, CancellationToken.None);
                }
                else
                {
                    var original = (await _imports.GetAsync(id, 1, CancellationToken.None))!;
                    var completed = JsonSerializer.Deserialize<BackgroundJob<ImportJobDefinition>>(JsonSerializer.Serialize(original))!;
                    completed.Status = "Completed";
                    completed.Result = JsonNode.Parse("""{"totalResources":0,"totalErrors":0,"errorFileUrl":null}""");
                    await _imports.UpdateAsync(completed, 1, CancellationToken.None);
                }
            });
        var body = jobType == "Export" ? "" : """
            {"resourceType":"Parameters","parameter":[
              {"name":"inputFormat","valueCode":"application/fhir+ndjson"},
              {"name":"input","part":[{"name":"type","valueCode":"Patient"},{"name":"url","valueUri":"input.ndjson"}]}
            ]}
            """;

        var response = await SendAsync($"Start{jobType}", body: body);

        response.StatusCode.ShouldBe(202);
        var jobId = response.Body["jobId"]!.GetValue<string>();
        var status = jobType == "Export"
            ? (await _exports.GetAsync(jobId, 1, CancellationToken.None))!.Status
            : (await _imports.GetAsync(jobId, 1, CancellationToken.None))!.Status;
        status.ShouldBe("Completed");
        (await SendAsync($"Get{jobType}Status", jobId: jobId)).StatusCode.ShouldBe(200);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("{\"totalResources\":2,\"exportedFiles\":{}}")]
    [InlineData("{\"totalResources\":\"invalid\",\"exportedFiles\":{}}")]
    public async Task GivenMalformedPersistedResult_WhenPollingExport_ThenFailureIsExplicit(string? result)
    {
        await AddExportAsync("Completed", result);

        var response = await SendAsync("GetExportStatus");

        response.StatusCode.ShouldBe(500);
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        response.Body["issue"]![0]!["diagnostics"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("Failed", 500)]
    [InlineData("Cancelled", 410)]
    public async Task GivenUnsuccessfulJob_WhenPollingExport_ThenItIsNotSuccessful(string status, int expectedStatus)
    {
        await AddExportAsync(status);

        var response = await SendAsync("GetExportStatus");

        response.StatusCode.ShouldBe(expectedStatus);
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
    }

    [Fact]
    public async Task GivenFailedImportOutput_WhenPolling_ThenFailureSurvivesDurableTaskCompletion()
    {
        await AddImportAsync();
        _orchestrations.GetOrchestrationStateAsync("job", false).Returns(
            new List<OrchestrationState>
            {
                new()
                {
                    OrchestrationStatus = OrchestrationStatus.Completed,
                    CompletedTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Output = """{"JobId":"job","Status":"Failed","TotalResources":0,"TotalErrors":0,"ErrorMessage":"File not found in blob storage"}"""
                }
            });

        var response = await SendAsync("GetImportStatus");

        response.StatusCode.ShouldBe(500);
        response.Body["issue"]![0]!["diagnostics"]!.GetValue<string>().ShouldContain("File not found");
        (await _imports.GetAsync("job", 1, CancellationToken.None))!.Status.ShouldBe("Failed");
    }

    [Fact]
    public async Task GivenCompletedImport_WhenPolling_ThenPersistedCountsAreReturned()
    {
        var job = await AddImportAsync();
        job.Status = "Completed";
        job.Result = JsonNode.Parse("""{"TotalResources":3,"TotalErrors":0,"ErrorFileUrl":null}""");
        await _imports.UpdateAsync(job, 1, CancellationToken.None);

        var response = await SendAsync("GetImportStatus");

        response.StatusCode.ShouldBe(200);
        response.Body["output"]![0]!["count"]!.GetValue<int>().ShouldBe(3);
    }

    [Fact]
    public async Task GivenCompletedImportWithMissingErrorBlob_WhenPolling_ThenNoNonexistentArtifactIsAdvertised()
    {
        var job = await AddImportAsync();
        job.Status = "Completed";
        job.Result = JsonNode.Parse("""{"TotalResources":2,"TotalErrors":1,"ErrorFileUrl":"missing-errors.ndjson"}""");
        await _imports.UpdateAsync(job, 1, CancellationToken.None);

        var response = await SendAsync("GetImportStatus");

        response.StatusCode.ShouldBe(500);
        response.Body["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
    }

    [Fact]
    public async Task GivenCancelledMetadataAndStaleRuntime_WhenPolling_ThenCancellationIsPreserved()
    {
        await AddExportAsync("Cancelled");
        _orchestrations.GetOrchestrationStateAsync("job", false).Returns(
            new List<OrchestrationState> { new() { OrchestrationStatus = OrchestrationStatus.Running } });

        var response = await SendAsync("GetExportStatus");

        response.StatusCode.ShouldBe(410);
        (await _exports.GetAsync("job", 1, CancellationToken.None))!.Status.ShouldBe("Cancelled");
    }

    [Fact]
    public async Task GivenFailedExportOutput_WhenPolling_ThenApplicationFailureIsPreserved()
    {
        await AddExportAsync("Running");
        _orchestrations.GetOrchestrationStateAsync("job", false).Returns(
            new List<OrchestrationState>
            {
                new()
                {
                    OrchestrationStatus = OrchestrationStatus.Completed,
                    Output = """{"Success":false,"TotalResourcesExported":0,"TotalBytesWritten":0,"WorkerResults":null,"ErrorMessage":"Partition failed","FailurePhase":"WorkerExecution"}"""
                }
            });

        var response = await SendAsync("GetExportStatus");

        response.StatusCode.ShouldBe(500);
        response.Body["issue"]![0]!["diagnostics"]!.GetValue<string>().ShouldContain("Partition failed");
    }

    [Fact]
    public async Task GivenCompletedMetadata_WhenPollingRepeatedly_ThenCompletionTimestampIsStable()
    {
        var job = await AddImportAsync();
        job.Status = "Completed";
        job.EndDate = DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        job.Result = JsonNode.Parse("""{"TotalResources":3,"TotalErrors":0,"ErrorFileUrl":null}""");
        await _imports.UpdateAsync(job, 1, CancellationToken.None);
        _orchestrations.GetOrchestrationStateAsync("job", false).Returns(
            new List<OrchestrationState> { new() { OrchestrationStatus = OrchestrationStatus.Completed } });

        await SendAsync("GetImportStatus");
        await SendAsync("GetImportStatus");

        (await _imports.GetAsync("job", 1, CancellationToken.None))!.EndDate.ShouldBe(
            DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task GivenAnotherTenant_WhenPolling_ThenJobIsNotFound()
    {
        await AddExportAsync("Running");
        var response = await SendAsync("GetExportStatus", tenantId: 2);
        response.StatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task GivenSupportedParameters_WhenStartingImport_ThenJobCanBePolled()
    {
        var response = await SendAsync("StartImport", body: """
            {"resourceType":"Parameters","parameter":[
              {"name":"inputFormat","valueCode":"application/fhir+ndjson"},
              {"name":"input","part":[{"name":"type","valueCode":"Patient"},{"name":"url","valueUri":"input/Patient.ndjson"}]}
            ]}
            """);

        response.StatusCode.ShouldBe(202);
        var jobId = response.Body["jobId"]!.GetValue<string>();
        var poll = await SendAsync("GetImportStatus", jobId: jobId);
        poll.StatusCode.ShouldBe(202);
        (await _imports.GetAsync(jobId, 1, CancellationToken.None))!.Definition.InputFiles.Single().Url.ShouldBe("input/Patient.ndjson");
    }

    [Theory]
    [InlineData("Export", "Completed", 200)]
    [InlineData("Export", "Failed", 500)]
    [InlineData("Export", "Cancelled", 410)]
    [InlineData("Import", "Completed", 200)]
    [InlineData("Import", "Failed", 500)]
    [InlineData("Import", "Cancelled", 410)]
    public async Task GivenCompletionWhilePollingRuntime_WhenSavingTheStalePoll_ThenAuthoritativeOutcomeIsReturned(
        string jobType, string terminalStatus, int expectedStatus)
    {
        await AddActiveJobAsync(jobType);
        _orchestrations.GetOrchestrationStateAsync("job", false).Returns(async _ =>
        {
            await FinalizeJobAsync(jobType, terminalStatus);
            return (IList<OrchestrationState>)new List<OrchestrationState> { new() { OrchestrationStatus = OrchestrationStatus.Running } };
        });

        var response = await SendAsync($"Get{jobType}Status");

        response.StatusCode.ShouldBe(expectedStatus);
        if (terminalStatus == "Failed")
        {
            response.Body["issue"]![0]!["diagnostics"]!.GetValue<string>().ShouldContain("authoritative failure");
        }
        if (jobType == "Import" && terminalStatus == "Completed")
        {
            response.Body["output"]![0]!["count"]!.GetValue<int>().ShouldBe(7);
        }
        await AssertTerminalJobAsync(jobType, terminalStatus);
    }

    [Theory]
    [InlineData("Export", "Completed", 409)]
    [InlineData("Export", "Failed", 409)]
    [InlineData("Export", "Cancelled", 204)]
    [InlineData("Import", "Completed", 409)]
    [InlineData("Import", "Failed", 409)]
    [InlineData("Import", "Cancelled", 204)]
    public async Task GivenTerminalMetadataDuringCancellation_WhenSavingCancellation_ThenTheWinnerIsPreserved(
        string jobType, string terminalStatus, int expectedStatus)
    {
        await AddActiveJobAsync(jobType);
        _orchestrations.ForceTerminateTaskOrchestrationAsync("job", Arg.Any<string>())
            .Returns(_ => FinalizeJobAsync(jobType, terminalStatus));

        var response = await SendAsync($"Cancel{jobType}");

        response.StatusCode.ShouldBe(expectedStatus);
        if (expectedStatus == 409)
        {
            response.Body["issue"]![0]!["code"]!.GetValue<string>().ShouldBe("conflict");
            response.Body["issue"]![0]!["diagnostics"]!.GetValue<string>().ShouldContain(terminalStatus);
        }
        await AssertTerminalJobAsync(jobType, terminalStatus);
    }

    [Theory]
    [InlineData("Export", "Completed", 409)]
    [InlineData("Export", "Failed", 409)]
    [InlineData("Export", "Cancelled", 204)]
    [InlineData("Import", "Completed", 409)]
    [InlineData("Import", "Failed", 409)]
    [InlineData("Import", "Cancelled", 204)]
    public async Task GivenAlreadyTerminalMetadata_WhenCancelling_ThenStoredResultAndRuntimeAreUntouched(
        string jobType, string status, int expectedStatus)
    {
        await AddActiveJobAsync(jobType);
        await FinalizeJobAsync(jobType, status);
        _orchestrations.ForceTerminateTaskOrchestrationAsync("job", Arg.Any<string>())
            .Returns(_ => throw new InvalidOperationException("Runtime cancellation must not be invoked for a terminal job."));

        var response = await SendAsync($"Cancel{jobType}");

        response.StatusCode.ShouldBe(expectedStatus);
        await AssertTerminalJobAsync(jobType, status);
    }

    [Theory]
    [InlineData("Export")]
    [InlineData("Import")]
    public async Task GivenTerminalJobDeletedBeforeConflictReload_WhenCancelling_ThenMissingIsReturned(string jobType)
    {
        await AddActiveJobAsync(jobType);
        var services = new ServiceCollection()
            .AddSingleton(_app.Services.GetRequiredService<TaskHubClient>())
            .AddSingleton(_app.Services.GetRequiredService<ILoggerFactory>());
        if (jobType == "Export")
        {
            services.AddSingleton(DeleteAfterConflict(_exports, jobType));
        }
        else
        {
            services.AddSingleton(DeleteAfterConflict(_imports, jobType));
        }
        using var provider = services.BuildServiceProvider();

        var response = await SendAsync($"Cancel{jobType}", requestServices: provider);

        response.StatusCode.ShouldBe(404);
    }

    private IBackgroundJobRepository<T> DeleteAfterConflict<T>(IBackgroundJobRepository<T> repository, string jobType)
        where T : class, IJobDefinition
    {
        var forwarding = Substitute.For<IBackgroundJobRepository<T>>();
        forwarding.GetAsync("job", 1, Arg.Any<CancellationToken>())
            .Returns(_ => repository.GetAsync("job", 1, CancellationToken.None));
        forwarding.UpdateAsync(Arg.Any<BackgroundJob<T>>(), 1, Arg.Any<CancellationToken>()).Returns(async call =>
        {
            await FinalizeJobAsync(jobType, "Completed");
            try
            {
                await repository.UpdateAsync(call.Arg<BackgroundJob<T>>(), 1, CancellationToken.None);
            }
            catch (BackgroundJobUpdateConflictException)
            {
                await repository.DeleteAsync("job", 1, CancellationToken.None);
                throw;
            }
        });
        return forwarding;
    }

    private async Task AddActiveJobAsync(string jobType)
    {
        if (jobType == "Export")
        {
            await AddExportAsync("Queued");
        }
        else
        {
            await AddImportAsync();
        }
    }

    private async Task FinalizeJobAsync(string jobType, string status)
    {
        if (jobType == "Export")
        {
            var job = (await _exports.GetAsync("job", 1, CancellationToken.None))!;
            SetTerminalFields(job, status);
            job.Result = JsonNode.Parse("""{"totalResources":0,"exportedFiles":{}}""");
            await _exports.UpdateAsync(job, 1, CancellationToken.None);
        }
        else
        {
            var job = (await _imports.GetAsync("job", 1, CancellationToken.None))!;
            SetTerminalFields(job, status);
            job.Result = JsonNode.Parse("""{"totalResources":7,"totalErrors":0,"errorFileUrl":null}""");
            await _imports.UpdateAsync(job, 1, CancellationToken.None);
        }
    }

    private static void SetTerminalFields<T>(BackgroundJob<T> job, string status) where T : class
    {
        job.Status = status;
        job.EndDate = DateTimeOffset.Parse("2026-09-16T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        job.ErrorMessage = status == "Failed" ? "authoritative failure" : null;
        job.Progress = JsonNode.Parse("""{"progressPercentage":100,"resourcesExported":7,"processedResources":7,"processedFiles":1}""");
    }

    private async Task AssertTerminalJobAsync(string jobType, string status)
    {
        if (jobType == "Export")
        {
            var job = (await _exports.GetAsync("job", 1, CancellationToken.None))!;
            job.Status.ShouldBe(status);
            job.Result!["totalResources"]!.GetValue<int>().ShouldBe(0);
        }
        else
        {
            var job = (await _imports.GetAsync("job", 1, CancellationToken.None))!;
            job.Status.ShouldBe(status);
            job.Result!["totalResources"]!.GetValue<int>().ShouldBe(7);
        }
    }

    private async Task AddExportAsync(string status, string? result = null)
    {
        await _exports.CreateAsync(new BackgroundJob<ExportJobDefinition>
        {
            JobId = "job", JobType = 1, Status = status,
            ErrorMessage = status == "Failed" ? "Export worker failed" : null,
            Result = result == null ? null : JsonNode.Parse(result),
            Definition = new ExportJobDefinition
            {
                TenantId = 1, ResourceTypes = ["Patient"], TypeFilters = new Dictionary<string, string>(),
                OutputFormat = "application/fhir+ndjson", OutputPath = "partition/1/export/job"
            }
        }, CancellationToken.None);
    }

    private async Task<BackgroundJob<ImportJobDefinition>> AddImportAsync()
    {
        var job = new BackgroundJob<ImportJobDefinition>
        {
            JobId = "job", JobType = 2, Status = "Running",
            Definition = new ImportJobDefinition
            {
                TenantId = 1, InputFormat = "application/fhir+ndjson", InputSource = "Patient", Mode = "IncrementalLoad",
                InputFiles = [new InputFileInfo { Type = "Patient", Url = "input/Patient.ndjson" }]
            }
        };
        await _imports.CreateAsync(job, CancellationToken.None);
        return job;
    }

    private async Task<(int StatusCode, JsonNode Body)> SendAsync(
        string endpointName, int tenantId = 1, string jobId = "job", string? body = null, string? query = null,
        IServiceProvider? requestServices = null)
    {
        var endpoint = ((IEndpointRouteBuilder)_app).DataSources.SelectMany(source => source.Endpoints)
            .Single(candidate => candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == endpointName);
        await using var scope = _app.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = requestServices ?? scope.ServiceProvider };
        context.Request.RouteValues["tenantId"] = tenantId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.Request.RouteValues["jobId"] = jobId;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = endpointName == "StartExport" ? $"/tenant/{tenantId}/$export" : $"/tenant/{tenantId}/_import/{jobId}";
        context.Request.QueryString = new QueryString(query);
        context.Request.Method = endpointName.StartsWith("Cancel", StringComparison.Ordinal)
            ? "DELETE"
            : body == null ? "GET" : "POST";
        if (body != null)
        {
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        }
        context.Response.Body = new MemoryStream();
        await endpoint.RequestDelegate!(context);
        context.Response.Body.Position = 0;
        return (context.Response.StatusCode, context.Response.Body.Length == 0
            ? new JsonObject()
            : (await JsonNode.ParseAsync(context.Response.Body))!);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        Directory.Delete(_blobDirectory, recursive: true);
    }
}
