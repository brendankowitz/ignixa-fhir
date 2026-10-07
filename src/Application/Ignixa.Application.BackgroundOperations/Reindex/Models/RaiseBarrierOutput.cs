namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record RaiseBarrierOutput(
    int TenantId,
    long CutoffTransactionId,
    long CutoffSurrogateId);
