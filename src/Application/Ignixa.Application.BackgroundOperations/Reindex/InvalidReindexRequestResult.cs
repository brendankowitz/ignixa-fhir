namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record InvalidReindexRequestResult(string ErrorMessage) : CreateReindexJobResult;
