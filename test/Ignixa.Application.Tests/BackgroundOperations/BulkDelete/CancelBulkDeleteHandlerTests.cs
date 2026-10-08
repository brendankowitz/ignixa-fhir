using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkDelete;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.BulkDelete;

public class CancelBulkDeleteHandlerTests
{
    private const int TenantId = 1;
    private const string JobId = "job";
    private readonly InMemoryBackgroundJobRepository<BulkDeleteJobDefinition> _jobs;
    private readonly IOrchestrationServiceClient _orchestrations = Substitute.For<IOrchestrationServiceClient>();

    public CancelBulkDeleteHandlerTests()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Distributed);
        _jobs = new(tenants, NullLogger<InMemoryBackgroundJobRepository<BulkDeleteJobDefinition>>.Instance);
    }

    [Theory]
    [InlineData(null, BackgroundJobType.BulkDelete)]
    [InlineData(2, BackgroundJobType.BulkDelete)]
    [InlineData(TenantId, BackgroundJobType.Export)]
    public async Task GivenNoBulkDeleteJobForTheTenant_WhenCancelling_ThenItIsNotFound(int? ownerTenantId, BackgroundJobType jobType)
    {
        if (ownerTenantId is { } owner)
        {
            await CreateJobAsync("Running", owner, jobType);
        }

        var outcome = await HandleAsync();

        outcome.ShouldBe(CancelBulkDeleteOutcome.NotFound);
        await _orchestrations.DidNotReceiveWithAnyArgs().ForceTerminateTaskOrchestrationAsync(default!, default!);
    }

    [Fact]
    public async Task GivenTheSystemPartition_WhenCancelling_ThenItIsNotFoundWithoutAJobLookupOrTermination()
    {
        var repository = Substitute.For<IBackgroundJobRepository<BulkDeleteJobDefinition>>();

        var outcome = await new CancelBulkDeleteHandler(
                new TaskHubClient(_orchestrations), repository, NullLogger<CancelBulkDeleteHandler>.Instance)
            .HandleAsync(new CancelBulkDeleteCommand(0, JobId), CancellationToken.None);

        outcome.ShouldBe(CancelBulkDeleteOutcome.NotFound);
        await repository.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default, default);
        await _orchestrations.DidNotReceiveWithAnyArgs().ForceTerminateTaskOrchestrationAsync(default!, default!);
    }

    [Fact]
    public async Task GivenAJobIdOfAnotherType_WhenCancelling_ThenTheTypedLookupReportsNotFoundWithoutReadingItsDefinition()
    {
        var repository = Substitute.For<IBackgroundJobRepository<BulkDeleteJobDefinition>>();
        repository.GetAsync(JobId, TenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BackgroundJob<BulkDeleteJobDefinition>?>(new JsonException("export definition")));
        repository.GetAsync(JobId, TenantId, (int)BackgroundJobType.BulkDelete, Arg.Any<CancellationToken>())
            .Returns((BackgroundJob<BulkDeleteJobDefinition>?)null);

        var outcome = await HandleAsync(repository);

        outcome.ShouldBe(CancelBulkDeleteOutcome.NotFound);
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task GivenATerminalJob_WhenCancelling_ThenNothingChanges(string status)
    {
        await CreateJobAsync(status);

        var outcome = await HandleAsync();

        outcome.ShouldBe(CancelBulkDeleteOutcome.AlreadyTerminal);
        (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!.Status.ShouldBe(status);
        await _orchestrations.DidNotReceiveWithAnyArgs().ForceTerminateTaskOrchestrationAsync(default!, default!);
    }

    [Fact]
    public async Task GivenARunningJob_WhenCancelling_ThenTheOrchestrationIsTerminatedAndTheLatestProgressIsKept()
    {
        await CreateJobAsync("Running");
        _orchestrations.ForceTerminateTaskOrchestrationAsync(JobId, Arg.Any<string>()).Returns(async _ =>
        {
            // A batch records progress while the cancel is in flight.
            var job = (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!;
            job.Progress = JsonNode.Parse("""{"resourceDeletedCount":{"Patient":6}}""");
            await _jobs.UpdateAsync(job, TenantId, CancellationToken.None);
        });

        var outcome = await HandleAsync();

        outcome.ShouldBe(CancelBulkDeleteOutcome.Accepted);
        await _orchestrations.Received(1).ForceTerminateTaskOrchestrationAsync(JobId, "Cancelled by user");
        var stored = (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!;
        stored.Status.ShouldBe("Cancelled");
        stored.EndDate.ShouldNotBeNull();
        stored.Progress!["resourceDeletedCount"]!["Patient"]!.GetValue<long>().ShouldBe(6);
    }

    [Fact]
    public async Task GivenTheJobCompletesDuringCancellation_WhenCancelling_ThenTheCompletionStands()
    {
        await CreateJobAsync("Running");
        var repository = Substitute.For<IBackgroundJobRepository<BulkDeleteJobDefinition>>();
        repository.GetAsync(JobId, TenantId, (int)BackgroundJobType.BulkDelete, Arg.Any<CancellationToken>())
            .Returns(_ => _jobs.GetAsync(JobId, TenantId, (int)BackgroundJobType.BulkDelete, CancellationToken.None));
        repository.UpdateAsync(Arg.Any<BackgroundJob<BulkDeleteJobDefinition>>(), TenantId, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var winner = (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!;
                winner.Status = "Completed";
                await _jobs.UpdateAsync(winner, TenantId, CancellationToken.None);
                await _jobs.UpdateAsync(call.Arg<BackgroundJob<BulkDeleteJobDefinition>>(), TenantId, CancellationToken.None);
            });

        var outcome = await HandleAsync(repository);

        outcome.ShouldBe(CancelBulkDeleteOutcome.AlreadyTerminal);
        (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!.Status.ShouldBe("Completed");
    }

    private Task<CancelBulkDeleteOutcome> HandleAsync(IBackgroundJobRepository<BulkDeleteJobDefinition>? repository = null) =>
        new CancelBulkDeleteHandler(new TaskHubClient(_orchestrations), repository ?? _jobs, NullLogger<CancelBulkDeleteHandler>.Instance)
            .HandleAsync(new CancelBulkDeleteCommand(TenantId, JobId), CancellationToken.None);

    private Task CreateJobAsync(string status, int ownerTenantId = TenantId, BackgroundJobType jobType = BackgroundJobType.BulkDelete) =>
        _jobs.CreateAsync(new BackgroundJob<BulkDeleteJobDefinition>
        {
            JobId = JobId,
            OrchestrationInstanceId = JobId,
            JobType = (int)jobType,
            Status = status,
            Definition = new BulkDeleteJobDefinition
            {
                TenantId = ownerTenantId, ResourceTypes = ["Patient"], SearchQuery = string.Empty,
                Mode = BulkDeleteMode.SoftDelete, ExcludedResourceTypes = [],
            },
        }, CancellationToken.None);
}
