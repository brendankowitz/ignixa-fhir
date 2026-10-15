using Medino;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed record GetReindexJobsQuery(int TerminalJobLimit = 10)
    : IRequest<IReadOnlyList<ReindexStatusResult>>;
