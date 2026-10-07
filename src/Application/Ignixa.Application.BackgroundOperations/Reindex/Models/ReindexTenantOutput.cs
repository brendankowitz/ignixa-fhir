namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record ReindexTenantOutput(
    int TenantId,
    bool Success,
    long CutoffTransactionId,
    long CutoffSurrogateId,
    long ResourcesToReindex,
    long ResourcesReindexed,
    long Conflicts,
    IReadOnlyList<ReindexFailedResource> FailedResources,
    string? ErrorMessage)
{
    public IReadOnlyList<string> FailedResourceTypes { get; init; } = [];
}
