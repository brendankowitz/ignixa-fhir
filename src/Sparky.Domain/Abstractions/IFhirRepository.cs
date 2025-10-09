using Sparky.Domain.Models;

namespace Sparky.Domain.Abstractions;

/// <summary>
/// Core abstraction for FHIR resource storage and retrieval.
/// Provider-agnostic interface supports file, SQL, Cosmos, and in-memory implementations.
/// </summary>
public interface IFhirRepository
{
    /// <summary>
    /// Retrieves a resource by key. Returns null if not found.
    /// </summary>
    ValueTask<ResourceWrapper?> GetAsync(ResourceKey key, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a resource. Returns the persisted resource key with version.
    /// </summary>
    ValueTask<ResourceKey> CreateOrUpdateAsync(ResourceWrapper resource, CancellationToken ct = default);
}
