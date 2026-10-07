namespace Ignixa.Application.Features.Conformance;

/// <summary>
/// Fallback for application compositions without the conformance event subsystem.
/// A stale retry remains stale and fails closed with 503.
/// </summary>
public sealed class NullConformanceDefinitionsSynchronizer : IConformanceDefinitionsSynchronizer
{
    public Task SynchronizeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
