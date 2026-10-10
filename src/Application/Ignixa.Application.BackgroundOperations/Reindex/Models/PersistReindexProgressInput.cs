namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

public sealed record PersistReindexProgressInput(string JobId, ReindexProgress Progress);
