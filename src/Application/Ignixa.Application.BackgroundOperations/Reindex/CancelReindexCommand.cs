using Medino;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record CancelReindexCommand(
    string JobId,
    string Reason = "Cancelled by user") : IRequest<CancelReindexResult>;
