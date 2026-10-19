using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using DurableTask.Core.Serializing;
using Ignixa.Application.BackgroundOperations.BulkDelete;
using Ignixa.Application.BackgroundOperations.BulkDelete.Orchestrations;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.BulkDelete;

public class GetBulkDeleteStatusHandlerTests
{
    private const int TenantId = 1;
    private const string JobId = "job";
    private readonly ITenantConfigurationStore _tenants = Substitute.For<ITenantConfigurationStore>();
    private readonly InMemoryBackgroundJobRepository<BulkDeleteJobDefinition> _jobs;
    private readonly IOrchestrationServiceClient _orchestrations = Substitute.For<IOrchestrationServiceClient>();

    public GetBulkDeleteStatusHandlerTests()
    {
        // Distributed mode: the repository does not check tenant ownership, so the handler must.
        _tenants.Mode.Returns(TenantMode.Distributed);
        _jobs = new(_tenants, NullLogger<InMemoryBackgroundJobRepository<BulkDeleteJobDefinition>>.Instance);
    }

    [Fact]
    public async Task GivenNoJob_WhenReadingStatus_ThenItIsNotFound()
    {
        await Should.ThrowAsync<KeyNotFoundException>(() => HandleAsync());
    }

    [Fact]
    public async Task GivenAnotherTenantsJob_WhenReadingStatus_ThenItIsNotFound()
    {
        await CreateJobAsync("Running", ownerTenantId: 2);

        await Should.ThrowAsync<KeyNotFoundException>(() => HandleAsync());
    }

    [Fact]
    public async Task GivenAJobOfAnotherType_WhenReadingStatus_ThenItIsNotFound()
    {
        await CreateJobAsync("Running", jobType: BackgroundJobType.Export);

        await Should.ThrowAsync<KeyNotFoundException>(() => HandleAsync());
    }

    [Fact]
    public async Task GivenTheSystemPartition_WhenReadingStatus_ThenItIsNotFoundWithoutAJobLookup()
    {
        var repository = Substitute.For<IBackgroundJobRepository<BulkDeleteJobDefinition>>();
        var handler = new GetBulkDeleteStatusHandler(
            new TaskHubClient(_orchestrations), repository, NullLogger<GetBulkDeleteStatusHandler>.Instance);

        await Should.ThrowAsync<KeyNotFoundException>(() =>
            handler.HandleAsync(new GetBulkDeleteStatusQuery(0, JobId), CancellationToken.None));

        await repository.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default, default);
    }

    [Fact]
    public async Task GivenAJobIdOfAnotherType_WhenReadingStatus_ThenTheTypedLookupReportsNotFoundWithoutReadingItsDefinition()
    {
        var repository = Substitute.For<IBackgroundJobRepository<BulkDeleteJobDefinition>>();
        repository.GetAsync(JobId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BackgroundJob<BulkDeleteJobDefinition>?>(new JsonException("export definition")));
        repository.GetAsync(JobId, TenantId, (int)BackgroundJobType.BulkDelete, Arg.Any<CancellationToken>())
            .Returns((BackgroundJob<BulkDeleteJobDefinition>?)null);
        var handler = new GetBulkDeleteStatusHandler(
            new TaskHubClient(_orchestrations), repository, NullLogger<GetBulkDeleteStatusHandler>.Instance);

        await Should.ThrowAsync<KeyNotFoundException>(() =>
            handler.HandleAsync(new GetBulkDeleteStatusQuery(TenantId, JobId), CancellationToken.None));
    }

    [Theory]
    [InlineData(OrchestrationStatus.Running)]
    [InlineData(OrchestrationStatus.ContinuedAsNew)]
    public async Task GivenAWorkingOrchestrationExecution_WhenReadingStatus_ThenTheJobIsRunning(OrchestrationStatus orchestrationStatus)
    {
        await CreateJobAsync("Queued");
        SetOrchestration(orchestrationStatus);

        var status = await HandleAsync();

        status.Status.ShouldBe("Running");
    }

    [Fact]
    public async Task GivenACompletedJob_WhenReadingStatus_ThenTheResultIsAuthoritativeAndZeroCountsAreOmitted()
    {
        await CreateJobAsync("Completed", result: """{"resourceDeletedCount":{"Patient":2,"Observation":0},"issues":[]}""");

        var status = await HandleAsync();

        status.Status.ShouldBe("Completed");
        status.ResourceDeletedCount.ShouldBe(new Dictionary<string, long> { ["Patient"] = 2 });
        status.Issues.ShouldBeEmpty();
        status.ErrorMessage.ShouldBeNull();
        await _orchestrations.DidNotReceiveWithAnyArgs().GetOrchestrationStateAsync(default(string)!, default(bool));
    }

    [Fact]
    public async Task GivenACompletedJobWithoutAResult_WhenReadingStatus_ThenTheInvalidRecordIsReported()
    {
        await CreateJobAsync("Completed");

        await Should.ThrowAsync<JsonException>(() => HandleAsync());
    }

    [Fact]
    public async Task GivenInvalidProgress_WhenReadingStatus_ThenTheInvalidRecordIsReported()
    {
        await CreateJobAsync("Cancelled", progress: """{"resourceDeletedCount":{"Patient":"many"}}""");

        await Should.ThrowAsync<JsonException>(() => HandleAsync());
    }

    [Fact]
    public async Task GivenACancelledJob_WhenReadingStatus_ThenItsCountsComeFromProgress()
    {
        await CreateJobAsync("Cancelled", progress: """{"resourceDeletedCount":{"Patient":3}}""");

        var status = await HandleAsync();

        status.Status.ShouldBe("Cancelled");
        status.ResourceDeletedCount.ShouldBe(new Dictionary<string, long> { ["Patient"] = 3 });
    }

    [Fact]
    public async Task GivenAPendingOrchestration_WhenReadingStatus_ThenTheJobIsQueued()
    {
        await CreateJobAsync("Queued");
        SetOrchestration(OrchestrationStatus.Pending);

        var status = await HandleAsync();

        status.Status.ShouldBe("Queued");
        status.ResourceDeletedCount.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenARunningOrchestration_WhenReadingStatus_ThenTheJobIsRunningWithItsProgress()
    {
        await CreateJobAsync("Queued", progress: """{"resourceDeletedCount":{"Patient":5}}""");
        SetOrchestration(OrchestrationStatus.Running);

        var status = await HandleAsync();

        status.Status.ShouldBe("Running");
        status.ResourceDeletedCount.ShouldBe(new Dictionary<string, long> { ["Patient"] = 5 });
        (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!.StartDate.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenACompletedOrchestrationWithAnUnfinalizedJob_WhenReadingStatus_ThenItsOutputCompletesTheJob()
    {
        await CreateJobAsync("Running");
        SetOrchestration(OrchestrationStatus.Completed,
            Output(new BulkDeleteOrchestrationOutput(true, false, new() { ["Patient"] = 3 }, null)));

        var status = await HandleAsync();

        status.Status.ShouldBe("Completed");
        status.ResourceDeletedCount.ShouldBe(new Dictionary<string, long> { ["Patient"] = 3 });
        (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!.Status.ShouldBe("Completed");
    }

    [Fact]
    public async Task GivenAFailedOrchestrationOutputWithAnUnfinalizedJob_WhenReadingStatus_ThenTheJobFailsWithItsError()
    {
        await CreateJobAsync("Running");
        SetOrchestration(OrchestrationStatus.Completed,
            Output(new BulkDeleteOrchestrationOutput(false, false, new() { ["Patient"] = 1 }, "boom")));

        var status = await HandleAsync();

        status.Status.ShouldBe("Failed");
        status.ErrorMessage.ShouldBe("boom");
        status.Issues.ShouldBe(["boom"]);
        status.ResourceDeletedCount.ShouldBe(new Dictionary<string, long> { ["Patient"] = 1 });
    }

    [Fact]
    public async Task GivenAnInvalidOrchestrationOutput_WhenReadingStatus_ThenTheInvalidRecordIsReported()
    {
        await CreateJobAsync("Running");
        SetOrchestration(OrchestrationStatus.Completed, "{not json");

        await Should.ThrowAsync<JsonException>(() => HandleAsync());
    }

    [Fact]
    public async Task GivenAFailedOrchestration_WhenReadingStatus_ThenTheJobFailsKeepingItsProgress()
    {
        await CreateJobAsync("Running", progress: """{"resourceDeletedCount":{"Patient":2}}""");
        SetOrchestration(OrchestrationStatus.Failed, "completion could not be recorded");

        var status = await HandleAsync();

        status.Status.ShouldBe("Failed");
        status.ErrorMessage.ShouldBe("completion could not be recorded");
        status.Issues.ShouldBe(["completion could not be recorded"]);
        status.ResourceDeletedCount.ShouldBe(new Dictionary<string, long> { ["Patient"] = 2 });
    }

    [Fact]
    public async Task GivenATerminatedOrchestration_WhenReadingStatus_ThenTheJobIsCancelledWithItsProgress()
    {
        await CreateJobAsync("Running", progress: """{"resourceDeletedCount":{"Patient":2}}""");
        SetOrchestration(OrchestrationStatus.Terminated);

        var status = await HandleAsync();

        status.Status.ShouldBe("Cancelled");
        status.ResourceDeletedCount.ShouldBe(new Dictionary<string, long> { ["Patient"] = 2 });
    }

    private Task<GetBulkDeleteStatusResult> HandleAsync() =>
        new GetBulkDeleteStatusHandler(new TaskHubClient(_orchestrations), _jobs, NullLogger<GetBulkDeleteStatusHandler>.Instance)
            .HandleAsync(new GetBulkDeleteStatusQuery(TenantId, JobId), CancellationToken.None);

    // Serialized exactly as DurableTask persists orchestration output, including Json.NET $type metadata.
    private static string Output(BulkDeleteOrchestrationOutput output) => JsonDataConverter.Default.Serialize(output);

    private void SetOrchestration(OrchestrationStatus status, string? output = null)
    {
        var state = new OrchestrationState
        {
            OrchestrationInstance = new OrchestrationInstance { InstanceId = JobId },
            OrchestrationStatus = status,
            Output = output,
        };
        _orchestrations.GetOrchestrationStateAsync(JobId, Arg.Any<bool>()).Returns([state]);
        _orchestrations.GetOrchestrationStateAsync(JobId, Arg.Any<string>()).Returns(state);
    }

    private Task CreateJobAsync(
        string status,
        int ownerTenantId = TenantId,
        BackgroundJobType jobType = BackgroundJobType.BulkDelete,
        string? progress = null,
        string? result = null) =>
        _jobs.CreateAsync(new BackgroundJob<BulkDeleteJobDefinition>
        {
            JobId = JobId,
            OrchestrationInstanceId = JobId,
            JobType = (int)jobType,
            Status = status,
            Progress = progress is null ? null : JsonNode.Parse(progress),
            Result = result is null ? null : JsonNode.Parse(result),
            Definition = new BulkDeleteJobDefinition
            {
                TenantId = ownerTenantId, ResourceTypes = ["Patient"], SearchQuery = string.Empty,
                Mode = BulkDeleteMode.SoftDelete, ExcludedResourceTypes = [],
            },
        }, CancellationToken.None);
}
