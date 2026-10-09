using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Process-local, monotonic staleness lease for serving searches.
/// </summary>
public sealed class ConformanceLease : IConformanceLease
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _maxStaleness;
    private readonly TimeSpan _retryAfter;
    private readonly ILogger<ConformanceLease> _logger;
    private readonly ObservableGauge<double> _ageGauge;
    private long _leaseStartTimestamp = -1;
    private long _leaseStartUtcTicks;
    // 0 = never held, 1 = held, 2 = lost after being held.
    private int _leaseState;

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
        _retryAfter = TimeSpan.FromSeconds(configuredOptions.SyncIntervalSeconds);
        _ageGauge = ConformanceMetrics.CreateLeaseAgeGauge(
            () => IsStarted ? Age.TotalSeconds : 0d);
    }

    /// <inheritdoc />
    public bool IsHeld
    {
        get
        {
            var held = IsStarted && Age <= _maxStaleness;
            ObserveTransition(held);
            return held;
        }
    }

    /// <inheritdoc />
    public TimeSpan Age
    {
        get
        {
            var startTimestamp = Interlocked.Read(ref _leaseStartTimestamp);
            return startTimestamp < 0
                ? TimeSpan.MaxValue
                : _timeProvider.GetElapsedTime(startTimestamp);
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? LeaseStartUtc
    {
        get
        {
            var startTimestamp = Interlocked.Read(ref _leaseStartTimestamp);
            return startTimestamp < 0
                ? null
                : new DateTimeOffset(Interlocked.Read(ref _leaseStartUtcTicks), TimeSpan.Zero);
        }
    }

    /// <inheritdoc />
    public TimeSpan RetryAfter => _retryAfter;

    /// <inheritdoc />
    public ConformanceLeaseStart CaptureStart() =>
        new(_timeProvider.GetUtcNow(), _timeProvider.GetTimestamp());

    /// <inheritdoc />
    public void Renew(ConformanceLeaseStart start)
    {
        Interlocked.Exchange(ref _leaseStartUtcTicks, start.Utc.UtcTicks);
        Interlocked.Exchange(ref _leaseStartTimestamp, start.Timestamp);
        ObserveTransition(_timeProvider.GetElapsedTime(start.Timestamp) <= _maxStaleness);
    }

    private bool IsStarted => Interlocked.Read(ref _leaseStartTimestamp) >= 0;

    private void ObserveTransition(bool held)
    {
        if (held)
        {
            var previous = Interlocked.Exchange(ref _leaseState, 1);
            if (previous == 2)
            {
                _logger.LogInformation("Conformance staleness lease regained");
            }
            return;
        }

        if (Interlocked.CompareExchange(ref _leaseState, 2, 1) == 1)
        {
            ConformanceMetrics.RecordLeaseLost();
            _logger.LogWarning(
                "Conformance staleness lease lost after {LeaseAge}; search requests will fail until synchronization succeeds",
                Age);
        }
    }
}
