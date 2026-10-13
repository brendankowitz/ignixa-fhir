using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Activities;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Tests.Search.Indexing;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public sealed class AwaitDrainActivityTests
{
    // Spec §8.6: an incomplete transaction below the cutoff holds the drain even after the watermark advanced;
    // a drain still waiting past DrainWarningAfter logs the oldest such transaction.
    [Theory]
    [InlineData(4, false)]
    [InlineData(6, true)]
    public async Task GivenTheDrainIsStillWaiting_WhenPolled_ThenOnlyAWaitPastTheThresholdWarnsWithTheOldestTransaction(
        int waitedMinutes,
        bool warns)
    {
        var (drained, logs) = await PollAsync(waited: TimeSpan.FromMinutes(waitedMinutes));

        drained.ShouldBeFalse();
        var warnings = logs.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message).ToArray();
        if (warns)
        {
            var warning = warnings.ShouldHaveSingleItem();
            warning.ShouldContain("oldest incomplete transaction 25");
            warning.ShouldContain("visible watermark 100");
        }
        else
        {
            warnings.ShouldBeEmpty();
        }
    }

    private static async Task<(bool Drained, List<(LogLevel Level, string Message)> Logs)> PollAsync(TimeSpan waited)
    {
        var store = Substitute.For<IReindexStore>();
        store.GetVisibleWatermarkAsync(Arg.Any<CancellationToken>()).Returns(100);
        store.GetOldestIncompleteTransactionAsync(50, Arg.Any<CancellationToken>())
            .Returns(new IncompleteTransaction(25, DateTime.UtcNow, DateTime.UtcNow));
        var stores = Substitute.For<IReindexStoreFactory>();
        stores.GetReindexStoreAsync(1, Arg.Any<CancellationToken>()).Returns(store);
        var jobRepository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        var progress = new ReindexProgressReporter(jobRepository, TimeProvider.System);
        var logs = new List<(LogLevel Level, string Message)>();
        var activity = new AwaitDrainActivity(
            stores,
            TimeProvider.System,
            progress,
            new CapturingLoggerFactory(logs).CreateLogger<AwaitDrainActivity>());
        var input = new AwaitDrainInput("job", 1, 50, DateTime.UtcNow - waited, TimeSpan.FromMinutes(5));

        var json = await activity.RunAsync(null!, JsonSerializer.Serialize(new[] { input }));

        return (JsonSerializer.Deserialize<bool>(json), logs);
    }
}
