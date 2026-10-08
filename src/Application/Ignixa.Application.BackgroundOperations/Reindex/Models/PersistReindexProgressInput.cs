namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record PersistReindexProgressInput(
    string JobId,
    long Sequence,
    string Phase,
    IReadOnlyList<ReindexTenantState> Tenants);
