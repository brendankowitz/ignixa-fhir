using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Domain.Models;

namespace Ignixa.Api.Services;

public sealed class ReindexCompletionHook(
    ConformanceRefreshPublisher refreshPublisher)
    : IReindexCompletionHook
{
    public Task OnCompletedAsync(
        BackgroundJob<ReindexJobDefinition> job,
        CancellationToken cancellationToken) =>
        refreshPublisher.RefreshUntilCurrentAsync(cancellationToken);
}
