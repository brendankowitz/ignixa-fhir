using System.Collections.Concurrent;
using Ignixa.Domain.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ignixa.DataLayer.SqlServer.Indexing;

/// <summary>
/// Owns exactly one <see cref="SqlServerSearchIndexReferenceDataCache"/> per tenant for the lifetime of the
/// process, so every consumer of a tenant's reference data sees the same instance.
/// <para>
/// <b>Why this exists rather than each caller creating its own.</b> Row generators read
/// <c>SearchParameterMappings</c> off the cache instance the write path was given, and skip a row when a
/// parameter is missing from it. So a search-parameter sync that populates a <i>different</i> instance fixes
/// nothing: the write path keeps dropping index rows while the sync reports success. Before this type, the
/// write path's cache was reachable only from inside <c>SqlEntityFrameworkRepositoryFactory</c>'s private
/// per-tenant cache, which is why the package-load sync could never reach it.
/// </para>
/// <para>
/// Creation is shared, not per-caller: the entry is a <see cref="Lazy{T}"/> over the creation task, so
/// concurrent first-callers await one construction and one pair of eager preloads rather than racing to build
/// duplicates. A failed creation is evicted so the next caller retries instead of inheriting a permanently
/// faulted task. The creation token is deliberately <see cref="CancellationToken.None"/> — the result is
/// shared, so one caller's cancellation must not poison the instance every other tenant request will use.
/// </para>
/// <para>
/// The <c>Forget*</c> methods broadcast across every tenant, mirroring EF's
/// <c>MultiTenantSearchIndexCache.ForgetMissingSystem</c>. A negative lookup is bounded by its own TTL inside
/// each cache; this exists so an in-process write that creates a row can retract the record immediately
/// rather than leaving other tenants answering "missing" until expiry.
/// </para>
/// </summary>
public sealed class SqlServerSearchIndexCacheRegistry(
    ISqlExecutionService sqlExecutionService,
    ILoggerFactory loggerFactory) : IDisposable
{
    private readonly ConcurrentDictionary<int, Lazy<Task<SqlServerSearchIndexReferenceDataCache>>> _caches = new();
    private readonly ILogger<SqlServerSearchIndexCacheRegistry> _logger =
        loggerFactory.CreateLogger<SqlServerSearchIndexCacheRegistry>();

    private bool _disposed;

    public Task<SqlServerSearchIndexReferenceDataCache> GetOrCreateAsync(int tenantId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var entry = _caches.GetOrAdd(
            tenantId,
            id => new Lazy<Task<SqlServerSearchIndexReferenceDataCache>>(
                () => SqlServerRepositoryFactory.CreateReferenceDataCacheAsync(
                    sqlExecutionService, id, loggerFactory, CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return AwaitEvictingOnFailureAsync(tenantId, entry);
    }

    /// <summary>
    /// Retracts a recorded "system is missing" across every tenant's cache. Safe to call for a tenant that
    /// has no cache yet, and never blocks on one still being created.
    /// </summary>
    public void ForgetMissingSystem(string? systemUri)
        => Broadcast(cache => cache.ForgetMissingSystem(systemUri));

    public void ForgetMissingQuantityCode(string? code)
        => Broadcast(cache => cache.ForgetMissingQuantityCode(code));

    /// <summary>Drops a tenant's cache so the next request rebuilds it. Returns false when none existed.</summary>
    public bool Invalidate(int tenantId)
    {
        if (!_caches.TryRemove(tenantId, out var entry))
        {
            return false;
        }

        _logger.LogInformation("Invalidated reference data cache for tenant {TenantId}", tenantId);
        DisposeEntry(entry);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var entry in _caches.Values)
        {
            DisposeEntry(entry);
        }

        _caches.Clear();
    }

    private async Task<SqlServerSearchIndexReferenceDataCache> AwaitEvictingOnFailureAsync(
        int tenantId, Lazy<Task<SqlServerSearchIndexReferenceDataCache>> entry)
    {
        try
        {
            return await entry.Value;
        }
        catch (Exception ex)
        {
            // Evict rather than leave a faulted task cached: otherwise a transient failure during the first
            // request for a tenant would deny that tenant reference data for the process lifetime.
            _caches.TryRemove(tenantId, out _);
            _logger.LogError(ex, "Failed to build reference data cache for tenant {TenantId}", tenantId);
            throw;
        }
    }

    private void Broadcast(Action<SqlServerSearchIndexReferenceDataCache> action)
    {
        foreach (var entry in _caches.Values)
        {
            if (TryGetCreated(entry, out var cache))
            {
                action(cache);
            }
        }
    }

    private static void DisposeEntry(Lazy<Task<SqlServerSearchIndexReferenceDataCache>> entry)
    {
        if (TryGetCreated(entry, out var cache))
        {
            cache.Dispose();
        }
    }

    private static bool TryGetCreated(
        Lazy<Task<SqlServerSearchIndexReferenceDataCache>> entry,
        out SqlServerSearchIndexReferenceDataCache cache)
    {
        if (entry.IsValueCreated && entry.Value.IsCompletedSuccessfully)
        {
            cache = entry.Value.Result;
            return true;
        }

        cache = null!;
        return false;
    }
}
