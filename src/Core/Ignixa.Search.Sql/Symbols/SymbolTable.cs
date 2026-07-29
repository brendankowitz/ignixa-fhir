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

    /// <summary>
    /// The id stored for a resource type Resolve collected but the resolver could not find. Every real
    /// ResourceTypeId is positive, so a predicate built from this one matches no row — which is exactly
    /// what a query naming a resource type the catalog has never seen should return. Emitting an
    /// unsatisfiable predicate mirrors <see cref="SystemId"/>'s treatment of an unknown system; the
    /// alternative, dropping the entry, makes the first search against an empty catalog throw
    /// <see cref="KeyNotFoundException"/> from Lower instead of returning an empty bundle.
    /// </summary>
    public const short UnmatchableResourceTypeId = -1;

    /// <summary>
    /// Looks up a resource type's ResourceTypeId, returning <see cref="UnmatchableResourceTypeId"/> for a
    /// type that was collected but not found. Throws <see cref="KeyNotFoundException"/> only when the type
    /// was never collected at all — a compiler invariant violation, the same three-state contract
    /// <see cref="SystemId"/> uses.
    /// </summary>
    public short ResourceTypeId(string resourceType)
        => _resourceTypeIds.TryGetValue(resourceType, out var id)
           ? id
           : throw new KeyNotFoundException($"SymbolTable has no ResourceTypeId for '{resourceType}'.");

    /// <summary>
    /// Attempts to look up a resource type's ResourceTypeId without throwing. Returns
    /// <see langword="false"/> only when the type was <em>never collected</em> — a
    /// <c>SymbolCollectingVisitor</c> invariant violation, not a normal "type the resolver could not
    /// find". That is a distinct case: when the DB resolver returns <see langword="null"/>,
    /// <c>Resolve.RunAsync</c> stores <see cref="UnmatchableResourceTypeId"/> (-1) rather than
    /// omitting the key, so this method returns <c>(true, -1)</c> for a resolver miss. Callers such
    /// as <see cref="LeafContext.DeclaredTargetResourceTypeIds"/> rely on that distinction: they
    /// include the sentinel as an OR arm that matches nothing rather than dropping the target —
    /// because dropping every declared target would collapse the list and reintroduce the
    /// unconstrained match the type-narrowing pass exists to prevent.
    /// </summary>
    public bool TryGetResourceTypeId(string resourceType, out short id)
        => _resourceTypeIds.TryGetValue(resourceType, out id);

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

    /// <summary>
    /// The reference-path search parameter a <c>_not-referenced=Type:path</c> search resolved to, or
    /// <see langword="null"/> when the pair was not resolved to a reference parameter — in which case
    /// Lower falls back to a path-agnostic anti-join (source type only), matching the shipping engine.
    /// Holds the parameter, not its id; Lower resolves the id through <see cref="SearchParamId"/>, the
    /// same way compartment membership does.
    /// </summary>
    public SearchParameterInfo? NotReferencedPath(string sourceResourceType, string referencePath)
        => _notReferencedPaths.TryGetValue((sourceResourceType, referencePath), out var parameter) ? parameter : null;

    /// <summary>
    /// Looks up a token-system or quantity-system surrogate ID. Returns the stored nullable value when
    /// the key exists — <see langword="null"/> means the system was collected but the resolver found no
    /// matching row (a known miss). Throws <see cref="KeyNotFoundException"/> when the key was never
    /// collected at all, which is a compiler invariant violation: every system Lower references must be
    /// resolved before Lower runs.
    /// </summary>
    public int? SystemId(string system)
        => _systemIds.TryGetValue(system, out var id)
            ? id
            : throw new KeyNotFoundException($"SymbolTable has no SystemId for '{system}' -- Resolve should have collected every system Lower will need.");

    /// <summary>
    /// Looks up a quantity-code surrogate ID. Returns the stored nullable value when the key exists —
    /// <see langword="null"/> means the code was collected but the resolver found no matching row (a
    /// known miss). Throws <see cref="KeyNotFoundException"/> when the key was never collected at all.
    /// </summary>
    public int? QuantityCodeId(string code)
        => _quantityCodeIds.TryGetValue(code, out var id)
            ? id
            : throw new KeyNotFoundException($"SymbolTable has no QuantityCodeId for '{code}' -- Resolve should have collected every quantity code Lower will need.");
}
