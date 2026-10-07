using Ignixa.Application.Features.Conformance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Conformance;

public class ConformanceLeaseTests
{
    [Fact]
    public void GivenSyncSucceedsAfterWork_WhenLeaseIsRenewed_ThenAgeIncludesTheSyncDuration()
    {
        var clock = new ManualTimeProvider();
        var lease = new ConformanceLease(
            Options.Create(new ConformanceTransitionOptions { MaxStaleness = TimeSpan.FromSeconds(10) }),
            clock,
            NullLogger<ConformanceLease>.Instance);
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
        var lease = new ConformanceLease(
            Options.Create(new ConformanceTransitionOptions { MaxStaleness = TimeSpan.FromSeconds(10) }),
            new ManualTimeProvider(),
            NullLogger<ConformanceLease>.Instance);

        lease.IsHeld.ShouldBeFalse();
        lease.Age.ShouldBe(TimeSpan.MaxValue);
        lease.LeaseStartUtc.ShouldBeNull();
    }

    [Fact]
    public void GivenSyncExceedsMaxStaleness_WhenLeaseIsRenewed_ThenTheLossIsObservedWithoutARequest()
    {
        var clock = new ManualTimeProvider();
        var logger = Substitute.For<ILogger<ConformanceLease>>();
        var lease = new ConformanceLease(
            Options.Create(new ConformanceTransitionOptions { MaxStaleness = TimeSpan.FromSeconds(10) }),
            clock,
            logger);
        lease.Renew(lease.CaptureStart());
        var syncStart = lease.CaptureStart();

        clock.Advance(TimeSpan.FromSeconds(11));
        lease.Renew(syncStart);

        logger.ReceivedCalls()
            .Count(call => Equals(call.GetArguments()[0], LogLevel.Warning))
            .ShouldBe(1);
    }

    [Fact]
    public void GivenStaleLeaseForBackgroundWork_WhenSearchGuardRuns_ThenItDoesNotBlockTheWork()
    {
        var lease = Substitute.For<IConformanceLease>();
        lease.IsHeld.Returns(false);

        Should.NotThrow(() => ConformanceSearchGuard.EnsureRequestCanSearch(
            lease,
            requestOriginated: true,
            isBackgroundTask: true));
    }

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
