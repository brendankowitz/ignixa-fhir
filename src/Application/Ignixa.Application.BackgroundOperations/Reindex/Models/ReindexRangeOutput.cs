namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record ReindexRangeOutput(
    long ResourcesRead,
    long ResourcesReindexed,
    long Conflicts,
    IReadOnlyList<ReindexFailedResource> FailedResources)
{
    public long FailedResourceCount { get; init; } = FailedResources.Count;

    public IReadOnlyList<string> FailedResourceTypes { get; init; } = FailedResources
        .Select(failure => failure.ResourceType)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
