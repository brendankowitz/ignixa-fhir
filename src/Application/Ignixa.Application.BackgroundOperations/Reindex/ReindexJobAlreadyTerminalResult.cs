namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record ReindexJobAlreadyTerminalResult(
    string JobId,
    ReindexJobStatus Status) : CancelReindexResult;
