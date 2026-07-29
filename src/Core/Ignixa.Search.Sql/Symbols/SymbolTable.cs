using Ignixa.Search.Models;

namespace Ignixa.Search.Sql.Symbols;

/// <summary>
/// An immutable snapshot of resolved SearchParamId/ResourceTypeId (and system/quantity/compartment) values,
/// built once by Resolve before Lower and Emit run — this is what makes Lower and Emit pure, synchronous
/// functions of (IR, SymbolTable, SqlCatalog). Keyed by <see cref="SearchParameterInfo.Url"/>, since a
/// SearchParamId is a global surrogate for a canonical URL, not scoped per resource type.
/// </summary>
internal sealed class SymbolTable
{
    private readonly IReadOnlyDictionary<string, short> _searchParamIds;
    private readonly IReadOnlyDictionary<string, short> _resourceTypeIds;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)>> _compartmentMembership;
    private readonly IReadOnlyDictionary<string, int?> _systemIds;
    private readonly IReadOnlyDictionary<string, int?> _quantityCodeIds;
    private readonly IReadOnlyDictionary<(string SourceResourceType, string ReferencePath), SearchParameterInfo> _notReferencedPaths;

    public SymbolTable(
        IReadOnlyDictionary<string, short> searchParamIds,
        IReadOnlyDictionary<string, short> resourceTypeIds,
        IReadOnlyDictionary<string, IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)>>? compartmentMembership = null,
        IReadOnlyDictionary<string, int?>? systemIds = null,
        IReadOnlyDictionary<string, int?>? quantityCodeIds = null,
        IReadOnlyDictionary<(string SourceResourceType, string ReferencePath), SearchParameterInfo>? notReferencedPaths = null)
    {
        _searchParamIds = searchParamIds;
        _resourceTypeIds = resourceTypeIds;
        _compartmentMembership = compartmentMembership ?? new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>();
        _systemIds = systemIds ?? new Dictionary<string, int?>();
        _quantityCodeIds = quantityCodeIds ?? new Dictionary<string, int?>();
        _notReferencedPaths = notReferencedPaths ?? new Dictionary<(string, string), SearchParameterInfo>();
    }

    /// <summary>
    /// Looks up a parameter's SearchParamId. Throws on a miss: by the time Lower runs every referenced
    /// parameter must be resolved, so a miss means Resolve skipped a node kind — not a runtime "not found"
    /// (that is ISymbolResolver's nullable return, handled during Resolve).
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

    /// <summary>
    /// The id stored for a resource type Resolve collected but the resolver could not find. Real ids are
    /// positive, so a predicate built from this matches no row — mirroring <see cref="SystemId"/>'s unknown
    /// system. Dropping the entry instead would throw <see cref="KeyNotFoundException"/> from Lower.
    /// </summary>
    public const short UnmatchableResourceTypeId = -1;

    /// <summary>
    /// Looks up a resource type's ResourceTypeId, returning <see cref="UnmatchableResourceTypeId"/> for a
    /// collected-but-not-found type. Throws only when the type was never collected — a compiler invariant
    /// violation, the same three-state contract as <see cref="SystemId"/>.
    /// </summary>
    public short ResourceTypeId(string resourceType)
        => _resourceTypeIds.TryGetValue(resourceType, out var id)
           ? id
           : throw new KeyNotFoundException($"SymbolTable has no ResourceTypeId for '{resourceType}'.");

    /// <summary>
    /// Non-throwing <see cref="ResourceTypeId"/>. Returns <see langword="false"/> only when the type was
    /// never collected; a resolver miss returns <c>(true, -1)</c>. Callers rely on that distinction to keep
    /// the sentinel as a matches-nothing OR arm rather than dropping a target and matching unconstrained.
    /// </summary>
    public bool TryGetResourceTypeId(string resourceType, out short id)
        => _resourceTypeIds.TryGetValue(resourceType, out id);

    /// <summary>
    /// A compartment type's full membership map — every Reference-type parameter that establishes membership,
    /// grouped by parameter with the resource types using it. Holds names, not ids (Lower resolves those),
    /// and the full map, not one request's filtered subset. Throws on a miss.
    /// </summary>
    public IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)> CompartmentMembership(string compartmentType)
        => _compartmentMembership.TryGetValue(compartmentType, out var membership)
           ? membership
           : throw new KeyNotFoundException($"SymbolTable has no compartment membership map for '{compartmentType}' -- Resolve should have resolved every compartment type Lower will need.");

    /// <summary>
    /// The reference-path parameter a <c>_not-referenced=Type:path</c> resolved to, or <see langword="null"/>
    /// when unresolved (Lower then uses a path-agnostic anti-join). Holds the parameter; Lower resolves its id
    /// via <see cref="SearchParamId"/>.
    /// </summary>
    public SearchParameterInfo? NotReferencedPath(string sourceResourceType, string referencePath)
        => _notReferencedPaths.TryGetValue((sourceResourceType, referencePath), out var parameter) ? parameter : null;

    /// <summary>
    /// Looks up a token/quantity-system surrogate id. A present key returns the stored nullable value
    /// (<see langword="null"/> = collected but not found). Throws when never collected — a compiler invariant
    /// violation.
    /// </summary>
    public int? SystemId(string system)
        => _systemIds.TryGetValue(system, out var id)
            ? id
            : throw new KeyNotFoundException($"SymbolTable has no SystemId for '{system}' -- Resolve should have collected every system Lower will need.");

    /// <summary>
    /// Looks up a quantity-code surrogate id. A present key returns the stored nullable value
    /// (<see langword="null"/> = collected but not found); throws when never collected.
    /// </summary>
    public int? QuantityCodeId(string code)
        => _quantityCodeIds.TryGetValue(code, out var id)
            ? id
            : throw new KeyNotFoundException($"SymbolTable has no QuantityCodeId for '{code}' -- Resolve should have collected every quantity code Lower will need.");
}
