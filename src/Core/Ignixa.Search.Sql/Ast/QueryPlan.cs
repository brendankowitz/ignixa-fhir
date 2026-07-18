namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The compiler's plan output -- Lower produces this, Emit consumes it. Every entry in Ctes,
/// including Intersect/Union/ResourceSource/Except nodes, becomes its own named CTE when emitted --
/// that is what makes this a graph rather than a tree of inline joins, and lets Match point at any
/// depth of nesting. OuterPredicate is the one exception to "everything is a CTE": ordinary
/// resource-column predicates (_id/_type/_lastUpdated) are applied as a WHERE clause on an outer join
/// to dbo.Resource, not folded into the CTE graph -- see task 6's plan section for why (avoids relying
/// on SQL Server pushing a predicate through multiple CTE layers under TOP, a real, not hypothetical,
/// risk). Includes (Phase 7) is the first tier-3 result-shape field -- non-null and non-empty only for
/// queries with _include/_revinclude/:iterate; Emit materializes a cteMatchPage CTE and a
/// (T1, Sid1, IsMatch, IsPartial) result shape only in that case, leaving every plan with no Includes
/// byte-identical to before this field existed. Sort/Page (Phase 8 part 2) are the second tier-3
/// result-shape fields -- Sort decorates ordering only (never membership), synthesized entirely inside
/// Emit's page-selection sites; Page is the keyset boundary a caller decodes from a continuation
/// token. Both are purely additive -- a plan with neither is byte-identical to before these fields
/// existed.
/// </summary>
public sealed record QueryPlan(
    IReadOnlyList<CteDefinition> Ctes,
    CteRef Match,
    int? Top = null,
    Predicate? OuterPredicate = null,
    IReadOnlyList<IncludeStage>? Includes = null,
    SortSpec? Sort = null,
    PageSpec? Page = null)
{
    public string Explain() => PlanExplainer.Print(this);
}
