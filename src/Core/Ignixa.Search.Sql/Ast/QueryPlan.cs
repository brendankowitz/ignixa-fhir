namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The compiler's plan output: Lower produces it, Emit consumes it. Each <see cref="Ctes"/> entry becomes a
/// named CTE, forming a graph <see cref="Match"/> references at any depth. <see cref="OuterPredicate"/> is the
/// lone non-CTE filter (_id/_type/_lastUpdated as a WHERE on the outer join to dbo.Resource). Base result is
/// (T1, Sid1); <see cref="Includes"/> adds (IsMatch, IsPartial), and <see cref="Shape"/> can replace it with a
/// count or with include rows alone.
/// </summary>
public sealed record QueryPlan(
    IReadOnlyList<CteDefinition> Ctes,
    CteRef Match,
    int? Top = null,
    Predicate? OuterPredicate = null,
    IReadOnlyList<IncludeStage>? Includes = null,
    SortSpec? Sort = null,
    PageSpec? Page = null,
    /// <summary>
    /// What the statement returns; null means <see cref="ResultShape.Matches"/>. Read it through
    /// <see cref="EffectiveShape"/>, which resolves the default.
    /// </summary>
    ResultShape? Shape = null,
    ResourceVisibility? Visibility = null,
    ProjectionSpec? Projection = null,
    SurrogateIdRange? SurrogateRange = null,
    /// <summary>
    /// When set, restricts the match set to rows whose dbo.Resource.SearchParamHash differs from this
    /// value — the resources reindex must revisit because their indexed parameters predate the current
    /// definition set. A row with a NULL hash has never been indexed and always qualifies.
    /// </summary>
    SqlParameterRef? SearchParameterHash = null,
    OffsetSpec? OffsetPage = null)
{
    /// <summary>The plan's visibility, defaulting to current non-deleted rows when the caller named none.</summary>
    public ResourceVisibility EffectiveVisibility => Visibility ?? ResourceVisibility.Current;

    /// <summary>The plan's result shape, defaulting to <see cref="ResultShape.Matches"/>.</summary>
    public ResultShape EffectiveShape => Shape ?? ResultShape.Default;

    /// <summary>True when the statement returns a count rather than rows.</summary>
    public bool CountOnly => EffectiveShape is ResultShape.Count;

    /// <summary>True when the statement omits the match page and returns include-stage rows only.</summary>
    public bool IncludesOnly => EffectiveShape is ResultShape.IncludesPage;

    /// <summary>
    /// The keyset boundary for later pages of an <see cref="IncludesOnly"/> stream: a predicate over the
    /// union of every stage's body skips up to and including the last returned row under
    /// <c>ORDER BY T1 ASC, Sid1 ASC</c>, while each stage's CTE stays unfiltered to remain a valid
    /// <c>:iterate</c> seed. Null on any other shape, so an ordinary search cannot silently drop include rows.
    /// </summary>
    public IncludeBoundary? IncludeBoundary => (EffectiveShape as ResultShape.IncludesPage)?.Resume;

    public string Explain() => PlanExplainer.Print(this);
}
