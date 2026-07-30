namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// What a compiled statement returns. The cases are the terminal shapes <see cref="Builders.SqlBuilder"/> can
/// emit and a plan is exactly one of them, so match, count and include-only semantics cannot be combined.
/// </summary>
public abstract record ResultShape
{
    private ResultShape()
    {
    }

    /// <summary>The shape a plan takes when none is named: <see cref="Matches"/>.</summary>
    public static ResultShape Default { get; } = new Matches();

    /// <summary>
    /// The match page, plus the rows of every include stage the plan defines. The ordinary search shape.
    /// </summary>
    public sealed record Matches : ResultShape;

    /// <summary>
    /// A single <c>COUNT_BIG(DISTINCT …)</c> over the match set. Row caps, offsets and keyset boundaries do
    /// not bound it: a count is of the whole set, not of a page.
    /// </summary>
    /// <param name="RestrictToSortPhase">
    /// Whether the count covers only the segment <see cref="QueryPlan.Sort"/> names. False — the default —
    /// counts every matching resource and ignores the sort entirely, which is the FHIR <c>Bundle.total</c>.
    /// True joins the primary sort key (or applies its <c>NOT EXISTS</c> for
    /// <see cref="SortPhase.MissingPrimary"/>) so the count answers "how many rows are in the segment I am
    /// currently paging", and requires the plan to carry a sort.
    /// </param>
    public sealed record Count(bool RestrictToSortPhase = false) : ResultShape;

    /// <summary>
    /// Include-stage rows only, omitting the match page from the result while still using it to seed the
    /// stages: the <c>$includes</c> operation's second page. <paramref name="Resume"/> is the keyset
    /// boundary of the previous page, null on the first.
    /// </summary>
    /// <param name="Resume">The last include row of the previous page, or null to start at the first.</param>
    public sealed record IncludesPage(IncludeBoundary? Resume = null) : ResultShape;
}
