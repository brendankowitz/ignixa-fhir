using Ignixa.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.Conformance;

public sealed class ConformanceBarrierRetryPolicy(
    IConformanceDefinitionsSynchronizer synchronizer,
    TimeSpan retryAfter,
    ILogger<ConformanceBarrierRetryPolicy> logger)
{
    private readonly TimeSpan _retryAfter = retryAfter > TimeSpan.Zero
        ? retryAfter
        : throw new ArgumentOutOfRangeException(nameof(retryAfter));

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            return await operation(cancellationToken);
        }
        catch (StaleConformanceDefinitionsException firstRejection)
        {
            logger.LogWarning(
                firstRejection,
                "Write transaction {TransactionId} was rejected by the conformance barrier; refreshing and retrying once",
                firstRejection.TransactionId);
        }

        try
        {
            await synchronizer.SynchronizeAsync(cancellationToken);
        }
        catch
        {
            ConformanceMetrics.RecordBarrierRejection("failed");
            throw;
        }

        try
        {
            var result = await operation(cancellationToken);
            ConformanceMetrics.RecordBarrierRejection("retried_ok");
            return result;
        }
        catch (StaleConformanceDefinitionsException secondRejection)
        {
            ConformanceMetrics.RecordBarrierRejection("failed");
            logger.LogWarning(
                secondRejection,
                "Write transaction {TransactionId} remained behind the conformance barrier after one refresh",
                secondRejection.TransactionId);
            throw new ConformanceStaleException(_retryAfter, secondRejection);
        }
        catch
        {
            ConformanceMetrics.RecordBarrierRejection("failed");
            throw;
        }
    }
}
