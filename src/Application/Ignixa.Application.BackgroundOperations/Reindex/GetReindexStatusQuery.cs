using Medino;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record GetReindexStatusQuery(string JobId) : IRequest<ReindexStatusResult?>;
