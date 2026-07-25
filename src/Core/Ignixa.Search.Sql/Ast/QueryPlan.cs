namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The compiler's plan output: Lower produces it, Emit consumes it. Every entry in <see cref="Ctes"/>
/// becomes its own named CTE when emitted, which makes the plan a graph rather than a tree of inline
/// joins and lets <see cref="Match"/> reference any nesting depth. <see cref="OuterPredicate"/> is the
/// one thing not expressed as a CTE: resource-column filters (_id/_type/_lastUpdated) apply as a WHERE
/// clause on an outer join to dbo.Resource, avoiding reliance on SQL Server pushing a predicate through
/// multiple CTE layers under TOP.
/// <para>
/// <see cref="Includes"/>, <see cref="Sort"/>, <see cref="Page"/>, and <see cref="CountOnly"/> are
/// additive result-shape modifiers. With all of them at their defaults, Emit renders the plain
/// (T1, Sid1) row shape. A non-empty <see cref="Includes"/> switches to a (T1, Sid1, IsMatch, IsPartial)
/// shape; <see cref="CountOnly"/> replaces every row-returning shape with a single
/// COUNT_BIG(DISTINCT Sid1) SELECT.
/// </para>
/// <para>
/// A non-null <see cref="Projection"/> appends dbo.Resource columns after the identity (and flag)
/// columns, turning the emitted statement into a self-contained row-returning query. When
/// <see cref="Projection"/> is null the historical identity-only shape is preserved and the caller
/// fetches resource rows itself.
/// </para>
/// </summary>
public sealed record QueryPlan(
    IReadOnlyList<CteDefinition> Ctes,
    CteRef Match,
    int? Top = null,
    Predicate? OuterPredicate = null,
    IReadOnlyList<IncludeStage>? Includes = null,
    SortSpec? Sort = null,
    PageSpec? Page = null,
    bool CountOnly = false,
    ResourceVisibility? Visibility = null,
    ProjectionSpec? Projection = null,
    SurrogateIdRange? SurrogateRange = null)
{
    /// <summary>The plan's visibility, defaulting to current non-deleted rows when the caller named none.</summary>
    public ResourceVisibility EffectiveVisibility => Visibility ?? ResourceVisibility.Current;

    public string Explain() => PlanExplainer.Print(this);
}
