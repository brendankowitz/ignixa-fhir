// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Caching.Memory;
using Ignixa.Application.Features.Metadata.Segments;

namespace Ignixa.Application.Infrastructure.Caching;

/// <summary>
/// In-memory implementation of ICapabilityCache using MemoryCache.
/// Phase 1.2 implementation - simple, single-process caching.
/// Phase 7 will add Redis-based distributed cache.
/// </summary>
public class MemoryCapabilityCache : ICapabilityCache
{
    private readonly IMemoryCache _memoryCache;

    public MemoryCapabilityCache(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
    }

    public ValueTask<CapabilityCacheEntry?> GetAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var entry = _memoryCache.Get<CapabilityCacheEntry>(key);
        return ValueTask.FromResult(entry);
    }

    public ValueTask SetAsync(
        string key,
        CapabilityCacheEntry entry,
        TimeSpan expiration,
        CancellationToken cancellationToken)
    {
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration,
            SlidingExpiration = null, // No sliding - absolute expiration only
        };

        _memoryCache.Set(key, entry, cacheOptions);

        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(
        string key,
        CancellationToken cancellationToken)
    {
        _memoryCache.Remove(key);
        return ValueTask.CompletedTask;
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken)
    {
        // MemoryCache doesn't have a built-in Clear method
        // For Phase 1.2, we'll accept this limitation
        // Phase 7 (Redis) will support pattern-based removal

        // Note: To properly clear MemoryCache, would need to track keys separately
        // or dispose/recreate the cache. For Phase 1.2 simplicity, this is a no-op.
        // Cache entries will naturally expire after TTL.

        return ValueTask.CompletedTask;
    }
}
