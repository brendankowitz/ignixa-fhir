using Ignixa.Application.Features.Conformance;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Conformance;

/// <summary>Spec §4.5 and M-L4: startup requires TransitionGrace ≥ MaxStaleness + TransitionSafetyMargin and BarrierDelay ≥ MaxStaleness.</summary>
public class ConformanceTransitionOptionsValidatorTests
{
    private static readonly TimeSpan Ten = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Thirty = TimeSpan.FromSeconds(30);

    public static TheoryData<string, int, TimeSpan, TimeSpan, TimeSpan, TimeSpan, bool> Cases => new()
    {
        { "grace equals staleness plus margin", 30, Ten, TimeSpan.FromSeconds(40), Thirty, Ten, true },
        { "grace one second short of staleness plus margin", 30, Ten, TimeSpan.FromSeconds(39), Thirty, Ten, false },
        { "grace below staleness", 30, Ten, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(1), Ten, false },
        { "barrier delay equals staleness", 30, Ten, TimeSpan.FromSeconds(40), Thirty, Ten, true },
        { "barrier delay below staleness", 30, Ten, TimeSpan.FromSeconds(40), Thirty, TimeSpan.FromSeconds(9), false },
        { "zero sync interval", 0, Ten, TimeSpan.FromSeconds(40), Thirty, Ten, false },
        { "negative sync interval", -1, Ten, TimeSpan.FromSeconds(40), Thirty, Ten, false },
        { "zero staleness", 30, TimeSpan.Zero, Thirty, Thirty, TimeSpan.Zero, false },
        { "negative staleness and barrier delay", 30, TimeSpan.FromSeconds(-2), Thirty, Thirty, TimeSpan.FromSeconds(-1), false },
        { "zero safety margin", 30, Ten, TimeSpan.FromSeconds(11), TimeSpan.Zero, Ten, false },
        { "negative safety margin", 30, Ten, TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(-1), Ten, false },
        { "durations at the maximum", 30, TimeSpan.MaxValue, TimeSpan.MaxValue, TimeSpan.FromSeconds(1), TimeSpan.MaxValue, false },
        { "negative staleness with maximum grace", 30, TimeSpan.MinValue, TimeSpan.MaxValue, Thirty, TimeSpan.MaxValue, false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void GivenTransitionTimings_WhenValidated_ThenOnlyTimingsThatCoverStalenessSucceed(
        string scenario,
        int syncIntervalSeconds,
        TimeSpan maxStaleness,
        TimeSpan transitionGrace,
        TimeSpan transitionSafetyMargin,
        TimeSpan barrierDelay,
        bool expectedValid)
    {
        var options = new ConformanceTransitionOptions
        {
            SyncIntervalSeconds = syncIntervalSeconds,
            MaxStaleness = maxStaleness,
            TransitionGrace = transitionGrace,
            TransitionSafetyMargin = transitionSafetyMargin,
        };

        var result = ConformanceTransitionOptionsValidator.Validate(options, new ReindexOptions { BarrierDelay = barrierDelay });

        result.Succeeded.ShouldBe(expectedValid, scenario);
    }
}
