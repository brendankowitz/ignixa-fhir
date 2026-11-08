namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <param name="IgnoredLifecycleEvents">Completions whose target the job no longer owned, so the projection would not apply them.</param>
/// <param name="UnplannedTargets">Owned targets with no completion, failed back to Pending with the fallback reason.</param>
public sealed record ReindexFinishResult(
    IReadOnlyList<string> IgnoredLifecycleEvents,
    IReadOnlyList<string> UnplannedTargets);
