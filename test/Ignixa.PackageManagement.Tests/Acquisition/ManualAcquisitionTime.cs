namespace Ignixa.PackageManagement.Tests.Acquisition;

internal sealed class ManualAcquisitionTime : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private long _ticks;
    internal Action? BeforeTimestamp { get; set; }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp()
    {
        BeforeTimestamp?.Invoke();
        lock (_gate)
        {
            return _ticks;
        }
    }

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + TimeSpan.FromTicks(GetTimestamp());

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_gate)
        {
            _timers.Add(timer);
        }
        return timer;
    }

    internal void Advance(TimeSpan duration)
    {
        List<ManualTimer> ready;
        lock (_gate)
        {
            _ticks += duration.Ticks;
            ready = _timers.Where(t => t.Due <= _ticks).ToList();
            foreach (ManualTimer timer in ready)
            {
                timer.Due = long.MaxValue;
            }
        }
        foreach (ManualTimer timer in ready)
        {
            timer.Invoke();
        }
    }

    internal async Task<TimeSpan> WaitForDelayAsync(TimeSpan maximum)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            lock (_gate)
            {
                long next = _timers.Where(t => t.Duration <= maximum).Select(t => t.Due).DefaultIfEmpty(long.MaxValue).Min();
                long remaining = next - _ticks;
                if (next != long.MaxValue && remaining <= maximum.Ticks)
                {
                    return TimeSpan.FromTicks(Math.Max(0, remaining));
                }
            }
            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class ManualTimer(ManualAcquisitionTime owner, TimerCallback callback, object? state) : ITimer
    {
        internal long Due { get; set; } = long.MaxValue;
        internal TimeSpan Duration { get; private set; }
        internal void Invoke() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : owner._ticks + dueTime.Ticks;
                Duration = dueTime;
            }
            return true;
        }
        public void Dispose()
        {
            lock (owner._gate)
            {
                Due = long.MaxValue;
            }
        }
        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
