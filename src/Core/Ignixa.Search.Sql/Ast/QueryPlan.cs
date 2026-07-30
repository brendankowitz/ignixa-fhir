namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The compiler's plan output: Lower produces it, Emit consumes it. Each <see cref="Ctes"/> entry becomes a
/// named CTE, forming a graph <see cref="Match"/> references at any depth. <see cref="OuterPredicate"/> holds
/// the resource-column filters (_id/_type/_lastUpdated) lifted out of the CTE graph onto the outer join to
/// dbo.Resource; <see cref="Page"/>, <see cref="SurrogateRange"/>, <see cref="SearchParameterHash"/> and a
/// MissingPrimary <see cref="Sort"/> each contribute their own outer filter alongside it, subject to
/// <see cref="Shape"/> — a count ignores <see cref="Page"/> outright and applies <see cref="Sort"/> only when
/// it is restricted to the sort phase. Base result is (T1, Sid1); <see cref="Includes"/> adds
/// (IsMatch, IsPartial), and <see cref="Shape"/> can replace it with a count or with include rows alone.
/// </summary>
/// <param name="Ctes">The CTE graph, in declaration order. Each entry becomes one named CTE.</param>
/// <param name="Match">Which CTE the outer query joins to dbo.Resource to produce the match set.</param>
/// <param name="Top">
/// Row cap on the match page, emitted as <c>TOP (Top + 1)</c> so the caller can tell a full page from the
/// last one. Null means uncapped.
/// </param>
/// <param name="OuterPredicate">
/// Resource-column filters (_id/_type/_lastUpdated) lifted out of the CTE graph onto the outer join.
/// </param>
/// <param name="Includes">
/// The _include/_revinclude stages, in dependency order. Null (never an empty list) when the plan includes
/// nothing, so such a plan emits exactly what it emitted before includes existed.
/// </param>
/// <param name="Sort">The _sort keys and the phase they are evaluated in. Null means the default (T1, Sid1) order.</param>
/// <param name="Page">
/// The keyset boundary the match page seeks past. Its arity must match the active key count of
/// <paramref name="Sort"/>'s current phase.
/// </param>
/// <param name="Shape">
/// What the statement returns; null means <see cref="ResultShape.Matches"/>. Read it through
/// <see cref="EffectiveShape"/>, which resolves the default.
/// </param>
/// <param name="Visibility">
/// Which resource versions are in scope; null means <see cref="ResourceVisibility.Current"/>. Read it through
/// <see cref="EffectiveVisibility"/>.
/// </param>
/// <param name="Projection">Extra result columns beyond (T1, Sid1), emitted in declared order.</param>
/// <param name="SurrogateRange">Bounds the match set by surrogate id, which is how a bulk export shards work.</param>
/// <param name="SearchParameterHash">
/// When set, restricts the match set to rows whose dbo.Resource.SearchParamHash differs from this value — the
/// resources reindex must revisit because their indexed parameters predate the current definition set. A row
/// with a NULL hash has never been indexed and always qualifies.
/// </param>
/// <param name="OffsetPage">
/// OFFSET/FETCH paging, mutually exclusive with <paramref name="Top"/> and <paramref name="Page"/>: SQL Server
/// rejects TOP alongside OFFSET (error 10741), and a seek alongside OFFSET applies two paging mechanisms at once.
/// </param>
public sealed record QueryPlan(
    IReadOnlyList<CteDefinition> Ctes,
    CteRef Match,
    int? Top = null,
    Predicate? OuterPredicate = null,
    IReadOnlyList<IncludeStage>? Includes = null,
    SortSpec? Sort = null,
    PageSpec? Page = null,
    ResultShape? Shape = null,
    ResourceVisibility? Visibility = null,
    ProjectionSpec? Projection = null,
    SurrogateIdRange? SurrogateRange = null,
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
