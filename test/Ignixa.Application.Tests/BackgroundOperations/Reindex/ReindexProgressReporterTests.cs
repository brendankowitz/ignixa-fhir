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
    public async Task GivenClosedJob_WhenLateProgressOrHeartbeatIsReported_ThenNoOpPreservesJobAndHeartbeat(string status)
    {
        var repository = CreateRepository();
        var job = CreateJob();
        job.Status = status;
        job.HeartbeatDate = DateTimeOffset.UtcNow.AddMinutes(-1);
        await repository.CreateAsync(job, CancellationToken.None);
        var reporter = CreateReporter(repository);
        var before = JsonSerializer.Serialize(await repository.GetAsync("job", 1, CancellationToken.None));

        (await reporter.ReportAsync(Snapshot(1, 10), CancellationToken.None)).ShouldBeFalse();
        (await reporter.HeartbeatAsync("job", CancellationToken.None)).ShouldBeFalse();

        JsonSerializer.Serialize(await repository.GetAsync("job", 1, CancellationToken.None)).ShouldBe(before);
    }

    [Fact]
    public async Task GivenRepeatedAndDelayedSnapshots_WhenReported_ThenCountsAreIdempotentAndNeverRegress()
    {
        var repository = CreateRepository();
        await repository.CreateAsync(CreateJob(), CancellationToken.None);
        var reporter = CreateReporter(repository);

        await reporter.ReportAsync(Snapshot(1, 10), CancellationToken.None);
        await reporter.ReportAsync(Snapshot(2, 15), CancellationToken.None);
        await reporter.ReportAsync(Snapshot(2, 15), CancellationToken.None);
        await reporter.ReportAsync(Snapshot(1, 10), CancellationToken.None);
        await reporter.HeartbeatAsync("job", CancellationToken.None);

        var job = (await repository.GetAsync("job", 1, CancellationToken.None))!;
        job.Progress!["totalResourcesToReindex"]!.GetValue<long>().ShouldBe(15);
        job.Progress["resourcesSuccessfullyReindexed"]!.GetValue<long>().ShouldBe(15);
        job.Progress["progress"]!.GetValue<double>().ShouldBe(99.9);
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

        (await CreateReporter(proxy).ReportAsync(Snapshot(1, 15), CancellationToken.None)).ShouldBeFalse();

        JsonSerializer.Serialize(await repository.GetAsync("job", 1, CancellationToken.None)).ShouldBe(closed);
    }

    private static PersistReindexProgressInput Snapshot(long sequence, long count) =>
        new("job", sequence, "Reindexing",
            [ReindexTenantState.Create(1) with
            {
                Phase = "Reindexing", ResourcesToReindex = 15, ResourcesRead = count, ResourcesReindexed = count
            }]);

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
        new(repository, TimeProvider.System);
}
