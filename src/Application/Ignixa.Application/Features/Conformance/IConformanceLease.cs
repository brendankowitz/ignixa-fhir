namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Tracks whether this instance has recently synchronized its conformance state.
/// </summary>
public interface IConformanceLease
{
    /// <summary>
    /// Gets whether this instance may serve request-originated searches.
    /// </summary>
    bool IsHeld { get; }

    /// <summary>
    /// Gets the elapsed time since the successful synchronization began.
    /// </summary>
    TimeSpan Age { get; }

    /// <summary>
    /// Gets the UTC start recorded for observability, or <see langword="null"/> before a successful synchronization.
    /// Lease enforcement uses the corresponding monotonic timestamp.
    /// </summary>
    DateTimeOffset? LeaseStartUtc { get; }

    /// <summary>
    /// Gets the retry delay clients should use after a stale-lease response.
    /// </summary>
    TimeSpan RetryAfter { get; }

    /// <summary>
    /// Captures the start of a synchronization or local activation.
    /// </summary>
    ConformanceLeaseStart CaptureStart();

    /// <summary>
    /// Renews the lease after all work in the operation represented by <paramref name="start"/> has succeeded.
    /// </summary>
    void Renew(ConformanceLeaseStart start);
}
