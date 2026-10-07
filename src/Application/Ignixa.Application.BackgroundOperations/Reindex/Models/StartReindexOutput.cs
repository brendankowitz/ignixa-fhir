namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record StartReindexOutput(IReadOnlyList<string> IgnoredLifecycleEvents);
