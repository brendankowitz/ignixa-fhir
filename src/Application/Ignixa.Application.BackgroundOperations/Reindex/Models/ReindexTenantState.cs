namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record ReindexTenantState(
    int TenantId,
    string Phase,
    long CutoffTransactionId,
    long CutoffSurrogateId,
    DateTime DrainStartedUtc,
    int ResourceTypeIndex,
    long PlannerCursor,
    IReadOnlyList<ReindexRange> PendingRanges,
    long? NextPlannerCursor,
    long ResourcesToReindex,
    long ResourcesRead,
    long ResourcesReindexed,
    long Conflicts,
    long FailedResourceCount,
    IReadOnlyList<ReindexFailedResource> FailedResources,
    IReadOnlyList<string> FailedResourceTypes,
    string? ErrorMessage)
{
    public long? VisibleWatermark { get; init; }

    public static ReindexTenantState Create(int tenantId) =>
        new(
            tenantId,
            "Barrier",
            -1,
            -1,
            default,
            0,
            -1,
            [],
            null,
            0,
            0,
            0,
            0,
            0,
            [],
            [],
            null);

    public bool IsCompleted => Phase == "Completed";

    public ReindexTenantOutput ToOutput() =>
        new(
            TenantId,
            FailedResourceTypes.Count == 0 && FailedResources.Count == 0 && ErrorMessage is null,
            CutoffTransactionId,
            CutoffSurrogateId,
            ResourcesToReindex,
            ResourcesReindexed,
            Conflicts,
            FailedResourceCount,
            FailedResources,
            ErrorMessage)
        {
            FailedResourceTypes = FailedResourceTypes
        };
}
