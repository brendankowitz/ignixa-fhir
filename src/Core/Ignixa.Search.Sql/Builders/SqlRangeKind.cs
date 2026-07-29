namespace Ignixa.Search.Sql.Builders;

/// <summary>
/// The kind tokens carried by <see cref="SqlTextRange.Kind"/> — what a span of emitted SQL *is*, as
/// opposed to <see cref="SqlTextRange.Label"/>, which says which one it is.
/// </summary>
/// <remarks>
/// A consumer highlighting SQL needs to style and describe a span even when no plan row is addressable by
/// that span's label. Kinding the range is what makes those spans self-describing, rather than requiring
/// the consumer to prefix-match the label or sniff the SQL text.
/// <para>
/// "No row" below means precisely that: no <see cref="Ast.PlanExplainRow.CanonicalLabel"/> equals this
/// range's label. It does not mean the plan had no say in the SQL — <see cref="OrderBy"/> and
/// <see cref="Seek"/> are emitted from <c>plan.Sort</c> and <c>plan.Page</c>, which do have rows
/// (<c>sort</c> and <c>page</c>); those rows are simply not named after these ranges.
/// </para>
/// <para>
/// Tokens, not an enum, to match <see cref="Ast.PlanRowKind"/> and <c>IrRow.Kind</c>: these are intended
/// to reach a renderer across a wire, and a stable string survives a version skew that a reordered enum
/// does not.
/// </para>
/// </remarks>
public static class SqlRangeKind
{
    /// <summary>A plan CTE's definition. Joins to the row whose canonical label is <see cref="SqlLabels.CteLabel"/>.</summary>
    public const string Cte = "cte";

    /// <summary>The match-page CTE that applies paging to the match CTE. No row is named for it.</summary>
    public const string MatchPage = "matchPage";

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
    /// An include stage's limit-applying companion, labelled <see cref="SqlLabels.IncludeLimitLabel"/>.
    /// No row carries that label: the stage's single row is named <see cref="SqlLabels.IncludeLabel"/>,
    /// so a consumer wanting every range for a stage takes both labels for the same index rather than
    /// deriving one from the other by string surgery.
    /// </summary>
    public const string IncludeLimit = "includeLimit";

    /// <summary>The final UNION ALL stitching the match page to every include stage. No row is named for it.</summary>
    public const string Assembly = "assembly";

    /// <summary>
    /// The outer global-page SELECT of an includes-only page: one <c>TOP (@limit + 1)</c> over the union
    /// of every include stage, applying the row budget once across all stages. No row is named for it; it
    /// is the includes-only counterpart of the per-stage <see cref="IncludeLimit"/>.
    /// </summary>
    public const string IncludePage = "includePage";
}
