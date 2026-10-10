namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record NoReindexWorkResult(string ErrorMessage) : CreateReindexJobResult;
