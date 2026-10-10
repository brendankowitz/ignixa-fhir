namespace Ignixa.Application.BackgroundOperations.Reindex;

public abstract record CreateReindexJobResult;

public sealed record ReindexJobCreatedResult(string JobId) : CreateReindexJobResult;

public sealed record ActiveReindexJobResult(string ActiveJobId) : CreateReindexJobResult;

public sealed record InvalidReindexRequestResult(string ErrorMessage) : CreateReindexJobResult;

public sealed record NoReindexWorkResult(string ErrorMessage) : CreateReindexJobResult;
