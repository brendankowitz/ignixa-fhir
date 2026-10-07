namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// A paired wall-clock and monotonic start time for a conformance synchronization.
/// </summary>
public readonly record struct ConformanceLeaseStart(DateTimeOffset Utc, long Timestamp);
