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

    // A record synthesizes a protected copy constructor, which an external assembly can chain to in order to
    // derive a fourth case: `record Rogue(ResultShape Seed) : ResultShape(Seed)`. That case would match the
    // abstract type but none of the concrete ones, reaching the same silent fall-through this union exists to
    // prevent. An abstract private protected member cannot be implemented outside this assembly, so it makes
    // the hierarchy closed in fact and not merely by convention.
    private protected abstract void ThisUnionIsClosed();

    /// <summary>The shape a plan takes when none is named: <see cref="Matches"/>.</summary>
    public static ResultShape Default { get; } = new Matches();

    /// <summary>
    /// The match page, plus the rows of every include stage the plan defines. The ordinary search shape.
    /// </summary>
    public sealed record Matches : ResultShape
    {
        private protected override void ThisUnionIsClosed()
        {
        }
    }

    /// <summary>
    /// A single <c>COUNT_BIG(DISTINCT …)</c> over the match set. Row caps, offsets and keyset boundaries do
    /// not bound it: a count is of the whole set, not of a page. Closed over which rows it covers, so an
    /// unrecognised scope cannot be constructed and silently fall through to the whole set.
    /// </summary>
    public abstract record Count : ResultShape
    {
        private Count()
        {
        }

        /// <summary>
        /// Every matching resource, ignoring any sort the plan carries. This is the FHIR <c>Bundle.total</c>.
        /// </summary>
        public sealed record AllMatches : Count
        {
            private protected override void ThisUnionIsClosed()
            {
            }
        }

        /// <summary>
        /// Counts only the rows the plan's current <see cref="SortPhase"/> reaches. The sort's ordering is
        /// irrelevant to a count, so this applies just the phase's row filter: under
        /// <see cref="SortPhase.Valued"/> the primary sort key's INNER join, which drops resources that have
        /// no value for it; under <see cref="SortPhase.MissingPrimary"/> the <c>NOT EXISTS</c> that keeps
        /// only those. It answers "how many rows are in the segment I am paging right now", which is what a
        /// caller walking a two-phase sort needs to size each segment. Requires the plan to carry at least
        /// one sort key — a keyless sort names no segment, and is rejected rather than counted as the whole
        /// set. A <c>_lastUpdated</c> or <c>_type</c> primary key is a non-nullable resource column with no
        /// join and no missing segment, so this counts the same rows as <see cref="AllMatches"/>.
        /// </summary>
        public sealed record CurrentSortPhase : Count
        {
            private protected override void ThisUnionIsClosed()
            {
            }
        }
    }

    /// <summary>
    /// Include-stage rows only, omitting the match page from the result while still using it to seed the
    /// stages: the <c>$includes</c> operation's second page. <paramref name="Resume"/> is the keyset
    /// boundary of the previous page, null on the first.
    /// </summary>
    /// <param name="Resume">The last include row of the previous page, or null to start at the first.</param>
    public sealed record IncludesPage(IncludeBoundary? Resume = null) : ResultShape
    {
        private protected override void ThisUnionIsClosed()
        {
        }
    }
}
