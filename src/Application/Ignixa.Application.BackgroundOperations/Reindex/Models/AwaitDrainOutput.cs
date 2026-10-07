namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record AwaitDrainOutput(
    int TenantId,
    bool IsDrained,
    long VisibleWatermark);
