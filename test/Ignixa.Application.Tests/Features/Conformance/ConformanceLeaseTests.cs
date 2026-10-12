using Ignixa.Application.Features.Conformance;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Conformance;

public class ConformanceLeaseTests
{
    [Fact]
    public void GivenSyncSucceedsAfterWork_WhenLeaseIsRenewed_ThenAgeIncludesTheSyncDuration()
    {
        var clock = new ManualTimeProvider();
        var lease = CreateLease(clock);
        var syncStart = lease.CaptureStart();

        clock.Advance(TimeSpan.FromSeconds(4));
        lease.Renew(syncStart);
        clock.Advance(TimeSpan.FromSeconds(7));

        lease.IsHeld.ShouldBeFalse();
        lease.Age.ShouldBe(TimeSpan.FromSeconds(11));
    }

    [Fact]
    public void GivenNoSuccessfulSync_WhenLeaseIsQueried_ThenItIsNotHeld()
    {
        var lease = CreateLease(new ManualTimeProvider());

        lease.IsHeld.ShouldBeFalse();
        lease.Age.ShouldBe(TimeSpan.MaxValue);
    }

    [Fact]
    public void GivenNewerRenewal_WhenAnOlderStartRenewsLater_ThenTheLeaseKeepsTheNewerStart()
    {
        var clock = new ManualTimeProvider();
        var lease = CreateLease(clock);
        var olderStart = lease.CaptureStart();
        clock.Advance(TimeSpan.FromSeconds(8));
        var newerStart = lease.CaptureStart();

        lease.Renew(newerStart);
        lease.Renew(olderStart);
        clock.Advance(TimeSpan.FromSeconds(5));

        lease.Age.ShouldBe(TimeSpan.FromSeconds(5));
        lease.IsHeld.ShouldBeTrue();
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, true, false)]
    public void GivenSearchOrigin_WhenTheLeaseGuardsTheSearch_ThenOnlyAStaleRequestSearchIsRejected(
        bool requestOriginated,
        bool isBackgroundTask,
        bool held,
        bool expectRejected)
    {
        var lease = CreateLease(new ManualTimeProvider());
        if (held)
        {
            lease.Renew(lease.CaptureStart());
        }

        var exception = Record.Exception(() => lease.EnsureRequestCanSearch(requestOriginated, isBackgroundTask));

        if (expectRejected)
        {
            exception.ShouldBeOfType<ConformanceStaleException>().RetryAfter.ShouldBe(TimeSpan.FromSeconds(7));
        }
        else
        {
            exception.ShouldBeNull();
        }
    }

    // Ten seconds of allowed staleness; a stale request is told to retry after one seven-second sync interval.
    private static ConformanceLease CreateLease(TimeProvider clock) =>
        new(
            Options.Create(new ConformanceTransitionOptions
            {
                MaxStaleness = TimeSpan.FromSeconds(10),
                SyncIntervalSeconds = 7,
            }),
            clock,
            NullLogger<ConformanceLease>.Instance);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan duration)
        {
            _timestamp += duration.Ticks;
            _utcNow += duration;
        }
    }
}
