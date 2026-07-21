using Ignixa.Search.Models;

namespace Ignixa.Search.Sql.Symbols;

/// <summary>
/// An immutable snapshot of resolved SearchParamId and ResourceTypeId values, built once by Resolve
/// before Lower and Emit run. It is what makes Lower and Emit pure, synchronous functions of
/// (IR, SymbolTable, SqlCatalog): all I/O happened before it was constructed.
/// </summary>
/// <remarks>
/// Keyed by <see cref="SearchParameterInfo.Url"/>, not by a (resourceType, code) pair, because
/// SearchParamId is a global surrogate for a canonical search-parameter URL, not something scoped per
/// resource type. One SearchParamId can apply to several resource types at once (e.g. a shared
/// "individual-*" parameter); that fan-out is a property of the parameter, tracked separately, not a
/// reason to widen the key.
/// </remarks>
public sealed class SymbolTable
{
    private readonly IReadOnlyDictionary<string, short> _searchParamIds;
    private readonly IReadOnlyDictionary<string, short> _resourceTypeIds;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)>> _compartmentMembership;

    public SymbolTable(
        IReadOnlyDictionary<string, short> searchParamIds,
        IReadOnlyDictionary<string, short> resourceTypeIds,
        IReadOnlyDictionary<string, IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)>>? compartmentMembership = null)
    {
        _searchParamIds = searchParamIds;
        _resourceTypeIds = resourceTypeIds;
        _compartmentMembership = compartmentMembership ?? new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>();
    }

    /// <summary>
    /// Looks up a search parameter's SearchParamId. Throws on a miss: by the time Lower runs, every
    /// parameter the IR references must already be resolved, so a miss means Resolve's tree-walk skipped
    /// a node kind — not a legitimate runtime "not found" (that is ISymbolResolver's nullable return,
    /// handled during Resolve, before this table exists).
    /// </summary>
    public short SearchParamId(SearchParameterInfo parameter)
    {
        var url = parameter.Url?.ToString()
            ?? throw new KeyNotFoundException(
                $"SymbolTable has no SearchParamId for parameter '{parameter.Code}' -- its Url is null, so it cannot be looked up. Resolve should have resolved every parameter Lower will need.");

        return _searchParamIds.TryGetValue(url, out var id)
            ? id
            : throw new KeyNotFoundException($"SymbolTable has no SearchParamId for '{url}' -- Resolve should have resolved every parameter Lower will need.");
    }

    public short ResourceTypeId(string resourceType)
        => _resourceTypeIds.TryGetValue(resourceType, out var id)
           ? id
           : throw new KeyNotFoundException($"SymbolTable has no ResourceTypeId for '{resourceType}'.");

    /// <summary>
    /// Looks up a compartment type's full membership map — every Reference-type search parameter that
    /// establishes membership in the compartment, grouped by parameter, each with the resource types that
    /// use it. Holds names, not resolved ids (Lower resolves those through the methods above), and the
    /// compartment's full map rather than any one request's filtered subset. Throws on a miss, the same
    /// resolved-before-Lower contract as the other lookups.
    /// </summary>
    public IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)> CompartmentMembership(string compartmentType)
        => _compartmentMembership.TryGetValue(compartmentType, out var membership)
           ? membership
           : throw new KeyNotFoundException($"SymbolTable has no compartment membership map for '{compartmentType}' -- Resolve should have resolved every compartment type Lower will need.");
}
