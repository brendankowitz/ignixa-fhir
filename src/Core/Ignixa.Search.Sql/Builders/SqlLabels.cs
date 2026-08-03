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

    /// <summary>The range label for a WHERE clause body.</summary>
    public const string Where = "where";

    /// <summary>The range label for the keyset-seek predicate inside a WHERE.</summary>
    public const string Seek = "seek";

    /// <summary>The range label for an ORDER BY clause body.</summary>
    public const string OrderBy = "orderBy";

    /// <summary>The range label for the final UNION ALL that stitches the result together.</summary>
    public const string Assembly = "assembly";

    /// <summary>
    /// The range label for the outer global-page SELECT of an includes-only page: the single
    /// <c>TOP (@limit + 1)</c> over the union of every include stage, applying the row budget once across
    /// all stages rather than once per stage.
    /// </summary>
    public const string IncludePage = "includePage";

    /// <summary>The identifier for the include stage at <paramref name="index"/>.</summary>
    public static string IncludeLabel(int index) => $"inc{index}";

    /// <summary>
    /// The identifier for the limit-applying companion of the include stage at <paramref name="index"/>.
    /// Every include stage emits this second CTE, which downstream SQL actually reads; the unlimited
    /// <see cref="IncludeLabel"/> body feeds it and nothing else. A stage has one plan row (labelled
    /// <see cref="IncludeLabel"/>) but two SQL ranges, so tooling takes both labels for the index.
    /// </summary>
    public static string IncludeLimitLabel(int index) => $"{IncludeLabel(index)}lim";
}
