namespace Ignixa.Search.Sql.Builders;

/// <summary>
/// The kind tokens carried by <see cref="SqlTextRange.Kind"/> — what a span of emitted SQL *is*, as
/// opposed to <see cref="SqlTextRange.Label"/>, which says which one it is.
/// </summary>
/// <remarks>
/// A consumer highlighting SQL needs to style and describe a span without a plan row to lean on: most
/// ranges have a <see cref="Ast.PlanExplainRow"/> counterpart, but the structural ones
/// (<see cref="MatchPage"/>, <see cref="Where"/>, <see cref="Seek"/>, <see cref="OrderBy"/>,
/// <see cref="Assembly"/>) exist only in the emitted SQL — the plan has no node for them. Kinding the
/// range is what makes those spans self-describing rather than requiring the consumer to prefix-match the
/// label or the SQL text.
/// <para>
/// Tokens, not an enum, to match <see cref="Ast.PlanRowKind"/> and <c>IrRow.Kind</c>: these cross a wire
/// to a renderer, and a stable string survives a version skew that a reordered enum does not.
/// </para>
/// </remarks>
public static class SqlRangeKind
{
    /// <summary>A plan CTE's definition. Labelled <see cref="SqlLabels.CteLabel"/>.</summary>
    public const string Cte = "cte";

    /// <summary>The match-page CTE that applies paging to the match CTE. No plan row.</summary>
    public const string MatchPage = "matchPage";

    /// <summary>A WHERE clause body. No plan row.</summary>
    public const string Where = "where";

    /// <summary>The keyset-seek predicate within a WHERE. No plan row.</summary>
    public const string Seek = "seek";

    /// <summary>An ORDER BY clause body. No plan row.</summary>
    public const string OrderBy = "orderBy";

    /// <summary>An include stage's unlimited body. Labelled <see cref="SqlLabels.IncludeLabel"/>.</summary>
    public const string Include = "include";

    /// <summary>
    /// An include stage's limit-applying companion. Labelled <see cref="SqlLabels.IncludeLimitLabel"/>;
    /// shares the <see cref="Ast.PlanExplainRow"/> of its <see cref="Include"/> sibling.
    /// </summary>
    public const string IncludeLimit = "includeLimit";

    /// <summary>The final UNION ALL stitching the match page to every include stage. No plan row.</summary>
    public const string Assembly = "assembly";
}
