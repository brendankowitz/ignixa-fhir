using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class GetReindexStatusHandlerTests
{
    [Fact]
    public async Task GivenStaleQueuedJob_WhenStatusIsRead_ThenItIsFlaggedStale()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        repository.GetAsync("job", 1, Arg.Any<CancellationToken>())
            .Returns(new BackgroundJob<ReindexJobDefinition>
            {
                JobId = "job",
                JobType = (int)BackgroundJobType.Reindex,
                Status = "Queued",
                Definition = ReindexJobDefinition.CreateForTest(),
                CreateDate = now.AddHours(-1),
                HeartbeatDate = now.AddHours(-1)
            });
        var timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(now);
        var handler = new GetReindexStatusHandler(
            repository,
            Options.Create(new ReindexOptions { StaleJobTimeout = TimeSpan.FromMinutes(30) }),
            timeProvider,
            NullLogger<GetReindexStatusHandler>.Instance);

        var result = await handler.HandleAsync(
            new GetReindexStatusQuery("job"),
            CancellationToken.None);

        result.ShouldNotBeNull();
        result.IsStale.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenActiveAndTerminalJobs_WhenListingJobs_ThenAllActiveAndOnlyRecentTerminalsAreReturned()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var repository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        repository.ListAsync((int)BackgroundJobType.Reindex, Arg.Any<CancellationToken>())
            .Returns(
            [
                Job("running", "Running", now.AddMinutes(-3), null),
                Job("queued", "Queued", now.AddMinutes(-2), null),
                Job("old", "Completed", now.AddMinutes(-10), now.AddMinutes(-10)),
                Job("recent", "Completed", now.AddMinutes(-1), now.AddMinutes(-1))
            ]);
        var handler = new GetReindexJobsHandler(
            repository,
            Options.Create(new ReindexOptions { StaleJobTimeout = TimeSpan.FromMinutes(30) }),
            TimeProvider.System);

        var result = await handler.HandleAsync(new GetReindexJobsQuery(1), CancellationToken.None);

        result.Select(job => job.JobId).ShouldBe(["queued", "running", "recent"]);
        result.All(job => job.Definition is not null).ShouldBeTrue();
    }

    private static BackgroundJob<ReindexJobDefinition> Job(
        string jobId,
        string status,
        DateTimeOffset queuedTime,
        DateTimeOffset? endTime) =>
        new()
        {
            JobId = jobId,
            JobType = (int)BackgroundJobType.Reindex,
            Status = status,
            Definition = ReindexJobDefinition.CreateForTest(),
            CreateDate = queuedTime,
            HeartbeatDate = queuedTime,
            EndDate = endTime
        };
}
