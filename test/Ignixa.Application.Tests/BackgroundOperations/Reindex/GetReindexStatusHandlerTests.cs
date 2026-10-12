using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using static Ignixa.Application.Tests.BackgroundOperations.Reindex.ReindexTestHelper;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class GetReindexStatusHandlerTests
{
    private readonly InMemoryBackgroundJobRepository<ReindexJobDefinition> _jobs = JobRepository();

    [Theory]
    [InlineData("Queued", null, true)]
    [InlineData("Running", ReindexPhase.BarrierDelay, false)]
    [InlineData("Running", ReindexPhase.Reindexing, true)]
    public async Task GivenAnHourWithoutHeartbeat_WhenStatusIsRead_ThenItIsStaleUnlessTheBarrierDelayCoversTheSilence(
        string status,
        ReindexPhase? phase,
        bool expectedStale)
    {
        var job = Job(status, observed: Now.AddHours(-1));
        job.Progress = phase is { } value ? new ReindexProgress(value).ToJson() : null;
        await _jobs.CreateAsync(job, CancellationToken.None);
        var handler = new GetReindexStatusHandler(
            _jobs,
            Options.Create(new ReindexOptions
            {
                StaleJobTimeout = TimeSpan.FromMinutes(30),
                BarrierDelay = TimeSpan.FromHours(3)
            }),
            new FixedTimeProvider(Now),
            NullLogger<GetReindexStatusHandler>.Instance);

        var result = await handler.HandleAsync(new GetReindexStatusQuery("job"), CancellationToken.None);

        result.ShouldNotBeNull().IsStale.ShouldBe(expectedStale);
    }

    [Fact]
    public async Task GivenJobOfAnotherType_WhenStatusIsRead_ThenItIsAbsent()
    {
        var export = Job("Running", "export");
        export.JobType = (int)BackgroundJobType.Export;
        await _jobs.CreateAsync(export, CancellationToken.None);
        var handler = new GetReindexStatusHandler(
            _jobs,
            Options.Create(new ReindexOptions()),
            TimeProvider.System,
            NullLogger<GetReindexStatusHandler>.Instance);

        (await handler.HandleAsync(new GetReindexStatusQuery("export"), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task GivenActiveAndTerminalJobs_WhenListingJobs_ThenAllActiveAndOnlyRecentTerminalsAreReturned()
    {
        await _jobs.CreateAsync(Job("Running", "running", observed: Now.AddMinutes(-3)), CancellationToken.None);
        await _jobs.CreateAsync(Job("Queued", "queued", observed: Now.AddMinutes(-2)), CancellationToken.None);
        await _jobs.CreateAsync(Job("Completed", "old", observed: Now.AddMinutes(-10)), CancellationToken.None);
        await _jobs.CreateAsync(Job("Completed", "recent", observed: Now.AddMinutes(-1)), CancellationToken.None);
        var handler = new GetReindexJobsHandler(
            _jobs,
            Options.Create(new ReindexOptions { StaleJobTimeout = TimeSpan.FromMinutes(30) }),
            new FixedTimeProvider(Now));

        var result = await handler.HandleAsync(new GetReindexJobsQuery(1), CancellationToken.None);

        result.Select(job => job.JobId).ShouldBe(["queued", "running", "recent"]);
        result.All(job => job.Definition is not null).ShouldBeTrue();
    }
}
