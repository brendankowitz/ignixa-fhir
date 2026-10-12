using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Conformance.Events.Models;
using Ignixa.Domain.Models;
using Shouldly;
using static Ignixa.Application.Tests.BackgroundOperations.Reindex.ReindexTestHelper;

namespace Ignixa.Application.Tests.BackgroundOperations.Reindex;

public class ReindexLifecycleEventWriterTests
{
    [Fact]
    public async Task GivenMultiBaseCanonical_WhenLifecycleCompletes_ThenEachTargetUsesItsOwnOutcome()
    {
        const string canonical = "http://example.org/SearchParameter/shared-custom";
        var state = new ConformanceState();
        state.ApplyAndTrack(Activation(canonical, eventId: 1, resourceType: "Patient", searchParamId: 17));
        state.ApplyAndTrack(Activation(canonical, eventId: 2, resourceType: "Observation", searchParamId: 18));
        var writer = new ReindexLifecycleEventWriter(new TestSourceEventStore(firstEventId: 3), state);
        var patient = new ReindexParameterDefinition(canonical, "custom", "Patient", 17, 1, ["Patient"]);
        var observation = new ReindexParameterDefinition(canonical, "custom", "Observation", 18, 2, ["Observation"]);
        await writer.StartAsync("job", [patient, observation], CancellationToken.None);

        var ignored = await writer.CompleteAsync(
            "job",
            [
                new ReindexTargetCompletion(patient, true, 10, null),
                new ReindexTargetCompletion(observation, true, 20, null)
            ],
            TimeSpan.Zero,
            CancellationToken.None);

        ignored.ShouldBeEmpty();
        state.GetSearchParameter("Patient", "custom")!.Status.ShouldBe(SearchParameterStatus.Enabled);
        state.GetSearchParameter("Observation", "custom")!.Status.ShouldBe(SearchParameterStatus.Enabled);
    }
}
