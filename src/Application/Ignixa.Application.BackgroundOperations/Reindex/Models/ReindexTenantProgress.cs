namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

/// <summary>
/// What a reindex job reports for one tenant: its status, barrier cutoffs and cumulative counts. The
/// orchestration accumulates it, persists it inside <see cref="ReindexProgress"/> and hands it to completion.
/// Counts only grow; the failed-resource sample is capped at <see cref="FailedResourceSampleSize"/>.
/// </summary>
public sealed record ReindexTenantProgress(
    int TenantId,
    ReindexTenantStatus Status,
    long? CutoffTransactionId,
    long? CutoffSurrogateId,
    long ResourcesToReindex,
    long ResourcesRead,
    long ResourcesReindexed,
    long Conflicts,
    long FailedResourceCount,
    string? ErrorMessage)
{
    public const int FailedResourceSampleSize = 100;

    public IReadOnlyList<ReindexFailedResource> FailedResources { get; init; } = [];

    public IReadOnlyList<string> FailedResourceTypes { get; init; } = [];

    public static ReindexTenantProgress Create(int tenantId) =>
        new(tenantId, ReindexTenantStatus.BarrierDelay, null, null, 0, 0, 0, 0, 0, null);

    [Newtonsoft.Json.JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsCompleted => Status is ReindexTenantStatus.Completed or ReindexTenantStatus.Failed;

    /// <summary>Every affected type was reindexed and no resource failed; the parameters it covers can be enabled.</summary>
    [Newtonsoft.Json.JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Success =>
        Status == ReindexTenantStatus.Completed &&
        FailedResourceTypes.Count == 0 &&
        FailedResources.Count == 0 &&
        ErrorMessage is null;

    public ReindexTenantProgress Add(ReindexRangeOutput output) =>
        this with
        {
            ResourcesRead = ResourcesRead + output.ResourcesRead,
            ResourcesReindexed = ResourcesReindexed + output.ResourcesReindexed,
            Conflicts = Conflicts + output.Conflicts,
            FailedResourceCount = FailedResourceCount + output.FailedResourceCount,
            FailedResources = Sample(FailedResources.Concat(output.FailedResources)),
            FailedResourceTypes = Union(FailedResourceTypes, output.FailedResourceTypes)
        };

    /// <summary>Records a range activity that failed after its retries: the whole type counts as failed.</summary>
    public ReindexTenantProgress AddRangeFailure(string resourceType, string reason) =>
        this with
        {
            FailedResources = Sample(FailedResources.Append(new ReindexFailedResource(resourceType, string.Empty, reason))),
            FailedResourceTypes = Union(FailedResourceTypes, [resourceType])
        };

    /// <summary>Ends the tenant: every type it had not finished is failed along with those that already were.</summary>
    public ReindexTenantProgress Fail(string message, IEnumerable<string> unfinishedResourceTypes) =>
        this with
        {
            Status = ReindexTenantStatus.Failed,
            ErrorMessage = message,
            FailedResourceTypes = Union(unfinishedResourceTypes, FailedResourceTypes)
        };

    private static IReadOnlyList<ReindexFailedResource> Sample(IEnumerable<ReindexFailedResource> failures) =>
        failures.Take(FailedResourceSampleSize).ToArray();

    private static IReadOnlyList<string> Union(IEnumerable<string> first, IEnumerable<string> second) =>
        first.Concat(second).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
