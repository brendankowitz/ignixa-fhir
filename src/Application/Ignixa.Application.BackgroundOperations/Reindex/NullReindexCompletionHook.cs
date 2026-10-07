using Ignixa.Domain.Models;

namespace Ignixa.Application.BackgroundOperations.Reindex;

public sealed class NullReindexCompletionHook : IReindexCompletionHook
{
    public Task OnCompletedAsync(
        BackgroundJob<ReindexJobDefinition> job,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
