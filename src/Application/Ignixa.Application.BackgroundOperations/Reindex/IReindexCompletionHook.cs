using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public interface IReindexCompletionHook
{
    Task OnCompletedAsync(
        BackgroundJob<ReindexJobDefinition> job,
        CancellationToken cancellationToken);
}
