// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Metadata.Segments;

namespace Ignixa.Application.Infrastructure.Caching;

/// <summary>
/// Cache abstraction for CapabilityStatement entries.
/// Phase 1.2 uses in-memory implementation, Phase 7 will add Redis support.
/// </summary>
public interface ICapabilityCache
{
    /// <summary>
    /// Gets a cached capability entry by key.
    /// </summary>
    /// <param name="key">Cache key (typically from CapabilityContext.ToCacheKey()).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Cached entry if found and not expired, null otherwise.</returns>
    ValueTask<CapabilityCacheEntry?> GetAsync(
        string key,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sets a capability entry in the cache with expiration.
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <param name="entry">Entry to cache.</param>
    /// <param name="expiration">Expiration duration (e.g., 1 hour).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SetAsync(
        string key,
        CapabilityCacheEntry entry,
        TimeSpan expiration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Removes a capability entry from the cache.
    /// </summary>
    /// <param name="key">Cache key to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask RemoveAsync(
        string key,
        CancellationToken cancellationToken);

    /// <summary>
    /// Clears all capability entries from the cache.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask ClearAsync(CancellationToken cancellationToken);
}
