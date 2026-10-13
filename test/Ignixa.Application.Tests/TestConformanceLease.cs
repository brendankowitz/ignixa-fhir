using Ignixa.Application.Features.Conformance;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ignixa.Application.Tests;

/// <summary>
/// Real conformance leases, by default on the system clock with an hour of allowed staleness and a 30 s sync interval.
/// </summary>
internal static class TestConformanceLease
{
    public static ConformanceLease Held()
    {
        var lease = NotHeld();
        lease.Renew(lease.CaptureStart());
        return lease;
    }

    public static ConformanceLease NotHeld(TimeProvider? clock = null, TimeSpan? maxStaleness = null, int syncIntervalSeconds = 30) =>
        new(
            Options.Create(new ConformanceTransitionOptions
            {
                MaxStaleness = maxStaleness ?? TimeSpan.FromHours(1),
                SyncIntervalSeconds = syncIntervalSeconds,
            }),
            clock ?? TimeProvider.System,
            NullLogger<ConformanceLease>.Instance);
}
