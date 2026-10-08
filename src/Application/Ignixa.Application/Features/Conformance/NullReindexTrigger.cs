namespace Ignixa.Application.Features.Conformance;

public sealed class NullReindexTrigger : IReindexTrigger
{
    public Task<ReindexTriggerResult> RequestReindexAsync(
        string reason,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ReindexTriggerResult(
            null,
            false,
            "Automatic reindex is not configured; parameters remain Pending."));
}
