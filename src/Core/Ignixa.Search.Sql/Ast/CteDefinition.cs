using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// One node in the compiler's CTE graph. ParamSource (a single search-param table filtered by
/// SearchParamId + Predicate), Intersect (AND), Union (OR), ResourceSource (all current, non-deleted
/// resources of a type -- :not's base set), Except (set subtraction -- :not's own operation).
/// ResourceSource has no Predicate: ordinary resource-column filtering (_id/_type/_lastUpdated) is a
/// separate mechanism, QueryPlan.OuterPredicate -- see that type's remarks. ParamSource.ResourceTypeId
/// constrains which resource type's rows this CTE can return -- a SearchParamId is assigned per
/// search-parameter-definition URL, not per resource type, so a shared definition (e.g. one search
/// parameter spanning Patient/Practitioner) would otherwise let a ParamSource CTE return rows from the
/// wrong resource type.
/// </summary>
public abstract record CteDefinition
{
    public sealed record ParamSource(TableDescriptor Table, short ResourceTypeId, short SearchParamId, Predicate Predicate) : CteDefinition;

    public sealed record Intersect(CteRef Left, CteRef Right) : CteDefinition;

    public sealed record Union(IReadOnlyList<CteRef> Parts) : CteDefinition;

    public sealed record ResourceSource(short ResourceTypeId) : CteDefinition;

    public sealed record Except(CteRef Left, CteRef Right) : CteDefinition;
}
