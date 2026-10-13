using Ignixa.Application.Features.Conformance;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Conformance;

/// <summary>Spec §4.5: ten seconds of allowed staleness; a stale request retries after one seven-second sync interval.</summary>
public class ConformanceLeaseTests
{
    private readonly FakeTimeProvider _clock = new();
    private readonly ConformanceLease _lease;

    public ConformanceLeaseTests() =>
        _lease = TestConformanceLease.NotHeld(_clock, TimeSpan.FromSeconds(10), syncIntervalSeconds: 7);

    [Fact]
    public void GivenTwoSyncsThatSucceedAfterWorkInReverseOrder_WhenTheLeaseIsRenewed_ThenItIsHeldOnlyWithinMaxStalenessOfTheNewerStart()
    {
        _lease.IsHeld.ShouldBeFalse("no sync has succeeded yet");
        _lease.Age.ShouldBe(TimeSpan.MaxValue);
        var olderStart = _lease.CaptureStart();
        _clock.Advance(TimeSpan.FromSeconds(4));
        var newerStart = _lease.CaptureStart();
        _clock.Advance(TimeSpan.FromSeconds(4));

        _lease.Renew(newerStart);
        _lease.Renew(olderStart);

        _clock.Advance(TimeSpan.FromSeconds(4));
        (_lease.IsHeld, _lease.Age).ShouldBe((true, TimeSpan.FromSeconds(8)));
        _clock.Advance(TimeSpan.FromSeconds(3));
        (_lease.IsHeld, _lease.Age).ShouldBe((false, TimeSpan.FromSeconds(11)));
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

        (Type?, TimeSpan?) expected = expectRejected ? (typeof(ConformanceStaleException), TimeSpan.FromSeconds(7)) : (null, null);
        (exception?.GetType(), (exception as ConformanceStaleException)?.RetryAfter).ShouldBe(expected);
    }
}
