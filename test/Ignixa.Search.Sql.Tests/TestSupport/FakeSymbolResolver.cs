using Ignixa.Search.Models;
using Ignixa.Search.Sql.Symbols;

namespace Ignixa.Search.Sql.Tests.TestSupport;

internal sealed class FakeSymbolResolver : ISymbolResolver
{
    public Dictionary<string, short> SearchParamIds { get; } = [];

    public Dictionary<string, short> ResourceTypeIds { get; } = [];

    public Dictionary<string, int> SystemIds { get; } = [];

    public Dictionary<string, int> QuantityCodeIds { get; } = [];

    public Task<short?> GetSearchParamIdAsync(SearchParameterInfo parameter, CancellationToken cancellationToken)
        => Task.FromResult(parameter.Url?.ToString() is { } url && SearchParamIds.TryGetValue(url, out var id) ? (short?)id : null);

    public Task<short?> GetResourceTypeIdAsync(string resourceType, CancellationToken cancellationToken)
        => Task.FromResult(ResourceTypeIds.TryGetValue(resourceType, out var id) ? (short?)id : null);

    public Task<int?> GetSystemIdAsync(string system, CancellationToken cancellationToken)
        => Task.FromResult(SystemIds.TryGetValue(system, out var id) ? (int?)id : null);

    public Task<int?> GetQuantityCodeIdAsync(string code, CancellationToken cancellationToken)
        => Task.FromResult(QuantityCodeIds.TryGetValue(code, out var id) ? (int?)id : null);
}
