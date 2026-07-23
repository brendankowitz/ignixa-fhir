// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.DataLayer.SqlEntityFramework.Indexing;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.DataLayer.SqlEntityFramework.Search;

/// <summary>
/// Adapts the existing <see cref="SearchIndexReferenceDataCache"/> (EF-coupled, this project's own)
/// to <see cref="ISymbolResolver"/> (Ignixa.Search.Sql's I/O contract, which has no EF reference).
/// Does not duplicate the cache's preload/negative-caching logic -- wraps it.
/// </summary>
/// <remarks>
/// Both underlying cache methods this class delegates to --
/// <see cref="SearchIndexReferenceDataCache.GetSearchParamIdAsync(SearchParameterInfo)"/> and
/// <see cref="SearchIndexReferenceDataCache.GetResourceTypeIdAsync(string?)"/> -- already query the
/// database directly on a cache miss (double-checked locking under the cache's own semaphore, see
/// SearchIndexReferenceDataCache.cs). Callers do not need to call a separate <c>PreloadXAsync</c>
/// first for correctness; preloading is a warm-cache performance optimization the cache's own
/// synchronous <c>TryGetXFromCache</c> methods rely on, not something this async adapter needs.
/// </remarks>
public sealed class SqlEntityFrameworkSymbolResolver : ISymbolResolver
{
    private readonly SearchIndexReferenceDataCache _cache;

    public SqlEntityFrameworkSymbolResolver(SearchIndexReferenceDataCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
    }

    public async Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
        => await _cache.GetSearchParamIdAsync(parameter, cancellationToken);

    public async Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
        => await _cache.GetResourceTypeIdAsync(resourceType, cancellationToken);

    public async Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken)
        => await _cache.GetSystemIdAsync(system, cancellationToken);

    /// <summary>
    /// Overrides the interface's sequential default with the cache's single-round-trip batch lookup.
    /// </summary>
    public Task<IReadOnlyDictionary<string, int?>> GetSystemIdsAsync(IReadOnlyCollection<string> systems, CancellationToken cancellationToken)
        => _cache.GetSystemIdsAsync(systems, cancellationToken);

    public async Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
        => await _cache.GetQuantityCodeIdAsync(code, cancellationToken);
}
