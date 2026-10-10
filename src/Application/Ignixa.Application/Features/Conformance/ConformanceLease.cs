using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Process-local, monotonic staleness lease for serving searches.
/// </summary>
/// <remarks>
/// Thread-safe. <see cref="IsHeld"/> is a pure read; lease lost/regained transitions are logged and counted
/// only by <see cref="Renew"/> and <see cref="Observe"/>, so the synchronization loop owns that telemetry.
/// </remarks>
public sealed class ConformanceLease
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _maxStaleness;
    private readonly ILogger<ConformanceLease> _logger;
    private readonly ObservableGauge<double> _ageGauge;
    private ConformanceLeaseStart? _start;
    // 0 = never held, 1 = held, 2 = lost after being held.
    private int _observedState;

    public ConformanceLease(
        IOptions<ConformanceTransitionOptions> options,
        TimeProvider timeProvider,
        ILogger<ConformanceLease> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var configuredOptions = options.Value;
        _maxStaleness = configuredOptions.MaxStaleness;
        RetryAfter = TimeSpan.FromSeconds(configuredOptions.SyncIntervalSeconds);
        _ageGauge = ConformanceMetrics.CreateLeaseAgeGauge(
            () => Volatile.Read(ref _start) is null ? 0d : Age.TotalSeconds);
    }

    /// <summary>
    /// Gets whether this instance may serve request-originated searches.
    /// </summary>
    public bool IsHeld => IsHeldAt(Volatile.Read(ref _start));

    /// <summary>
    /// Gets the elapsed time since the successful synchronization began.
    /// </summary>
    public TimeSpan Age => AgeOf(Volatile.Read(ref _start));

    /// <summary>
    /// Gets the UTC start recorded for observability, or <see langword="null"/> before a successful synchronization.
    /// Lease enforcement uses the corresponding monotonic timestamp.
    /// </summary>
    public DateTimeOffset? LeaseStartUtc => Volatile.Read(ref _start)?.Utc;

    /// <summary>
    /// Gets the retry delay clients should use after a stale-lease response.
    /// </summary>
    public TimeSpan RetryAfter { get; }

    /// <summary>
    /// Captures the start of a synchronization or local activation.
    /// </summary>
    public ConformanceLeaseStart CaptureStart() =>
        new(_timeProvider.GetUtcNow(), _timeProvider.GetTimestamp());

    /// <summary>
    /// Renews the lease after all work in the operation represented by <paramref name="start"/> has succeeded.
    /// A start older than the current one is ignored, so concurrent renewals never move the lease backwards.
    /// </summary>
    public void Renew(ConformanceLeaseStart start)
    {
        ArgumentNullException.ThrowIfNull(start);

        while (true)
        {
            var current = Volatile.Read(ref _start);
            if (current is not null && current.Timestamp >= start.Timestamp)
            {
                break;
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _start, start, current), current))
            {
                break;
            }
        }

        Observe();
    }

    /// <summary>
    /// Records a lease lost/regained transition since the previous observation. Called by the
    /// synchronization loop so an expiry is reported even when no request reads the lease.
    /// </summary>
    public void Observe()
    {
        var start = Volatile.Read(ref _start);
        if (IsHeldAt(start))
        {
            if (Interlocked.Exchange(ref _observedState, 1) == 2)
            {
                _logger.LogInformation("Conformance staleness lease regained");
            }

            return;
        }

        if (Interlocked.CompareExchange(ref _observedState, 2, 1) == 1)
        {
            ConformanceMetrics.RecordLeaseLost();
            _logger.LogWarning(
                "Conformance staleness lease lost after {LeaseAge}; search requests will fail until synchronization succeeds",
                AgeOf(start));
        }
    }

    private bool IsHeldAt(ConformanceLeaseStart? start) =>
        start is not null && AgeOf(start) <= _maxStaleness;

    private TimeSpan AgeOf(ConformanceLeaseStart? start) =>
        start is null ? TimeSpan.MaxValue : _timeProvider.GetElapsedTime(start.Timestamp);
}
