using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Activities;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public sealed class AwaitDrainActivityTests
{
    [Fact]
    public async Task GivenAnIncompleteTransactionBelowTheCutoff_WhenTheWatermarkHasAdvanced_ThenDrainStillWaits()
    {
        var store = Substitute.For<IReindexStore>();
        store.GetVisibleWatermarkAsync(Arg.Any<CancellationToken>()).Returns(100);
        store.GetOldestIncompleteTransactionAsync(50, Arg.Any<CancellationToken>())
            .Returns(new IncompleteTransaction(25, DateTime.UtcNow, DateTime.UtcNow));
        var stores = Substitute.For<IReindexStoreFactory>();
        stores.GetReindexStoreAsync(1, Arg.Any<CancellationToken>()).Returns(store);
        var jobRepository = Substitute.For<IBackgroundJobRepository<ReindexJobDefinition>>();
        var progress = new ReindexProgressReporter(jobRepository, TimeProvider.System);
        var heartbeat = new ReindexActivityHeartbeat(
            progress,
            Options.Create(new ReindexOptions()),
            TimeProvider.System,
            NullLogger<ReindexActivityHeartbeat>.Instance);
        var activity = new AwaitDrainActivity(
            stores,
            TimeProvider.System,
            heartbeat,
            NullLogger<AwaitDrainActivity>.Instance);
        var input = new AwaitDrainInput(
            "job",
            1,
            50,
            DateTime.UtcNow,
            TimeSpan.FromMinutes(5));

        var json = await activity.RunAsync(null!, JsonSerializer.Serialize(new[] { input }));
        var result = JsonSerializer.Deserialize<AwaitDrainOutput>(json)!;

        result.IsDrained.ShouldBeFalse();
        result.VisibleWatermark.ShouldBe(100);
    }
}
