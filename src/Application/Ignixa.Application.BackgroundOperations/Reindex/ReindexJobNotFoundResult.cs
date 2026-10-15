namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record ReindexJobNotFoundResult(string JobId) : CancelReindexResult;
