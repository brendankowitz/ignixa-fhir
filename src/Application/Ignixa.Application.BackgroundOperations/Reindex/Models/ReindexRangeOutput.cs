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

    /// <summary>
    /// Set when the worker's definitions were still at this event, behind the job's target, so no range work
    /// ran. The orchestration keeps the range and retries after a wait; no exception crosses the activity
    /// boundary, where DurableTask could not rebuild its type.
    /// </summary>
    public long? StaleDefinitionsEventId { get; init; }

    [Newtonsoft.Json.JsonIgnore]
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDefinitionsNotReady => StaleDefinitionsEventId.HasValue;

    public static ReindexRangeOutput DefinitionsNotReady(long definitionsEventId) =>
        new(0, 0, 0, []) { StaleDefinitionsEventId = definitionsEventId };
}
