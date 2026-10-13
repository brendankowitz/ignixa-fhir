using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using static Ignixa.Application.Tests.BackgroundOperations.Reindex.ReindexTestHelper;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class GetReindexStatusHandlerTests
{
    private static readonly IOptions<ReindexOptions> JobOptions = Options.Create(new ReindexOptions
    {
        StaleJobTimeout = TimeSpan.FromMinutes(30),
        BarrierDelay = TimeSpan.FromHours(3)
    });

    private readonly InMemoryBackgroundJobRepository<ReindexJobDefinition> _jobs = JobRepository();

    // §8.6: a job silent past StaleJobTimeout (plus BarrierDelay in that phase) is flagged stale.
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

        var result = await StatusAsync("job");

        result.ShouldNotBeNull().IsStale.ShouldBe(expectedStale);
    }

    // §6.2: the list holds every active and queued job and only the most recent terminal ones; an id that is not a
    // reindex job is a 404.
    [Fact]
    public async Task GivenActiveTerminalAndExportJobs_WhenListedOrRead_ThenOnlyReindexJobsAppearAndOnlyRecentTerminals()
    {
        var export = Job("Running", "export");
        export.JobType = (int)BackgroundJobType.Export;
        await _jobs.CreateAsync(export, CancellationToken.None);
        await _jobs.CreateAsync(Job("Running", "running", observed: Now.AddMinutes(-3)), CancellationToken.None);
        await _jobs.CreateAsync(Job("Queued", "queued", observed: Now.AddMinutes(-2)), CancellationToken.None);
        await _jobs.CreateAsync(Job("Completed", "old", observed: Now.AddMinutes(-10)), CancellationToken.None);
        await _jobs.CreateAsync(Job("Completed", "recent", observed: Now.AddMinutes(-1)), CancellationToken.None);

        var result = await new GetReindexJobsHandler(_jobs, JobOptions, new FakeTimeProvider(Now))
            .HandleAsync(new GetReindexJobsQuery(1), CancellationToken.None);

        result.Select(job => job.JobId).ShouldBe(["queued", "running", "recent"]);
        result.All(job => job.Definition is not null).ShouldBeTrue();
        (await StatusAsync("export")).ShouldBeNull();
    }

    private Task<ReindexStatusResult?> StatusAsync(string jobId) =>
        new GetReindexStatusHandler(_jobs, JobOptions, new FakeTimeProvider(Now), NullLogger<GetReindexStatusHandler>.Instance)
            .HandleAsync(new GetReindexStatusQuery(jobId), CancellationToken.None);
}
