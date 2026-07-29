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
/// fetches resource rows itself. It is ignored when <see cref="CountOnly"/> is set: a count is a
/// single scalar with no row to project onto, so no resource join is forced and no columns are emitted.
/// </para>
/// <para>
/// A non-null <see cref="SearchParameterHash"/> restricts the match set to rows whose
/// <c>dbo.Resource.SearchParamHash</c> differs from this value — the resources reindex must revisit
/// because their indexed parameters predate the current definition set. Rows with a <c>NULL</c> hash
/// have never been indexed and always qualify.
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
    SurrogateIdRange? SurrogateRange = null,
    /// <summary>
    /// When set, restricts the match set to rows whose dbo.Resource.SearchParamHash differs from this
    /// value — the resources reindex must revisit because their indexed parameters predate the current
    /// definition set. A row with a NULL hash has never been indexed and always qualifies.
    /// </summary>
    SqlParameterRef? SearchParameterHash = null,
    /// <summary>
    /// When true, the emitted statement returns include-stage rows only, omitting the match page from the
    /// result while still using it to seed the stages. This is the $includes operation's second page: the
    /// caller already has the match rows and asks only for more included resources.
    /// </summary>
    bool IncludesOnly = false,
    OffsetSpec? OffsetPage = null,
    bool CountPhaseScoped = false,
    /// <summary>
    /// The keyset-pagination continuation token (boundary) for the second and subsequent pages of an
    /// <see cref="IncludesOnly"/> page: the last include row the previous page returned. When set, each
    /// include stage carries a predicate that skips everything up to and including it under the global
    /// <c>ORDER BY T1 ASC, Sid1 ASC</c>, so the union of stages pages as one ordered stream. Only meaningful
    /// with <see cref="IncludesOnly"/>; the emitter rejects it otherwise, because on an ordinary search the
    /// resume predicate would silently drop included rows rather than page them.
    /// </summary>
    IncludeBoundary? IncludeBoundary = null)
{
    /// <summary>The plan's visibility, defaulting to current non-deleted rows when the caller named none.</summary>
    public ResourceVisibility EffectiveVisibility => Visibility ?? ResourceVisibility.Current;

    public string Explain() => PlanExplainer.Print(this);
}
