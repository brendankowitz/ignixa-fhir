using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using static Ignixa.Application.Tests.BackgroundOperations.Reindex.ReindexTestHelper;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexProgressReporterTests
{
    private readonly InMemoryBackgroundJobRepository<ReindexJobDefinition> _jobs = JobRepository();

    [Theory]
    [InlineData(null)]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task GivenMissingOrClosedJob_WhenLateProgressOrHeartbeatIsReported_ThenNoOpReportsClosedAndPreservesJob(string? status)
    {
        if (status is not null)
        {
            await CreateJobAsync(status, heartbeat: DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        var reporter = CreateReporter(_jobs);
        var before = await SerializedJobAsync();

        (await reporter.ReportAsync("job", Snapshot(10), CancellationToken.None)).ShouldBeFalse();
        (await reporter.HeartbeatAsync("job", CancellationToken.None)).ShouldBeFalse();

        (await SerializedJobAsync()).ShouldBe(before);
    }

    [Fact]
    public async Task GivenRunningJob_WhenProgressIsReported_ThenTheTypedSnapshotIsStoredAsCamelCaseJsonWithHeartbeat()
    {
        await CreateJobAsync("Running", heartbeat: DateTimeOffset.UtcNow.AddMinutes(-5));
        var snapshot = Snapshot(15);

        (await CreateReporter(_jobs).ReportAsync("job", snapshot, CancellationToken.None)).ShouldBeTrue();

        var stored = (await _jobs.GetAsync("job", 1, CancellationToken.None))!;
        stored.HeartbeatDate.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
        stored.Progress!["phase"]!.GetValue<string>().ShouldBe("Reindexing");
        stored.Progress["tenants"]![0]!["status"]!.GetValue<string>().ShouldBe("Reindexing");
        stored.Progress["tenants"]![0]!["resourcesReindexed"]!.GetValue<long>().ShouldBe(15);
        ReindexProgress.FromJson(stored.Progress)!.ToJson().ToJsonString().ShouldBe(snapshot.ToJson().ToJsonString());
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task GivenJobClosesAfterRead_WhenProgressWrites_ThenTheTerminalStateIsPreserved(string status)
    {
        await CreateJobAsync("Running", heartbeat: Now);
        var proxy = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        proxy.GetAsync("job", 1, Arg.Any<CancellationToken>()).Returns(_ => _jobs.GetAsync("job", 1, CancellationToken.None));
        string? closed = null;
        proxy.UpdateAsync(Arg.Any<BackgroundJob<ReindexJobDefinition>>(), 1, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var terminal = (await _jobs.GetAsync("job", 1, CancellationToken.None))!;
                terminal.Status = status;
                await _jobs.UpdateAsync(terminal, 1, CancellationToken.None);
                closed = await SerializedJobAsync();
                await _jobs.UpdateAsync(call.Arg<BackgroundJob<ReindexJobDefinition>>(), 1, CancellationToken.None);
            });

        (await CreateReporter(proxy).ReportAsync("job", Snapshot(15), CancellationToken.None)).ShouldBeFalse();

        (await SerializedJobAsync()).ShouldBe(closed);
    }

    [Fact]
    public async Task GivenJobStoreUnavailable_WhenWorkWouldStart_ThenTheStartingHeartbeatFailsBeforeTheWork()
    {
        var repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        repository.GetAsync("job", 1, Arg.Any<CancellationToken>())
            .Returns<Task<BackgroundJob<ReindexJobDefinition>?>>(_ => throw new TimeoutException("job store unavailable"));
        var started = false;

        await Should.ThrowAsync<TimeoutException>(() => CreateReporter(repository).RunWithHeartbeatAsync(
            "job",
            _ =>
            {
                started = true;
                return Task.FromResult(17);
            },
            CancellationToken.None));

        started.ShouldBeFalse();
    }

    private static ReindexProgress Snapshot(long count) =>
        new(ReindexPhase.Reindexing)
        {
            Tenants =
            [
                ReindexTenantProgress.Create(1) with
                {
                    Status = ReindexTenantStatus.Reindexing,
                    ResourcesToReindex = 15,
                    ResourcesReindexed = count
                }
            ]
        };

    private async Task CreateJobAsync(string status, DateTimeOffset heartbeat)
    {
        var job = Job(status);
        job.HeartbeatDate = heartbeat;
        job.Progress = new JsonObject();
        await _jobs.CreateAsync(job, CancellationToken.None);
    }

    private async Task<string> SerializedJobAsync() =>
        JsonSerializer.Serialize(await _jobs.GetAsync("job", 1, CancellationToken.None));

    private static ReindexProgressReporter CreateReporter(IBackgroundJobRepository<ReindexJobDefinition> repository) =>
        new(repository, TimeProvider.System);
}
