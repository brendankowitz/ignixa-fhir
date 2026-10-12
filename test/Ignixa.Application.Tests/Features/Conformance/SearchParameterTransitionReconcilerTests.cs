using Ignixa.Application.Features.Conformance;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using static Ignixa.Application.Tests.Features.Conformance.ConformanceEvents;
using static Ignixa.Application.Tests.Features.Conformance.TestPackages;

namespace Ignixa.Application.Tests.Features.Conformance;

public class SearchParameterTransitionReconcilerTests
{
    // N6 on restart: an uncommitted batch is one transition, rescheduled with the full grace from now.
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task GivenAnUncommittedBatchOfShadows_WhenReconciled_ThenOneTransitionIsScheduledWithFullGraceWhateverTheHideTimestamp(
        int hideTimestampOffsetDays)
    {
        const int shadowCount = 70;
        var hiddenAt = DateTimeOffset.UtcNow.AddDays(hideTimestampOffsetDays);
        using var state = new ConformanceState();
        var eventId = 0L;
        for (var index = 1; index <= shadowCount; index++)
        {
            state.ApplyAndTrack(At(++eventId, Activated(Canonical($"seed-{index}"), $"code-{index}", index)));
        }

        var shadows = Enumerable.Range(1, shadowCount).Select(index => Canonical($"shadow-{index}")).ToArray();
        for (var index = 1; index <= shadowCount; index++)
        {
            var shadow = Activated(shadows[index - 1], $"code-{index}", index, Canonical($"seed-{index}"), "shadow@1.0.0");
            state.ApplyAndTrack(At(++eventId, shadow, hiddenAt));
        }

        var batchId = ++eventId;
        state.ApplyAndTrack(At(batchId, PackageActivated("shadow", "1.0.0", shadows), hiddenAt));
        var scheduler = Substitute.For<ISearchParameterTransitionScheduler>();
        var grace = TimeSpan.FromMinutes(3);
        var reconciler = new SearchParameterTransitionReconciler(
            state,
            scheduler,
            Options.Create(new ConformanceTransitionOptions { TransitionGrace = grace }));

        await reconciler.ReconcileAsync(CancellationToken.None);

        scheduler.ReceivedCalls().ShouldHaveSingleItem();
        await scheduler.Received(1).ScheduleAsync(batchId, grace, CancellationToken.None);
    }
}
