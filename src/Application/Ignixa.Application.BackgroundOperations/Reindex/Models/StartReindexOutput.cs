namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record StartReindexOutput(IReadOnlyList<string> IgnoredLifecycleEvents)
{
    public bool ShouldContinue { get; init; } = true;
    public long? TargetEventId { get; init; }
    public IReadOnlyList<string>? ResourceTypes { get; init; }
    public IReadOnlyList<ReindexTarget>? Targets { get; init; }
}
