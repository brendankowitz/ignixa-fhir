using Ignixa.Domain.Models;
using Ignixa.SourceNodeSerialization.SourceNodes;

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
    /// Creates or updates a resource. Returns the resource key, raw bytes, and metadata.
    /// Accepts ResourceJsonNode so data layer can set id/meta before final serialization.
    /// Returns UpdateResult with ResourceKey + raw bytes - only deserialize if needed.
    /// </summary>
    ValueTask<UpdateResult> CreateOrUpdateAsync(ResourceWrapper resource, CancellationToken ct = default);

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
    /// <param name="operations">List of resources to write (resourceType, resourceId, resource, searchIndexes, httpMethod, entryIndex).
    /// The entryIndex is the bundle entry index (0-based) used to calculate unique surrogate IDs.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of resource keys with versions.</returns>
    Task<IReadOnlyList<ResourceKey>> BatchWriteAsync(
        TransactionId transactionId,
        IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)> operations,
        CancellationToken ct = default);

    /// <summary>
    /// Commits a transaction by renaming the lock file to committed file.
    /// Should be called after all batches are complete.
    /// </summary>
    /// <param name="transactionId">Transaction ID to commit.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask CommitTransactionAsync(TransactionId transactionId, CancellationToken ct = default);

    /// <summary>
    /// Get transactions with lock files/records older than specified threshold.
    /// Used by TransactionWatcherService to detect stalled transactions that need to be committed.
    /// FileSystem: Scans for .lock.ndjson files with old modification times.
    /// SQL: Queries TransactionEntity table where IsCompleted = false AND HeartbeatDate is old.
    /// </summary>
    /// <param name="stallThreshold">Time threshold for considering a transaction stalled.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of transaction IDs that are stalled and need to be committed.</returns>
    ValueTask<IReadOnlyList<TransactionId>> GetStalledTransactionsAsync(
        TimeSpan stallThreshold,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves version history for a specific resource instance.
    /// Instance-level history: GET [base]/[type]/[id]/_history
    /// Returns all historical versions of the resource, filtered and paginated per parameters.
    /// IMPORTANT: Does NOT calculate total count - use separate count query if needed.
    /// Streams results incrementally for optimal memory usage.
    /// </summary>
    /// <param name="key">Resource key (resourceType and resourceId, versionId ignored).</param>
    /// <param name="parameters">Query parameters (count, offset, since, until, sort).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Async stream of search entry results (raw bytes for zero-copy serialization).</returns>
    IAsyncEnumerable<SearchEntryResult> GetResourceHistoryAsync(
        ResourceKey key,
        HistoryQueryParameters parameters,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves version history for all resources of a given type.
    /// Type-level history: GET [base]/[type]/_history
    /// Returns all historical versions of all resources of this type.
    /// IMPORTANT: Does NOT calculate total count - use separate count query if needed.
    /// Streams results incrementally for optimal memory usage.
    /// </summary>
    /// <param name="resourceType">FHIR resource type (e.g., "Patient").</param>
    /// <param name="tenantId">Tenant partition ID.</param>
    /// <param name="parameters">Query parameters (count, offset, since, until, sort).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Async stream of search entry results (raw bytes for zero-copy serialization).</returns>
    IAsyncEnumerable<SearchEntryResult> GetTypeHistoryAsync(
        string resourceType,
        int tenantId,
        HistoryQueryParameters parameters,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieves version history across all resource types in the system.
    /// System-level history: GET [base]/_history
    /// Returns all historical versions of all resources in the tenant partition.
    /// IMPORTANT: Does NOT calculate total count - use separate count query if needed.
    /// Streams results incrementally for optimal memory usage.
    /// </summary>
    /// <param name="tenantId">Tenant partition ID.</param>
    /// <param name="parameters">Query parameters (count, offset, since, until, sort).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Async stream of search entry results (raw bytes for zero-copy serialization).</returns>
    IAsyncEnumerable<SearchEntryResult> GetSystemHistoryAsync(
        int tenantId,
        HistoryQueryParameters parameters,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes a resource (soft delete per FHIR R4 specification).
    /// Creates a new version marked as deleted (tombstone) with incremented version number.
    /// DELETE [base]/[type]/[id]
    ///
    /// FHIR R4 Specification: Section 3.1.0.7.1 (delete interaction)
    /// - Logical deletion (soft delete), not physical deletion
    /// - Creates new version with IsDeleted=true, increments version number
    /// - Subsequent GET returns 410 Gone
    /// - Deletion appears in _history endpoint
    ///
    /// Behavior:
    /// - Success: Returns ResourceKey with new version (e.g., v4 if current was v3)
    /// - Not Found: Returns null if resource never existed
    /// - Idempotent: Returns existing deleted version if already deleted (no new version created)
    /// </summary>
    /// <param name="key">Resource key (resourceType and resourceId, versionId ignored).</param>
    /// <param name="request">HTTP request metadata (method, URL).</param>
    /// <param name="transactionId">Optional transaction ID for bundle operations.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// ResourceKey with new deleted version, or null if resource never existed.
    /// </returns>
    ValueTask<ResourceKey?> DeleteAsync(
        ResourceKey key,
        ResourceRequest request,
        TransactionId? transactionId = null,
        CancellationToken ct = default);
}
