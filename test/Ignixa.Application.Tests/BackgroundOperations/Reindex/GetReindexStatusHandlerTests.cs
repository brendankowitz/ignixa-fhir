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
}
