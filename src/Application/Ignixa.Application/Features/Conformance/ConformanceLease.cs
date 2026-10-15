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
    private const long NeverRenewed = long.MinValue;

    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _maxStaleness;
    private readonly ILogger<ConformanceLease> _logger;
    private readonly ObservableGauge<double> _ageGauge;

    // The monotonic TimeProvider timestamp at which the newest successful synchronization began.
    private long _start = NeverRenewed;
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
            () => Interlocked.Read(ref _start) == NeverRenewed ? 0d : Age.TotalSeconds);
    }

    /// <summary>
    /// Gets whether this instance may serve request-originated searches.
    /// </summary>
    public bool IsHeld => IsHeldAt(Interlocked.Read(ref _start));

    /// <summary>
    /// Gets the elapsed time since the successful synchronization began.
    /// </summary>
    public TimeSpan Age => AgeOf(Interlocked.Read(ref _start));

    /// <summary>
    /// Gets the retry delay clients should use after a stale-lease response.
    /// </summary>
    public TimeSpan RetryAfter { get; }

    /// <summary>
    /// Captures the monotonic start of a synchronization or local activation, to pass to <see cref="Renew"/>.
    /// </summary>
    public long CaptureStart() => _timeProvider.GetTimestamp();

    /// <summary>
    /// Renews the lease after all work in the operation represented by <paramref name="start"/> has succeeded.
    /// A start older than the current one is ignored, so concurrent renewals never move the lease backwards.
    /// </summary>
    public void Renew(long start)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _start);
            if (current >= start ||
                Interlocked.CompareExchange(ref _start, start, current) == current)
            {
                break;
            }
        }

        Observe();
    }

    /// <summary>
    /// Throws when a request-originated search would use stale conformance definitions.
    /// </summary>
    /// <exception cref="ConformanceStaleException">The lease is not held.</exception>
    public void EnsureRequestCanSearch(bool requestOriginated, bool isBackgroundTask)
    {
        if (requestOriginated && !isBackgroundTask && !IsHeld)
        {
            throw new ConformanceStaleException(RetryAfter);
        }
    }

    /// <summary>
    /// Records a lease lost/regained transition since the previous observation. Called by the
    /// synchronization loop so an expiry is reported even when no request reads the lease.
    /// </summary>
    public void Observe()
    {
        var start = Interlocked.Read(ref _start);
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

    private bool IsHeldAt(long start) =>
        start != NeverRenewed && AgeOf(start) <= _maxStaleness;

    private TimeSpan AgeOf(long start) =>
        start == NeverRenewed ? TimeSpan.MaxValue : _timeProvider.GetElapsedTime(start);
}
