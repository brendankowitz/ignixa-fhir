namespace Ignixa.Search.Sql.Builders;

/// <summary>
/// The kind tokens carried by <see cref="SqlTextRange.Kind"/> — what a span of emitted SQL *is*, as
/// opposed to <see cref="SqlTextRange.Label"/> (which one it is). Kinding lets a consumer style and describe
/// a span even when no plan row is addressable by its label. Tokens, not an enum (like
/// <see cref="Ast.PlanRowKind"/>), since they reach a renderer across a wire.
/// </summary>
public static class SqlRangeKind
{
    /// <summary>A plan CTE's definition. Joins to the row whose canonical label is <see cref="SqlLabels.CteLabel"/>.</summary>
    public const string Cte = "cte";

    /// <summary>The match-page CTE that applies paging to the match CTE. No row is named for it.</summary>
    public const string MatchPage = "matchPage";

    /// <summary>
    /// The match-seed CTE that trims the has-more probe row off the match page before include stages seed
    /// from it, labelled <see cref="SqlLabels.MatchSeed"/>. No row is named for it.
    /// </summary>
    public const string MatchSeed = "matchSeed";

    /// <summary>A WHERE clause body. No row is named for it.</summary>
    public const string Where = "where";

    /// <summary>
    /// The keyset-seek predicate within a WHERE. No row is named for it; the plan node behind it is
    /// <c>plan.Page</c>, whose row is <c>page</c>.
    /// </summary>
    public const string Seek = "seek";

    /// <summary>
    /// An ORDER BY clause body. No row is named for it; the plan node behind it is <c>plan.Sort</c>,
    /// whose row is <c>sort</c>.
    /// </summary>
    public const string OrderBy = "orderBy";

    /// <summary>An include stage's unlimited body. Joins to the row <see cref="SqlLabels.IncludeLabel"/>.</summary>
    public const string Include = "include";

    /// <summary>
    /// An include stage's limit-applying companion, labelled <see cref="SqlLabels.IncludeLimitLabel"/>. No
    /// row carries that label — the stage's single row is <see cref="SqlLabels.IncludeLabel"/>, so a consumer
    /// wanting every range for a stage takes both labels for that index.
    /// </summary>
    public const string IncludeLimit = "includeLimit";

    /// <summary>The final UNION ALL stitching the match page to every include stage. No row is named for it.</summary>
    public const string Assembly = "assembly";

    /// <summary>
    /// The outer global-page SELECT of an includes-only page: one <c>TOP (@limit + 1)</c> over the union of
    /// every include stage, applying the row budget once. No row is named for it; the includes-only
    /// counterpart of the per-stage <see cref="IncludeLimit"/>.
    /// </summary>
    public const string IncludePage = "includePage";
}
