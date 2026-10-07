namespace Ignixa.Application.Features.Conformance;

public interface IConformanceDefinitionsSynchronizer
{
    Task SynchronizeAsync(CancellationToken cancellationToken);
}
