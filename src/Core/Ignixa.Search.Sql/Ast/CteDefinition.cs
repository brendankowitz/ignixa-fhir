using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// One node in the compiler's CTE graph. Scoped to this plan's needs only: ParamSource (a single
/// search-param table filtered by SearchParamId + Predicate), Intersect (AND), Union (OR).
/// ResourceSource/Except/ChainJoin are NOT included -- nothing in this plan's scope (:not, chain)
/// constructs them; add when that lowering rule is written. See design doc's CteDefinition grammar.
/// </summary>
public abstract record CteDefinition
{
    public sealed record ParamSource(TableDescriptor Table, short SearchParamId, Predicate Predicate) : CteDefinition;

    public sealed record Intersect(CteRef Left, CteRef Right) : CteDefinition;

    public sealed record Union(IReadOnlyList<CteRef> Parts) : CteDefinition;
}
