namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// What a compiled statement returns. Closed and exhaustive: the cases are the terminal shapes
/// <see cref="Builders.SqlBuilder"/> can emit, and a plan is exactly one of them. Replaces the former
/// CountOnly/IncludesOnly/IncludeBoundary flag set, whose contradictory combinations had to be rejected at
/// emit time; here they are unrepresentable.
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
    /// A single <c>COUNT_BIG(DISTINCT …)</c> over the match set. Paging is ignored — a count is of the
    /// whole set, not of a page. The count is scoped to the plan's sort phase when
    /// <see cref="QueryPlan.Sort"/> is present and unscoped when it is absent, so a plan asking for a
    /// whole-result total simply carries no sort.
    /// </summary>
    public sealed record Count : ResultShape;

    /// <summary>
    /// Include-stage rows only, omitting the match page from the result while still using it to seed the
    /// stages: the <c>$includes</c> operation's second page. <paramref name="Resume"/> is the keyset
    /// boundary of the previous page, null on the first.
    /// </summary>
    /// <param name="Resume">The last include row of the previous page, or null to start at the first.</param>
    public sealed record IncludesPage(IncludeBoundary? Resume = null) : ResultShape;
}
