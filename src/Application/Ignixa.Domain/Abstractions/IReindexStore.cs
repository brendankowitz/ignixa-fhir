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
    Task<(long TransactionId, long SurrogateId)> RaiseBarrierAsync(
        long targetEventId,
        CancellationToken cancellationToken);

    Task<long> GetVisibleWatermarkAsync(CancellationToken cancellationToken);

    Task<(long TransactionId, DateTime CreateDate, DateTime HeartbeatDate)?> GetOldestIncompleteTransactionAsync(
        long cutoffTransactionId,
        CancellationToken cancellationToken);

    Task<(IReadOnlyList<(long Start, long End, long ResourceCount)> Ranges, long? NextStartAfter)> GetSurrogateIdRangesAsync(
        string resourceType,
        long startAfterSurrogateId,
        long upperBoundSurrogateId,
        int targetRangeSize,
        int maxRanges,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ReindexResource>> ReadRangeAsync(
        string resourceType,
        long start,
        long endSurrogateId,
        int maxCount,
        long? afterSurrogateId,
        CancellationToken cancellationToken);

    Task<(int Updated, int Conflicts)> UpdateSearchIndicesAsync(
        IReadOnlyList<ReindexResource> resources,
        CancellationToken cancellationToken);

    Task<bool> HasSearchParameterAsync(
        int searchParamId,
        CancellationToken cancellationToken);
}
