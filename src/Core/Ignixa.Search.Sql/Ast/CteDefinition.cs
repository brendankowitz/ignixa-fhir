using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// One node in the compiler's CTE graph. ParamSource (a single search-param table filtered by
/// SearchParamId + Predicate), Intersect (AND), Union (OR), ResourceSource (all current, non-deleted
/// resources of a type -- :not's base set), Except (set subtraction -- :not's own operation).
/// ResourceSource's Predicate is null at the top level (QueryPlan.OuterPredicate is the mechanism
/// there, unchanged); a nested scope (a chain's target expression, which has no 'outer' WHERE to
/// attach to) uses it directly, Intersected with any ordinary predicates in that scope -- see the
/// chain design doc §5 for the full reasoning. ParamSource.ResourceTypeId
/// constrains which resource type's rows this CTE can return -- a SearchParamId is assigned per
/// search-parameter-definition URL, not per resource type, so a shared definition (e.g. one search
/// parameter spanning Patient/Practitioner) would otherwise let a ParamSource CTE return rows from the
/// wrong resource type. ChainJoin represents a chain (forward or reverse) as a join through
/// dbo.ReferenceSearchParam and dbo.Resource -- see the chain design doc for the full derivation.
/// CompartmentSource represents a compartment-search grouped predicate -- all rows in
/// dbo.ReferenceSearchParam matching one SearchParamId, any of a list of ResourceTypeIds (the
/// resource types that share this particular membership parameter), and a fixed compartment
/// reference -- one CTE per distinct membership SearchParamId (matching
/// CompartmentSearchQueryGenerator's own grouping), Unioned by StructuralContext.LowerCompartment.
/// See the compartment design doc §2 for the full derivation.
/// </summary>
public abstract record CteDefinition
{
    public sealed record ParamSource(TableDescriptor Table, short ResourceTypeId, short SearchParamId, Predicate? Predicate = null) : CteDefinition;

    public sealed record Intersect(CteRef Left, CteRef Right) : CteDefinition;

    public sealed record Union(IReadOnlyList<CteRef> Parts) : CteDefinition;

    public sealed record ResourceSource(short ResourceTypeId, Predicate? Predicate = null) : CteDefinition;

    public sealed record Except(CteRef Left, CteRef Right) : CteDefinition;

    public sealed record ChainJoin(
        CteRef InnerMatch,
        short ReferenceSearchParamId,
        short InnerResourceTypeId,
        IReadOnlyList<short> OutputResourceTypeIds,
        ChainDirection Direction) : CteDefinition;

    public sealed record CompartmentSource(IReadOnlyList<short> ResourceTypeIds, short SearchParamId, Predicate Predicate) : CteDefinition;
}
