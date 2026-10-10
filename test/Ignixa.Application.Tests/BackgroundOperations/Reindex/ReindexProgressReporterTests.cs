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

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexProgressReporterTests
{
    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task GivenClosedJob_WhenLateProgressOrHeartbeatIsReported_ThenNoOpReportsClosedAndPreservesJob(string status)
    {
        var repository = CreateRepository();
        var job = CreateJob();
        job.Status = status;
        job.HeartbeatDate = DateTimeOffset.UtcNow.AddMinutes(-1);
        await repository.CreateAsync(job, CancellationToken.None);
        var reporter = CreateReporter(repository);
        var before = JsonSerializer.Serialize(await repository.GetAsync("job", 1, CancellationToken.None));

        (await reporter.ReportAsync("job", Snapshot(10), CancellationToken.None)).ShouldBeFalse();
        (await reporter.HeartbeatAsync("job", CancellationToken.None)).ShouldBeFalse();

        JsonSerializer.Serialize(await repository.GetAsync("job", 1, CancellationToken.None)).ShouldBe(before);
    }

    [Fact]
    public async Task GivenMissingJob_WhenProgressIsReported_ThenItIsReportedClosed()
    {
        var reporter = CreateReporter(CreateRepository());

        (await reporter.ReportAsync("job", Snapshot(10), CancellationToken.None)).ShouldBeFalse();
        (await reporter.HeartbeatAsync("job", CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenRunningJob_WhenProgressIsReported_ThenTheTypedSnapshotIsStoredAsCamelCaseJsonWithHeartbeat()
    {
        var repository = CreateRepository();
        var job = CreateJob();
        job.HeartbeatDate = DateTimeOffset.UtcNow.AddMinutes(-5);
        await repository.CreateAsync(job, CancellationToken.None);
        var reporter = CreateReporter(repository);
        var snapshot = Snapshot(15);

        (await reporter.ReportAsync("job", snapshot, CancellationToken.None)).ShouldBeTrue();

        var stored = (await repository.GetAsync("job", 1, CancellationToken.None))!;
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
        var repository = CreateRepository();
        await repository.CreateAsync(CreateJob(), CancellationToken.None);
        var proxy = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        proxy.GetAsync("job", 1, Arg.Any<CancellationToken>())
            .Returns(_ => repository.GetAsync("job", 1, CancellationToken.None));
        string? closed = null;
        proxy.UpdateAsync(Arg.Any<BackgroundJob<ReindexJobDefinition>>(), 1, Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var terminal = (await repository.GetAsync("job", 1, CancellationToken.None))!;
                terminal.Status = status;
                await repository.UpdateAsync(terminal, 1, CancellationToken.None);
                closed = JsonSerializer.Serialize(await repository.GetAsync("job", 1, CancellationToken.None));
                await repository.UpdateAsync(
                    call.Arg<BackgroundJob<ReindexJobDefinition>>(), 1, CancellationToken.None);
            });

        (await CreateReporter(proxy).ReportAsync("job", Snapshot(15), CancellationToken.None)).ShouldBeFalse();

        JsonSerializer.Serialize(await repository.GetAsync("job", 1, CancellationToken.None)).ShouldBe(closed);
    }

    [Fact]
    public async Task GivenWorkBetweenHeartbeats_WhenTheFinishingHeartbeatFails_ThenTheWorkResultIsStillReturned()
    {
        var repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        repository.GetAsync("job", 1, Arg.Any<CancellationToken>()).Returns(_ => CreateJob());
        var updates = 0;
        repository.UpdateAsync(Arg.Any<BackgroundJob<ReindexJobDefinition>>(), 1, Arg.Any<CancellationToken>())
            .Returns(_ => ++updates == 1
                ? Task.CompletedTask
                : Task.FromException(new TimeoutException("job store unavailable")));

        var result = await CreateReporter(repository).RunWithHeartbeatAsync(
            "job", _ => Task.FromResult(17), CancellationToken.None);

        result.ShouldBe(17);
        updates.ShouldBe(2);
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
                    ResourcesRead = count,
                    ResourcesReindexed = count
                }
            ]
        };

    private static BackgroundJob<ReindexJobDefinition> CreateJob() =>
        new()
        {
            JobId = "job", JobType = 4, Status = "Running", Definition = ReindexTestHelper.CreateJobDefinition(),
            Progress = new JsonObject()
        };

    private static InMemoryBackgroundJobRepository<ReindexJobDefinition> CreateRepository() =>
        new(Substitute.For<ITenantConfigurationStore>(),
            NullLogger<InMemoryBackgroundJobRepository<ReindexJobDefinition>>.Instance);

    private static ReindexProgressReporter CreateReporter(IBackgroundJobRepository<ReindexJobDefinition> repository) =>
        new(repository, TimeProvider.System, NullLogger<ReindexProgressReporter>.Instance);
}
