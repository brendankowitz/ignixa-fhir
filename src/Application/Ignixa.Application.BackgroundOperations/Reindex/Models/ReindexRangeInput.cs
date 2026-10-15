namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record ReindexRangeInput(
    string JobId,
    int TenantId,
    string ResourceType,
    long StartSurrogateId,
    long EndSurrogateId,
    long TargetEventId,
    int MaximumNumberOfResourcesPerWrite,
    int QueryDelayIntervalInMilliseconds);
