using Ignixa.Application.Features.Conformance;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ignixa.Api.Tests;

/// <summary>
/// Real conformance leases on the system clock, for tests that only need a held or stale lease.
/// </summary>
internal static class TestConformanceLease
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    public static ConformanceLease Held()
    {
        var lease = NotHeld();
        lease.Renew(lease.CaptureStart());
        return lease;
    }

    public static ConformanceLease NotHeld() =>
        new(
            Options.Create(new ConformanceTransitionOptions
            {
                MaxStaleness = TimeSpan.FromHours(1),
                SyncIntervalSeconds = (int)RetryAfter.TotalSeconds,
            }),
            TimeProvider.System,
            NullLogger<ConformanceLease>.Instance);
}
