namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

/// <summary>
/// One tenant's wait for a condition the orchestration polls: the drain of in-flight transactions, or the
/// range workers' definitions catching up with the target event. Polls back off from one second to thirty;
/// the orchestration bounds the whole wait by <c>Reindex:StaleJobTimeout</c>.
/// </summary>
public sealed record ReindexWait(DateTime StartedUtc, DateTime NextAttemptUtc, TimeSpan Interval)
{
    public static readonly TimeSpan FirstInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(30);

    /// <summary>A wait that starts now; its first poll is due at once.</summary>
    public static ReindexWait Begin(DateTime now) => new(now, now, FirstInterval);

    public bool IsDue(DateTime now) => now >= NextAttemptUtc;

    public TimeSpan Elapsed(DateTime now) => now - StartedUtc;

    /// <summary>Schedules the next poll after the current interval and doubles the interval up to the maximum.</summary>
    public ReindexWait Backoff(DateTime now) =>
        this with
        {
            NextAttemptUtc = now + Interval,
            Interval = Interval * 2 > MaxInterval ? MaxInterval : Interval * 2
        };
}
