namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record ReindexRangeOutput(
    long ResourcesRead,
    long ResourcesReindexed,
    long Conflicts,
    IReadOnlyList<ReindexFailedResource> FailedResources);
