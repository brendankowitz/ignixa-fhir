namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// A paired wall-clock and monotonic start time for a conformance synchronization.
/// </summary>
/// <remarks>A reference type so the lease can replace both values with one atomic exchange.</remarks>
public sealed record ConformanceLeaseStart(DateTimeOffset Utc, long Timestamp);
