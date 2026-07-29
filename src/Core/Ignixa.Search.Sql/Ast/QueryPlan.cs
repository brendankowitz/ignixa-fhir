namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The compiler's plan output: Lower produces it, Emit consumes it. Each <see cref="Ctes"/> entry becomes a
/// named CTE, forming a graph <see cref="Match"/> references at any depth. <see cref="OuterPredicate"/> is the
/// lone non-CTE filter (_id/_type/_lastUpdated as a WHERE on the outer join to dbo.Resource). Base result is
/// (T1, Sid1); <see cref="Includes"/> adds (IsMatch, IsPartial), <see cref="CountOnly"/> replaces it with a count.
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
    /// Keyset continuation boundary for later pages of an <see cref="IncludesOnly"/> page: a predicate over
    /// the union of every stage's body skips up to and including the last returned row under
    /// <c>ORDER BY T1 ASC, Sid1 ASC</c>, while each stage's CTE stays unfiltered to remain a valid
    /// <c>:iterate</c> seed. Rejected by the emitter unless <see cref="IncludesOnly"/>.
    /// </summary>
    IncludeBoundary? IncludeBoundary = null)
{
    /// <summary>The plan's visibility, defaulting to current non-deleted rows when the caller named none.</summary>
    public ResourceVisibility EffectiveVisibility => Visibility ?? ResourceVisibility.Current;

    public string Explain() => PlanExplainer.Print(this);
}
