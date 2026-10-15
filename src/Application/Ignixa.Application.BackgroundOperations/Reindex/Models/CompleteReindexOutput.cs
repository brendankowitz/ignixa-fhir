namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record CompleteReindexOutput(
    bool Success,
    IReadOnlyList<string> IgnoredLifecycleEvents);
