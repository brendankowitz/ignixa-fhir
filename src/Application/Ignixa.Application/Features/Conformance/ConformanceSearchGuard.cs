namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Enforces the conformance lease before request-originated code evaluates search parameters.
/// </summary>
public static class ConformanceSearchGuard
{
    /// <summary>
    /// Throws when a request-originated search would use stale conformance definitions.
    /// </summary>
    public static void EnsureRequestCanSearch(
        IConformanceLease lease,
        bool requestOriginated,
        bool isBackgroundTask)
    {
        ArgumentNullException.ThrowIfNull(lease);

        if (!requestOriginated || isBackgroundTask || lease.IsHeld)
        {
            return;
        }

        throw new ConformanceStaleException(lease.RetryAfter);
    }
}
