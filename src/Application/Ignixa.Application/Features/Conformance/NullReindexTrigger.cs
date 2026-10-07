namespace Ignixa.Application.Features.Conformance;

public sealed class NullReindexTrigger : IReindexTrigger
{
    public Task RequestReindexAsync(string reason, CancellationToken cancellationToken) => Task.CompletedTask;
}
