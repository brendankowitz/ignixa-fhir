using System.Text.Json;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.BulkDelete.Activities;
using Ignixa.Application.BackgroundOperations.BulkDelete.Models;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.BulkDelete;

public sealed class CompleteBulkDeleteJobActivityTests : IAsyncLifetime
{
    private const int TenantId = 1;
    private const string JobId = "job";
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);
    private readonly InMemoryBackgroundJobRepository<BulkDeleteJobDefinition> _jobs;

    public CompleteBulkDeleteJobActivityTests()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.Mode.Returns(TenantMode.Isolated);
        _jobs = new(tenants, NullLogger<InMemoryBackgroundJobRepository<BulkDeleteJobDefinition>>.Instance);
    }

    public Task InitializeAsync() => _jobs.CreateAsync(new BackgroundJob<BulkDeleteJobDefinition>
    {
        JobId = JobId,
        JobType = (int)BackgroundJobType.BulkDelete,
        Status = "Running",
        Definition = new BulkDeleteJobDefinition
        {
            TenantId = TenantId, ResourceTypes = ["Patient"], SearchQuery = string.Empty, Mode = BulkDeleteMode.SoftDelete,
            ExcludedResourceTypes = [],
        },
    }, CancellationToken.None);

    [Fact]
    public async Task GivenARunningJob_WhenCompletingSuccessfully_ThenTheCountsAreTheResultAndProgress()
    {
        var recorded = await RunAsync(new CompleteBulkDeleteJobInput(JobId, TenantId, true, new() { ["Patient"] = 4 }, null));

        recorded.ShouldBeTrue();
        var job = (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!;
        job.Status.ShouldBe("Completed");
        job.EndDate.ShouldNotBeNull();
        job.ErrorMessage.ShouldBeNull();
        var result = job.Result.Deserialize<BulkDeleteJobResult>(WebOptions)!;
        result.ResourceDeletedCount.ShouldBe(new Dictionary<string, long> { ["Patient"] = 4 });
        result.Issues.ShouldBeEmpty();
        job.Progress.Deserialize<BulkDeleteJobProgress>(WebOptions)!.ResourceDeletedCount["Patient"].ShouldBe(4);
    }

    [Fact]
    public async Task GivenARunningJob_WhenCompletingWithAFailure_ThenTheJobIsFailedWithTheErrorAsAnIssue()
    {
        var recorded = await RunAsync(new CompleteBulkDeleteJobInput(JobId, TenantId, false, new() { ["Patient"] = 2 }, "boom"));

        recorded.ShouldBeTrue();
        var job = (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!;
        job.Status.ShouldBe("Failed");
        job.ErrorMessage.ShouldBe("boom");
        var result = job.Result.Deserialize<BulkDeleteJobResult>(WebOptions)!;
        result.Issues.ShouldBe(["boom"]);
        result.ResourceDeletedCount["Patient"].ShouldBe(2);
    }

    [Fact]
    public async Task GivenACancelledJob_WhenCompleting_ThenTheCancellationStandsAndNothingIsRecorded()
    {
        var job = (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!;
        job.Status = "Cancelled";
        await _jobs.UpdateAsync(job, TenantId, CancellationToken.None);

        var recorded = await RunAsync(new CompleteBulkDeleteJobInput(JobId, TenantId, true, new() { ["Patient"] = 4 }, null));

        recorded.ShouldBeFalse();
        var stored = (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!;
        stored.Status.ShouldBe("Cancelled");
        stored.Result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenACancellationAfterTheRead_WhenCompleting_ThenTheConflictReportsSuperseded()
    {
        var repository = Substitute.For<IBackgroundJobRepository<BulkDeleteJobDefinition>>();
        repository.GetAsync(JobId, TenantId, (int)BackgroundJobType.BulkDelete, Arg.Any<CancellationToken>())
            .Returns(_ => _jobs.GetAsync(JobId, TenantId, (int)BackgroundJobType.BulkDelete, CancellationToken.None));
        repository.UpdateAsync(Arg.Any<BackgroundJob<BulkDeleteJobDefinition>>(), TenantId, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var winner = (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!;
                winner.Status = "Cancelled";
                await _jobs.UpdateAsync(winner, TenantId, CancellationToken.None);
                await _jobs.UpdateAsync(call.Arg<BackgroundJob<BulkDeleteJobDefinition>>(), TenantId, CancellationToken.None);
            });

        var recorded = await RunAsync(new CompleteBulkDeleteJobInput(JobId, TenantId, true, [], null), repository);

        recorded.ShouldBeFalse();
        (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!.Status.ShouldBe("Cancelled");
    }

    private async Task<bool> RunAsync(CompleteBulkDeleteJobInput input, IBackgroundJobRepository<BulkDeleteJobDefinition>? repository = null)
    {
        var activity = new CompleteBulkDeleteJobActivity(repository ?? _jobs, NullLogger<CompleteBulkDeleteJobActivity>.Instance);
        var json = await activity.RunAsync(new TaskContext(new OrchestrationInstance { InstanceId = JobId }),
            JsonSerializer.Serialize(new[] { input }));
        return JsonSerializer.Deserialize<bool>(json);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}
