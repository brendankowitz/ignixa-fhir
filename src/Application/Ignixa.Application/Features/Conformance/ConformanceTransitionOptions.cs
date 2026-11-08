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

    /// <summary>
    /// Gets or sets how often each instance polls the shared conformance event stream.
    /// </summary>
    public int SyncIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the maximum age of the last successful conformance sync start while search remains available.
    /// </summary>
    public TimeSpan MaxStaleness { get; set; }

    /// <summary>
    /// Gets or sets the delay between hiding a definition and committing its extraction change.
    /// </summary>
    public TimeSpan TransitionGrace { get; set; }

    /// <summary>
    /// Gets or sets the execution allowance added above <see cref="MaxStaleness"/> before a transition may commit.
    /// </summary>
    public TimeSpan TransitionSafetyMargin { get; set; } = DefaultTransitionSafetyMargin;

    private static TimeSpan RoundUp(TimeSpan duration, TimeSpan interval)
    {
        var remainder = duration.Ticks % interval.Ticks;
        return remainder == 0
            ? duration
            : TimeSpan.FromTicks(duration.Ticks + interval.Ticks - remainder);
    }
}
