using Ignixa.Domain.Models;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Storage capable of applying a validated transaction's complete write set in one atomic core merge.
/// Each resource carries its expected current version, or "0" when it must not exist.
/// No resource or index may change if any write conflicts.
/// </summary>
public interface IAtomicFhirRepository
{
    Task WriteTransactionAsync(IReadOnlyList<ResourceWrapper> resources, CancellationToken cancellationToken);
}
