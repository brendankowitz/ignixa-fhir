namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// The compiler's plan output: Lower produces it and Emit consumes it. Each <see cref="Ctes"/> entry becomes a
/// named CTE. <see cref="MatchSpec"/> is the single source of match-page configuration; <see cref="Includes"/>
/// and <see cref="IncludeSeed"/> carry the include-specific graph configuration.
/// </summary>
/// <param name="Ctes">The CTE graph, in declaration order. Each entry becomes one named CTE.</param>
/// <param name="MatchSpec">The canonical configuration for the pre-page match root and its page wrappers.</param>
/// <param name="Includes">
/// The _include/_revinclude stages, in dependency order. Null or empty when the plan includes nothing, so such a
/// plan emits exactly what it emitted before includes existed.
/// </param>
/// <param name="Visibility">
/// Which resource versions are in scope; null means <see cref="ResourceVisibility.Current"/>. Read it through
/// <see cref="EffectiveVisibility"/>.
/// </param>
/// <param name="Projection">Extra result columns beyond (T1, Sid1), emitted in declared order.</param>
/// <param name="IncludeSeed">The optional match-page wrapper include stages seed from.</param>
public sealed record QueryPlan(
    IReadOnlyList<CteDefinition> Ctes,
    MatchPageSpec MatchSpec,
    IReadOnlyList<IncludeStage>? Includes = null,
    ResourceVisibility? Visibility = null,
    ProjectionSpec? Projection = null,
    CteRef? IncludeSeed = null)
{
    /// <summary>Which CTE produces the pre-page match set.</summary>
    public CteRef Match => MatchSpec.Root;

    /// <summary>Row cap on the match page, or null when uncapped.</summary>
    public int? Top => MatchSpec.Top;

    /// <summary>Resource-column filters lifted out of the CTE graph onto the outer join.</summary>
    public Predicate? OuterPredicate => MatchSpec.OuterPredicate;

    /// <summary>The _sort keys and their evaluation phase, or null for the default order.</summary>
    public SortSpec? Sort => MatchSpec.Sort;

    /// <summary>The keyset boundary the match page seeks past, or null for the first page.</summary>
    public PageSpec? Page => MatchSpec.Page;

    /// <summary>What the statement returns, or null for the default match shape.</summary>
    public ResultShape? Shape => MatchSpec.Shape;

    /// <summary>Bounds the match set by surrogate id, or null when unbounded.</summary>
    public SurrogateIdRange? SurrogateRange => MatchSpec.SurrogateRange;

    /// <summary>Restricts matches to resources with stale search parameters when present.</summary>
    public SqlParameterRef? SearchParameterHash => MatchSpec.SearchParameterHash;

    /// <summary>OFFSET/FETCH paging, or null when the page is not offset based.</summary>
    public OffsetSpec? OffsetPage => MatchSpec.OffsetPage;

    /// <summary>The plan's visibility, defaulting to current non-deleted rows when the caller named none.</summary>
    public ResourceVisibility EffectiveVisibility => Visibility ?? ResourceVisibility.Current;

    /// <summary>The plan's result shape, defaulting to <see cref="ResultShape.Matches"/>.</summary>
    public ResultShape EffectiveShape => MatchSpec.EffectiveShape;

    /// <summary>True when the statement returns a count rather than rows.</summary>
    public bool CountOnly => MatchSpec.CountOnly;

    /// <summary>True when the statement omits match rows from its final result and returns include-stage rows only.</summary>
    public bool IncludesOnly => MatchSpec.IncludesOnly;

    /// <summary>
    /// The keyset boundary for later pages of an includes-only stream, or null for any other result shape.
    /// </summary>
    public IncludeBoundary? IncludeBoundary => MatchSpec.IncludeBoundary;

    public string Explain() => PlanExplainer.Print(this);
}
