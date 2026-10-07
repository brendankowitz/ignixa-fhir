namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record StartReindexInput(
    string JobId,
    long TargetEventId,
    IReadOnlyList<ReindexTarget> Targets);
