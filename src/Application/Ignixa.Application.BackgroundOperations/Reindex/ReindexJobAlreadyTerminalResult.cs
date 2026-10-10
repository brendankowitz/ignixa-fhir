namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record ReindexJobAlreadyTerminalResult(
    string JobId,
    string Status) : CancelReindexResult;
