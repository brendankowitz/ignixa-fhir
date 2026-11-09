namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record RaiseBarrierInput(string JobId, int TenantId, long TargetEventId);
