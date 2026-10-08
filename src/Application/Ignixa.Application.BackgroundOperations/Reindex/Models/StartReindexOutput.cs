namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record StartReindexOutput(IReadOnlyList<string> IgnoredLifecycleEvents)
{
    public long? TargetEventId { get; init; }
    public IReadOnlyList<string>? ResourceTypes { get; init; }
    public IReadOnlyList<ReindexTarget>? Targets { get; init; }
}
