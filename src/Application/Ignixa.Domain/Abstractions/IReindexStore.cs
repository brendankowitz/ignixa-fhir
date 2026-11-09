using Ignixa.Domain.Models;
using System.Diagnostics.CodeAnalysis;

namespace Ignixa.Domain.Abstractions;

/// <summary>
/// SQL-backed operations required by reindex workers. Implementations must make index-only writes
/// without allocating a resource transaction or changing resource versioned data.
/// </summary>
public interface IReindexStore
{
    [SuppressMessage("Design", "CA1030:Use events where appropriate", Justification = "Raising the persisted SQL conformance barrier is a command, not an in-process notification.")]
    Task<BarrierCutoff> RaiseBarrierAsync(
        long targetEventId,
        CancellationToken cancellationToken);

    Task<IncompleteTransaction?> GetOldestIncompleteTransactionAsync(
        long cutoffTransactionId,
        CancellationToken cancellationToken);

    Task<SurrogateIdRangePage> GetSurrogateIdRangesAsync(
        string resourceType,
        long startAfterSurrogateId,
        long upperBoundSurrogateId,
        int targetRangeSize,
        int maxRanges,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads up to <paramref name="maxCount"/> current resources with surrogate IDs above
    /// <paramref name="afterSurrogateId"/> and at most <paramref name="endSurrogateId"/>, in surrogate-ID order.
    /// </summary>
    Task<IReadOnlyList<ReindexResource>> ReadRangeAsync(
        string resourceType,
        long afterSurrogateId,
        long endSurrogateId,
        int maxCount,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the batch's indexes, or writes nothing and returns <see cref="SearchIndexUpdateResult.Refused"/>
    /// when the tenant's conformance barrier is above the batch's definitions. The check and the write are atomic
    /// with respect to raising the barrier, so work a cancelled job left running cannot overwrite indexes a job
    /// with newer definitions wrote.
    /// </summary>
    Task<SearchIndexUpdateResult> UpdateSearchIndicesAsync(
        IReadOnlyList<ReindexResource> resources,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reports whether the tenant's search-parameter catalog has a row for <paramref name="storageCanonical"/>,
    /// the URI index rows are stored under (<see cref="ReindexParameterDefinition.StorageCanonical"/>). Catalog
    /// ids are allocated per tenant by URI and are unrelated to the conformance search-parameter id.
    /// </summary>
    Task<bool> HasSearchParameterAsync(
        string storageCanonical,
        CancellationToken cancellationToken);
}
