namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record PlanReindexOutput(
    IReadOnlyList<ReindexRange> Ranges,
    long? NextStartAfter);
