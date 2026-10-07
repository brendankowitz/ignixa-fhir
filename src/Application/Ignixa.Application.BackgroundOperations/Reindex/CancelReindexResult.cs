namespace Ignixa.Application.BackgroundOperations.Reindex;

public abstract record CancelReindexResult;

public sealed record ReindexCancelledResult(string JobId) : CancelReindexResult;

public sealed record ReindexJobNotFoundResult(string JobId) : CancelReindexResult;

public sealed record ReindexJobAlreadyTerminalResult(
    string JobId,
    string Status) : CancelReindexResult;
