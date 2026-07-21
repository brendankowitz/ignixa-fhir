namespace Ignixa.Search.Sql.Builders;

/// <summary>
/// The single producer of every index-derived label the compiler emits — CTE identifiers in the SQL,
/// the section labels on <see cref="SqlTextRange"/>, and the row labels
/// <see cref="Ast.PlanExplainer"/> prints.
/// </summary>
/// <remarks>
/// These strings carry two loads at once: SQL Server joins a CTE reference to its definition by them, and
/// tooling is intended to join a plan row to its parameter and to its slice of SQL text by them. Two
/// independent format strings would be one silent typo from breaking either join with nothing to fail at
/// compile time, so every producer routes through here. Guarded by test, not by the compiler — nothing
/// stops a future call site interpolating <c>$"cte{i}"</c> by hand.
/// <para>
/// Lives in <c>Builders</c> rather than on the explainer because these are SQL identifiers first and
/// display text second: emission is the correctness-critical consumer, and a change made to render a
/// nicer explain line must not be able to change the emitted SQL.
/// </para>
/// </remarks>
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

    /// <summary>The identifier for the include stage at <paramref name="index"/>.</summary>
    public static string IncludeLabel(int index) => $"inc{index}";

    /// <summary>
    /// The identifier for the limit-applying companion of the include stage at <paramref name="index"/>.
    /// Every include stage emits this second CTE, which is what downstream SQL actually reads — the
    /// unlimited <see cref="IncludeLabel"/> body feeds it and nothing else.
    /// </summary>
    /// <remarks>
    /// A plan has one <see cref="Ast.PlanExplainRow"/> per include stage, labelled
    /// <see cref="IncludeLabel"/>, but emits two SQL ranges. Tooling that highlights the SQL for a plan row
    /// must therefore take both this label and <see cref="IncludeLabel"/> for that index, rather than
    /// deriving one from the other by string surgery.
    /// </remarks>
    public static string IncludeLimitLabel(int index) => $"{IncludeLabel(index)}lim";
}
