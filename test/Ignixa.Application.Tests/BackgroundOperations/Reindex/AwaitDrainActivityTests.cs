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
    // Spec §8.6: an incomplete transaction below the cutoff holds the drain; past DrainWarningAfter it is logged.
    [Theory]
    [InlineData(4, 0)]
    [InlineData(6, 1)]
    public async Task GivenTheDrainIsStillWaiting_WhenPolled_ThenOnlyAWaitPastTheThresholdWarnsWithTheOldestTransaction(
        int waitedMinutes, int expectedWarnings)
    {
        var store = Substitute.For<IReindexStore>();
        store.GetVisibleWatermarkAsync(Arg.Any<CancellationToken>()).Returns(100);
        store.GetOldestIncompleteTransactionAsync(50, Arg.Any<CancellationToken>())
            .Returns(new IncompleteTransaction(25, DateTime.UtcNow, DateTime.UtcNow));
        var stores = Substitute.For<IReindexStoreFactory>();
        stores.GetReindexStoreAsync(1, Arg.Any<CancellationToken>()).Returns(store);
        var progress = new ReindexProgressReporter(Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>(), TimeProvider.System);
        var logs = new List<(LogLevel Level, string Message)>();
        var activity = new AwaitDrainActivity(stores, TimeProvider.System, progress, new CapturingLoggerFactory(logs).CreateLogger<AwaitDrainActivity>());
        var input = new AwaitDrainInput("job", 1, 50, DateTime.UtcNow - TimeSpan.FromMinutes(waitedMinutes), TimeSpan.FromMinutes(5));

        var json = await activity.RunAsync(null!, JsonSerializer.Serialize(new[] { input }));

        JsonSerializer.Deserialize<bool>(json).ShouldBeFalse();
        var warnings = logs.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message).ToArray();
        warnings.Length.ShouldBe(expectedWarnings);
        warnings.ShouldAllBe(warning => warning.Contains("oldest incomplete transaction 25") && warning.Contains("visible watermark 100"));
    }
}
