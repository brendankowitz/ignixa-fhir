using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Domain.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.Features.Terminology;

/// <summary>
/// <see cref="ISystemRepository"/> over <see cref="SqlServerSearchIndexReferenceDataCache"/>.
/// <para>
/// Delegating rather than issuing its own SQL keeps the cache coherent: a system created here lands in the
/// same in-memory map the search index reads, so a subsequent lookup does not have to round-trip. The EF
/// implementation could not do this — it wrote through its own DbContext and then had to call
/// <c>ForgetMissingSystem</c> to invalidate a separate cache's negative entry. The cache does record misses
/// (see its <c>NegativeLookupCache</c> fields), but <see cref="SqlServerSearchIndexReferenceDataCache.GetOrCreateSystemIdAsync"/>
/// invalidates its own entry, so this repository needs no explicit invalidation call — a property of
/// delegating to the cache, not of the cache forgetting nothing. A future writer here that issued its own
/// INSERT would have to call <see cref="SqlServerSearchIndexReferenceDataCache.ForgetMissingSystem"/>.
/// </para>
/// <para>
/// Three behaviours of the EF repository are not in the cache method and are preserved here rather than
/// dropped: the URI is trimmed, a whitespace-only URI is rejected (the cache only rejects null/empty), and a
/// concurrent insert that loses the unique-constraint race is resolved by re-reading instead of surfacing.
/// The cache's own get-or-create relies on an in-process semaphore alone, which its comment is explicit is
/// not safe across processes.
/// </para>
/// </summary>
public sealed class SqlServerSystemRepository(
    SqlServerSearchIndexReferenceDataCache searchIndexCache,
    ILogger<SqlServerSystemRepository> logger) : ISystemRepository
{
    public async Task<int> GetOrCreateAsync(string systemUri, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemUri);

        var normalizedUri = systemUri.Trim();

        try
        {
            return await searchIndexCache.GetOrCreateSystemIdAsync(normalizedUri, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            // Another writer inserted the same value between our SELECT and INSERT. 2601 is a duplicate key
            // on a unique index, 2627 a unique-constraint violation; matching on the number rather than the
            // message keeps an unrelated duplicate-key failure from being reported as this URI's race.
            var existing = await searchIndexCache.TryGetSystemIdAsync(normalizedUri, cancellationToken);

            if (existing is null)
            {
                logger.LogError(ex, "Race condition detected but system not found: {SystemUri}", normalizedUri);
                throw new InvalidOperationException($"Failed to get or create system: {normalizedUri}", ex);
            }

            logger.LogDebug(
                "Race condition resolved for System: {SystemUri} -> SystemId={SystemId}", normalizedUri, existing.Value);
            return existing.Value;
        }
    }

    public async Task<int?> GetSystemIdAsync(string systemUri, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemUri);

        return await searchIndexCache.TryGetSystemIdAsync(systemUri.Trim(), cancellationToken);
    }
}
