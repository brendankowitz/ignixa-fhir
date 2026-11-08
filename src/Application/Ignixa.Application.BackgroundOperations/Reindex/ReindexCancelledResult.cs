namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record ReindexCancelledResult(string JobId) : CancelReindexResult;
