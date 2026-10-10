namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// The work a new reindex job would do: the active tenants it spans, the projection event it targets and the
/// Pending parameters it covers, resolved together under the activation lock.
/// </summary>
public sealed record ReindexTargetPlan(
    IReadOnlyList<int> TenantIds,
    long TargetEventId,
    ReindexTargetResolution Resolution);
