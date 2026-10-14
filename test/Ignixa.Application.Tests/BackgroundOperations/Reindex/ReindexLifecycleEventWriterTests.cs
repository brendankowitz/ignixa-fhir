using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Models;
using Shouldly;
using static Ignixa.Application.Tests.BackgroundOperations.Reindex.ReindexTestHelper;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexLifecycleEventWriterTests
{
    // Spec §4.3: a lifecycle event the projection's guard ignores is reported, never silently dropped.
    [Theory]
    [InlineData("custom", 5)]
    [InlineData("unknown", 1)]
    public async Task GivenATargetTheProjectionDoesNotMatch_WhenStarted_ThenItsIgnoredEventIsReported(
        string code, long activationEventId)
    {
        using var state = new ConformanceState();
        state.ApplyAndTrack(Activation(eventId: 1));
        var writer = new ReindexLifecycleEventWriter(new TestSourceEventStore(firstEventId: 2), state);
        var target = Target(activationEventId: activationEventId) with { Code = code };

        var ignored = await writer.StartAsync("job", [target], CancellationToken.None);

        ignored.ShouldHaveSingleItem().ShouldContain(Canonical);
        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Pending);
    }
}
