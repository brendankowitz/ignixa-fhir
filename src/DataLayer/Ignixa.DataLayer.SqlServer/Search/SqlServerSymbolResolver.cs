using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.DataLayer.SqlServer.Search;

/// <summary>
/// Adapts <see cref="SqlServerSearchIndexReferenceDataCache"/> (this project's tenant-scoped reference-data
/// cache) to <see cref="ISymbolResolver"/> (Ignixa.Search.Sql's I/O contract). System/quantity-code lookups
/// route through the cache's read-only, miss-returns-null methods -- never the write path's get-or-create
/// methods, which would silently insert new catalog rows as a side effect of a search.
/// </summary>
public sealed class SqlServerSymbolResolver(SqlServerSearchIndexReferenceDataCache cache) : ISymbolResolver
{
    private readonly SqlServerSearchIndexReferenceDataCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
        => _cache.GetSearchParamIdAsync(parameter.Url?.ToString() ?? string.Empty, cancellationToken);

    public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
        => _cache.GetResourceTypeIdAsync(resourceType, cancellationToken);

    public Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken)
        => _cache.TryGetSystemIdAsync(system, cancellationToken);

    public Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
        => _cache.TryGetQuantityCodeIdAsync(code, cancellationToken);
}
