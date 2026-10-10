using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

/// <summary>
/// One tenant's orchestration state: the progress it reports plus the planner position, pending ranges and
/// the wait it is in. Carried through <c>ContinueAsNew</c>, so everything here round-trips as JSON.
/// </summary>
public sealed record ReindexTenantState(ReindexTenantProgress Progress)
{
    public long? VisibleWatermark { get; init; }

    public ReindexWait? Wait { get; init; }

    public int ResourceTypeIndex { get; init; }

    /// <summary>The surrogate id the next planning page starts after; null plans a type from its start.</summary>
    public long? PlannerCursor { get; init; }

    public long? NextPlannerCursor { get; init; }

    public IReadOnlyList<ReindexRange> PendingRanges { get; init; } = [];

    public static ReindexTenantState Create(int tenantId) => new(ReindexTenantProgress.Create(tenantId));

    [Newtonsoft.Json.JsonIgnore]
    public int TenantId => Progress.TenantId;

    [Newtonsoft.Json.JsonIgnore]
    public ReindexTenantStatus Status => Progress.Status;

    [Newtonsoft.Json.JsonIgnore]
    public bool IsCompleted => Progress.IsCompleted;

    /// <summary>The cutoffs the barrier returned; a tenant past the barrier always has them.</summary>
    public BarrierCutoff RequireCutoff() =>
        Progress is { CutoffTransactionId: { } transactionId, CutoffSurrogateId: { } surrogateId }
            ? new BarrierCutoff(transactionId, surrogateId)
            : throw new InvalidOperationException($"Tenant {TenantId} is {Status} without a barrier cutoff.");
}
