using Ignixa.Domain.Models;
using Ignixa.SourceNodeSerialization.SourceNodes.Models;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// Core abstraction for FHIR resource storage and retrieval.
/// Provider-agnostic interface supports file, SQL, Cosmos, and in-memory implementations.
///
/// Design Philosophy:
/// - Write path: Accept ResourceJsonNode (data layer can modify metadata before serialization)
/// - Read path: Return SearchEntryResult with raw bytes (zero-copy serialization to HTTP response)
/// </summary>
public interface IFhirRepository
{
    /// <summary>
    /// Retrieves a resource by key. Returns null if not found.
    /// Returns raw JSON bytes + metadata for zero-copy serialization.
    /// </summary>
    ValueTask<SearchEntryResult?> GetAsync(ResourceKey key, CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a resource. Returns the persisted resource key with version.
    /// Accepts ResourceJsonNode so data layer can set id/meta before final serialization.
    /// </summary>
    ValueTask<ResourceKey> CreateOrUpdateAsync(ResourceWrapper resource, CancellationToken ct = default);

    /// <summary>
    /// Allocates a new transaction ID for coordinated writes.
    /// Used by DeferredWriteCoordinator to get a transaction ID that will be used across multiple batches.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new transaction ID.</returns>
    ValueTask<TransactionId> GetNextTransactionIdAsync(CancellationToken ct = default);

    /// <summary>
    /// Batch write operation for bulk resource creation/updates.
    /// Atomically writes multiple resources in a single transaction.
    /// Returns the persisted resource keys with versions in the same order as input.
    /// Accepts ResourceJsonNode so data layer can set metadata before serialization.
    /// </summary>
    /// <param name="transactionId">Transaction ID to use for this batch (from GetNextTransactionIdAsync).</param>
    /// <param name="operations">List of resources to write (resourceType, resourceId, resource, searchIndexes).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of resource keys with versions.</returns>
    Task<IReadOnlyList<ResourceKey>> BatchWriteAsync(
        TransactionId transactionId,
        IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes)> operations,
        CancellationToken ct = default);

    /// <summary>
    /// Commits a transaction by renaming the lock file to committed file.
    /// Should be called after all batches are complete.
    /// </summary>
    /// <param name="transactionId">Transaction ID to commit.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask CommitTransactionAsync(TransactionId transactionId, CancellationToken ct = default);
}
