using Ignixa.Domain.Constants;

namespace Ignixa.Application.Features.Conformance;

public sealed class ConformanceTransitionOptions
{
    private static readonly TimeSpan DefaultClockSkewAllowance = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SafetyMarginRoundingInterval = TimeSpan.FromSeconds(30);

    public const string SectionName = "Conformance";

    /// <summary>
    /// Covers the longest default SQL read: four 30-second command attempts, the three exponential
    /// retry delays, and 15 seconds of clock skew, rounded up to the next 30-second boundary.
    /// </summary>
    public static readonly TimeSpan DefaultTransitionSafetyMargin =
        RoundUp(
            SqlExecutionPolicyDefaults.MaximumExecutionBudget + DefaultClockSkewAllowance,
            SafetyMarginRoundingInterval);

    public int SyncIntervalSeconds { get; set; } = 30;
    public TimeSpan MaxStaleness { get; set; }
    public TimeSpan TransitionGrace { get; set; }
    public TimeSpan TransitionSafetyMargin { get; set; } = DefaultTransitionSafetyMargin;

    private static TimeSpan RoundUp(TimeSpan duration, TimeSpan interval)
    {
        var remainder = duration.Ticks % interval.Ticks;
        return remainder == 0
            ? duration
            : TimeSpan.FromTicks(duration.Ticks + interval.Ticks - remainder);
    }
}
