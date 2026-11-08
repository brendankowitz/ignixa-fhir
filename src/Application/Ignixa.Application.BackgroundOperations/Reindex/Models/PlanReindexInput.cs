namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record PlanReindexInput(
    string JobId,
    int TenantId,
    string ResourceType,
    long? StartAfterSurrogateId,
    long CutoffSurrogateId,
    int TargetRangeSize,
    int MaxRanges);
