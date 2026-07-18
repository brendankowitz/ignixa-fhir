using Ignixa.Search.Models;

namespace Ignixa.Search.Sql.Symbols;

/// <summary>
/// An immutable snapshot of resolved SearchParamId/ResourceTypeId values, built once by Resolve
/// before Lower/Emit run. Lower and Emit are pure, synchronous functions of (IR, SymbolTable,
/// SqlCatalog) -- this type is what makes that true; all I/O happened before it was constructed.
/// </summary>
/// <remarks>
/// Keyed by <see cref="SearchParameterInfo.Url"/>, not by a (resourceType, code) pair. The
/// dbo.SearchParam table's Uri column is the table's actual primary key
/// (src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/Entities/SearchParamEntity.cs) -- SearchParamId
/// is a global surrogate for a canonical search-parameter URL, not something scoped per resource
/// type. A single SearchParamId can legitimately apply to several resource types at once (e.g. a
/// shared "individual-*" parameter with BaseResourceTypes = [Patient, Practitioner, RelatedPerson]);
/// that fan-out is a property of the parameter, tracked separately from this identity lookup, not a
/// reason to widen the key. This matches how the existing data layer already resolves the same
/// identity: SearchIndexReferenceDataCache.GetSearchParamIdAsync keys strictly by URL string, and
/// CompartmentSearchQueryGenerator builds its own "SearchParamUri -> (SearchParamId, Set&lt;ResourceTypeId&gt;)"
/// map for exactly this reason -- one SearchParamId, a set of resource types on the side. The design
/// doc's `("Patient","name") -> SearchParamId 202` worked example is descriptive shorthand for what
/// that URL means in context, not the literal key shape.
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
    /// Looks up a search parameter's SearchParamId. Throws if Resolve did not resolve this
    /// parameter -- by the time Lower runs, every parameter the IR actually references must
    /// already be in the table; a miss here means Resolve's tree-walk (task 4) missed a node kind,
    /// not a legitimate runtime "not found" case (that's ISymbolResolver's nullable return, handled
    /// during Resolve, before this table is ever handed to Lower).
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
    /// Looks up a compartment type's full membership map -- every Reference-type search parameter
    /// that establishes membership in this compartment, grouped by parameter, each with the full set
    /// of resource types that use it. Names, not resolved ids (Lower resolves SearchParamId/
    /// ResourceTypeId through the existing methods above) -- see Resolve's remarks for why this
    /// stores the compartment's FULL map rather than pre-filtered to any one request's
    /// FilteredResourceTypes. Throws if Resolve did not resolve this compartment type -- the same
    /// "Resolve should have resolved every X Lower will need" contract SearchParamId/ResourceTypeId
    /// already establish.
    /// </summary>
    public IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)> CompartmentMembership(string compartmentType)
        => _compartmentMembership.TryGetValue(compartmentType, out var membership)
           ? membership
           : throw new KeyNotFoundException($"SymbolTable has no compartment membership map for '{compartmentType}' -- Resolve should have resolved every compartment type Lower will need.");
}
