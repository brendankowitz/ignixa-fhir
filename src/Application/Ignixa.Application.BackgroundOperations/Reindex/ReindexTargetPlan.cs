using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// The work a new reindex job would do: the active tenants it spans, the projection event it targets and the
/// Pending parameters it covers, resolved together under the activation lock.
/// </summary>
public sealed record ReindexTargetPlan(
    IReadOnlyList<int> TenantIds,
    long TargetEventId,
    IReadOnlyList<ReindexParameterDefinition> Targets)
{
    /// <summary>Every type a target affects, once each, in ordinal order.</summary>
    public IReadOnlyList<string> ResourceTypes { get; } = Targets.SelectMany(target => target.AffectedResourceTypes)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.Ordinal)
        .ToArray();

    public bool HasWork => Targets.Count > 0;
}
