using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Export.Activities;
using Ignixa.Application.BackgroundOperations.Export.Models;
using Ignixa.Application.BackgroundOperations.Export.Orchestrations;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class ExportPartitionManifestTests
{
    [Theory]
    [InlineData(2L, 3L, 5L)]
    [InlineData(2147483647L, 1L, 2147483648L)]
    public async Task GivenMultiplePartitions_WhenExportCompletes_ThenPersistedPathsMatchWorkerPathsAndCounts(
        long firstCount, long secondCount, long expectedTotal)
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        var repository = new InMemoryBackgroundJobRepository<ExportJobDefinition>(
            tenants, NullLogger<InMemoryBackgroundJobRepository<ExportJobDefinition>>.Instance);
        await repository.CreateAsync(new BackgroundJob<ExportJobDefinition>
        {
            JobId = "job", JobType = 1, Status = "Running",
            Definition = new ExportJobDefinition
            {
                TenantId = 1, ResourceTypes = ["Patient"], TypeFilters = new Dictionary<string, string>(),
                OutputFormat = "application/fhir+ndjson", OutputPath = "partition/1/export/job"
            }
        }, CancellationToken.None);
        var context = Substitute.For<OrchestrationContext>();
        context.ScheduleTask<GetExportRangesOutput>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(new GetExportRangesOutput("Patient", [(1, 9), (10, 19)]));
        var workerPaths = new List<string>();
        context.ScheduleTask<ExportWorkerOutput>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(call =>
            {
                var input = (ExportWorkerInput)call.Arg<object[]>()[0];
                workerPaths.Add(input.OutputPath);
                return new ExportWorkerOutput(input.ResourceType, input.StartSurrogateId, input.EndSurrogateId,
                    input.StartSurrogateId == 1 ? firstCount : secondCount, 100);
            });
        var completion = new CompleteJobActivity(repository, NullLogger<CompleteJobActivity>.Instance);
        context.ScheduleTask<bool>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(async call =>
            {
                var input = (CompleteJobInput)call.Arg<object[]>()[0];
                await completion.RunAsync(new TaskContext(new OrchestrationInstance { InstanceId = "job" }), JsonSerializer.Serialize(new[] { input }));
                return true;
            });

        var result = await new ExportOrchestration().RunTask(context,
            new ExportCoordinatorInput("job", 1, ["Patient"], NumberOfRangesPerType: 2));

        result.Success.ShouldBeTrue();
        var job = (await repository.GetAsync("job", 1, CancellationToken.None))!;
        var persisted = job.Result!.AsObject();
        persisted.First(pair => pair.Key.Equals("totalResources", StringComparison.OrdinalIgnoreCase))
            .Value!.GetValue<long>().ShouldBe(expectedTotal);
        var files = persisted.First(pair => pair.Key.Equals("exportedFiles", StringComparison.OrdinalIgnoreCase)).Value!.AsObject();
        files.Count.ShouldBe(2);
        files.Select(pair => pair.Value!.GetValue<string>()).ShouldBe(workerPaths, ignoreOrder: true);
        var counts = persisted.First(pair => pair.Key.Equals("exportedFileCounts", StringComparison.OrdinalIgnoreCase)).Value!.AsObject();
        counts["Patient-1-9"]!.GetValue<long>().ShouldBe(firstCount);
        counts["Patient-10-19"]!.GetValue<long>().ShouldBe(secondCount);
    }
}
