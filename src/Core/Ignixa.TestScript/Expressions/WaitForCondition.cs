namespace Ignixa.TestScript.Expressions;

/// <summary>
/// Parsed form of the <c>http://ignixa.io/testscript/waitFor</c> extension: an operation carrying
/// this is retried — the same request, sent again — while its response's HTTP status equals
/// <paramref name="PollingStatusCode"/>, up to <paramref name="MaxAttempts"/> times, sleeping
/// <paramref name="IntervalMs"/> between attempts.
/// </summary>
public sealed record WaitForCondition(int PollingStatusCode, int MaxAttempts, int IntervalMs);
