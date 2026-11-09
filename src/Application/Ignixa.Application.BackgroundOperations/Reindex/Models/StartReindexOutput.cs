namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

/// <summary>
/// The start's outcome: whether the job was still open and moved to Running, and the targets whose
/// activation had moved on so they were not started.
/// </summary>
public sealed record StartReindexOutput(bool ShouldContinue, IReadOnlyList<string> IgnoredLifecycleEvents);
