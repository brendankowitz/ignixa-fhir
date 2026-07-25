using System.Collections.Concurrent;
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Tests.Corpus;

/// <summary>
/// Hands out a stable surrogate id for every symbol the compiler asks about, so a corpus query
/// compiles without a database. The ids are arbitrary: the shipping engine's captured SQL carries ids
/// from a live catalog that no longer exists, so <see cref="SqlShapeCanonicalizer"/> erases integer
/// literals on both sides and comparison never depends on the two agreeing.
///
/// Nothing ever resolves to null. A null would lower to <c>1 = 0</c> and collapse the plan, turning a
/// missing lookup row into a false "the compiler emits a different shape" finding.
/// </summary>
public sealed class CorpusSymbolResolver : ISymbolResolver
{
    private readonly ConcurrentDictionary<string, short> _searchParamIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, short> _resourceTypeIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _systemIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _quantityCodeIds = new(StringComparer.Ordinal);

    public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        var key = parameter.Url?.ToString() ?? parameter.Code ?? parameter.Name ?? "unnamed";
        return Task.FromResult<short?>(_searchParamIds.GetOrAdd(key, _ => (short)(1000 + _searchParamIds.Count)));
    }

    public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
        => Task.FromResult<short?>(_resourceTypeIds.GetOrAdd(resourceType, _ => (short)(1 + _resourceTypeIds.Count)));

    public Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken)
        => Task.FromResult<int?>(_systemIds.GetOrAdd(system, _ => 1 + _systemIds.Count));

    public Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
        => Task.FromResult<int?>(_quantityCodeIds.GetOrAdd(code, _ => 1 + _quantityCodeIds.Count));
}
