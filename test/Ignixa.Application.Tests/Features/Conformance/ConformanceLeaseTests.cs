using Ignixa.Application.Features.Conformance;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Conformance;

/// <summary>Spec §4.5: ten seconds of allowed staleness; a stale request retries after one seven-second sync interval.</summary>
public class ConformanceLeaseTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly ConformanceLease _lease;

    public ConformanceLeaseTests() =>
        _lease = TestConformanceLease.NotHeld(_clock, TimeSpan.FromSeconds(10), syncIntervalSeconds: 7);

    [Fact]
    public void GivenNoSyncThenOneThatSucceedsAfterWork_WhenLeaseIsRenewed_ThenItIsHeldOnlyWithinMaxStalenessOfTheSyncStart()
    {
        _lease.IsHeld.ShouldBeFalse("no sync has succeeded yet");
        _lease.Age.ShouldBe(TimeSpan.MaxValue);
        var syncStart = _lease.CaptureStart();

        _clock.Advance(TimeSpan.FromSeconds(4));
        _lease.Renew(syncStart);
        _lease.IsHeld.ShouldBeTrue();
        _clock.Advance(TimeSpan.FromSeconds(7));

        _lease.IsHeld.ShouldBeFalse();
        _lease.Age.ShouldBe(TimeSpan.FromSeconds(11));
    }

    [Fact]
    public void GivenNewerRenewal_WhenAnOlderStartRenewsLater_ThenTheLeaseKeepsTheNewerStart()
    {
        var olderStart = _lease.CaptureStart();
        _clock.Advance(TimeSpan.FromSeconds(8));
        var newerStart = _lease.CaptureStart();

        _lease.Renew(newerStart);
        _lease.Renew(olderStart);
        _clock.Advance(TimeSpan.FromSeconds(5));

        _lease.Age.ShouldBe(TimeSpan.FromSeconds(5));
        _lease.IsHeld.ShouldBeTrue();
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
        if (held)
        {
            _lease.Renew(_lease.CaptureStart());
        }

        var exception = Record.Exception(() => _lease.EnsureRequestCanSearch(requestOriginated, isBackgroundTask));

        if (expectRejected)
        {
            exception.ShouldBeOfType<ConformanceStaleException>().RetryAfter.ShouldBe(TimeSpan.FromSeconds(7));
        }
        else
        {
            exception.ShouldBeNull();
        }
    }
}
