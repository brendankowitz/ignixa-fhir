namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record AwaitDrainInput(
    string JobId,
    int TenantId,
    long CutoffTransactionId,
    DateTime DrainStartedUtc,
    TimeSpan DrainWarningAfter,
    TimeSpan DrainElapsed = default,
    TimeSpan StaleJobTimeout = default);
