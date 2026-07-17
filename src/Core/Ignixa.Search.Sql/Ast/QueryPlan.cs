namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The compiler's plan output -- Lower produces this, Emit consumes it. Every entry in Ctes,
/// including Intersect/Union/ResourceSource/Except nodes, becomes its own named CTE when emitted --
/// that is what makes this a graph rather than a tree of inline joins, and lets Match point at any
/// depth of nesting. OuterPredicate is the one exception to "everything is a CTE": ordinary
/// resource-column predicates (_id/_type/_lastUpdated) are applied as a WHERE clause on an outer join
/// to dbo.Resource, not folded into the CTE graph -- see task 6's plan section for why (avoids relying
/// on SQL Server pushing a predicate through multiple CTE layers under TOP, a real, not hypothetical,
/// risk). IncludeStage/SortSpec/full PageSpec (tier-3 result-shape stages) are not included yet --
/// nothing in scope here produces or consumes them.
/// </summary>
public sealed record QueryPlan(IReadOnlyList<CteDefinition> Ctes, CteRef Match, int? Top = null, Predicate? OuterPredicate = null)
{
    public string Explain() => PlanExplainer.Print(this);
}
