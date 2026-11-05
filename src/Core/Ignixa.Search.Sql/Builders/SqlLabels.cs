namespace Ignixa.Search.Sql.Builders;

/// <summary>
/// The single producer of every index-derived label the compiler emits — CTE identifiers in the SQL, the
/// section labels on <see cref="SqlTextRange"/>, and the row labels <see cref="Ast.PlanExplainer"/> prints.
/// These strings double as SQL and tooling join keys, so every producer routes through here to avoid a
/// silent typo (guarded by test, not the compiler). In Builders because they are SQL identifiers first.
/// </summary>
public static class SqlLabels
{
    /// <summary>The identifier for the CTE at <paramref name="index"/>.</summary>
    public static string CteLabel(int index) => $"cte{index}";

    /// <summary>The match-page CTE. A real SQL identifier, not just a range label.</summary>
    public const string MatchPage = "cteMatchPage";

    /// <summary>
    /// The match-seed CTE: <see cref="MatchPage"/> minus its has-more probe row, which is what
    /// _include/_revinclude stages seed from. Emitted only when the page over-fetches — under either paging
    /// mechanism, so ask <c>MatchPageSpec.TrimmedPageSize</c> rather than a mechanism's own flag; otherwise
    /// stages seed from <see cref="MatchPage"/> itself. A real SQL identifier, not just a range label.
    /// </summary>
    public const string MatchSeed = "cteMatchSeed";

    /// <summary>The range label for a WHERE clause body.</summary>
    public const string Where = "where";

    /// <summary>The range label for the keyset-seek predicate inside a WHERE.</summary>
    public const string Seek = "seek";

    /// <summary>The range label for an ORDER BY clause body.</summary>
    public const string OrderBy = "orderBy";

    /// <summary>
    /// The range label for the union of a shape's arms: the match page and include stages (<c>UNION ALL</c>),
    /// or only the include stages (<c>UNION</c>) for an includes page or includes count.
    /// </summary>
    public const string Assembly = "assembly";

    /// <summary>
    /// The range label for the outer global-page SELECT of an includes-only page: the single
    /// <c>TOP (Limit + 1)</c> over the union of every include stage, applying the row budget once across
    /// all stages rather than once per stage.
    /// </summary>
    public const string IncludePage = "includePage";

    /// <summary>
    /// The range label for the outer SELECT of an includes count: one <c>COUNT_BIG(*)</c> over the
    /// deduplicated union of every include stage body.
    /// </summary>
    public const string IncludeCount = "includeCount";

    /// <summary>The identifier for the include stage at <paramref name="index"/>.</summary>
    public static string IncludeLabel(int index) => $"inc{index}";

    /// <summary>
    /// The identifier for the limit-applying companion of the include stage at <paramref name="index"/>.
    /// Emitted only under <see cref="Ast.ResultShape.Matches"/>, where downstream SQL reads it; the includes
    /// page and includes count read the unlimited <see cref="IncludeLabel"/> body directly. Where emitted, a
    /// stage has one plan row (labelled <see cref="IncludeLabel"/>) but two SQL ranges, so tooling takes both
    /// labels for the index.
    /// </summary>
    public static string IncludeLimitLabel(int index) => $"{IncludeLabel(index)}lim";
}
