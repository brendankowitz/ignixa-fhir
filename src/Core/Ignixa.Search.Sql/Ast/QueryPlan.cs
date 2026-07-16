namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The compiler's plan output -- Lower produces this, Emit consumes it. Every entry in Ctes,
/// including Intersect/Union nodes, becomes its own named CTE when emitted -- that is what makes this
/// a graph rather than a tree of inline joins, and lets Match point at any depth of nesting.
/// IncludeStage/SortSpec/full PageSpec (tier-3 result-shape stages) are not included yet -- nothing in
/// scope here produces or consumes them.
/// </summary>
public sealed record QueryPlan(IReadOnlyList<CteDefinition> Ctes, CteRef Match, int? Top = null);
