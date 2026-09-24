using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.JobManagement;
using Ignixa.Application.BackgroundOperations.Jobs;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class BulkJobStatusMcpContractTests
{
    [Theory]
    [InlineData("Export")]
    [InlineData("Import")]
    public async Task GivenPersistedJob_WhenPollingThroughMcp_ThenExistingJobFieldsArePreserved(string jobType)
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        var exports = new InMemoryBackgroundJobRepository<ExportJobDefinition>(
            tenants, NullLogger<InMemoryBackgroundJobRepository<ExportJobDefinition>>.Instance);
        var imports = new InMemoryBackgroundJobRepository<ImportJobDefinition>(
            tenants, NullLogger<InMemoryBackgroundJobRepository<ImportJobDefinition>>.Instance);
        await exports.CreateAsync(new BackgroundJob<ExportJobDefinition>
        {
            JobId = "job", JobType = 1, Status = "Completed",
            Definition = new ExportJobDefinition
            {
                TenantId = 1, ResourceTypes = ["Patient"], TypeFilters = new Dictionary<string, string>(),
                OutputFormat = "application/fhir+ndjson", OutputPath = "partition/1/export/job"
            },
            Result = JsonNode.Parse("""{"TotalResources":1,"ExportedFiles":{"Patient":"partition/1/export/job/Patient.ndjson"}}""")
        }, CancellationToken.None);
        await imports.CreateAsync(new BackgroundJob<ImportJobDefinition>
        {
            JobId = "job", JobType = 2, Status = "Completed",
            Definition = new ImportJobDefinition
            {
                TenantId = 1, InputFormat = "application/fhir+ndjson", InputSource = "Patient",
                Mode = "IncrementalLoad", InputFiles = [new InputFileInfo { Type = "Patient", Url = "input.ndjson" }]
            },
            Result = JsonNode.Parse("""{"TotalResources":1,"TotalErrors":0,"ErrorFileUrl":null}""")
        }, CancellationToken.None);
        var handler = new GetJobStatusHandler(new TaskHubClient(Substitute.For<IOrchestrationServiceClient>()), imports, exports);
        var mediator = Substitute.For<IMediator>();
        mediator.SendAsync(Arg.Any<GetJobStatusQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => handler.HandleAsync(call.Arg<GetJobStatusQuery>(), call.Arg<CancellationToken>()));
        var tool = new GetJobStatusTool(new FhirRequestContextAccessor(), tenants, mediator);

        var status = await tool.GetJobStatusAsync("job", jobType, 1);
        var json = JsonSerializer.SerializeToNode(status, JsonSerializerOptions.Web)!;

        if (jobType == "Export")
        {
            json["result"]!["outputFiles"].ShouldNotBeNull();
            json["result"]!["outputFiles"]!["Patient"]!.GetValue<string>().ShouldBe("partition/1/export/job/Patient.ndjson");
        }
        else
        {
            json["definition"]!["inputFileCount"].ShouldNotBeNull();
            json["definition"]!["inputFileCount"]!.GetValue<int>().ShouldBe(1);
        }
        json["result"]!["totalResources"]!.GetValue<long>().ShouldBe(1);
    }
}
