namespace Ignixa.Search.Sql.Ast;

/// <summary>
/// Immutable configuration for the match page and its optional include-seed wrappers.
/// </summary>
/// <remarks>
/// <c>TopIncludesProbeRow</c> states that <c>Top</c> is the page size plus a has-more lookahead row. The
/// OFFSET/FETCH equivalent lives on <see cref="OffsetSpec.ProbeExtraRow"/>; ask <see cref="TrimmedPageSize"/>
/// rather than either flag, so the two paging mechanisms cannot be handled inconsistently.
/// </remarks>
public sealed record MatchPageSpec(
    CteRef Root,
    int? Top = null,
    Predicate? OuterPredicate = null,
    SortSpec? Sort = null,
    PageSpec? Page = null,
    ResultShape? Shape = null,
    SurrogateIdRange? SurrogateRange = null,
    SqlParameterRef? SearchParameterHash = null,
    OffsetSpec? OffsetPage = null,
    bool TopIncludesProbeRow = false)
{
    /// <summary>The result shape, defaulting to <see cref="ResultShape.Matches"/>.</summary>
    public ResultShape EffectiveShape => Shape ?? ResultShape.Default;

    /// <summary>True when the statement returns a count rather than rows.</summary>
    public bool CountOnly => EffectiveShape is ResultShape.Count;

    /// <summary>True when the statement omits match rows from its final result and returns include-stage rows only.</summary>
    public bool IncludesOnly => EffectiveShape is ResultShape.IncludesPage;

    /// <summary>
    /// How many rows are genuinely on the page, excluding any has-more probe row — the row count the
    /// include-seed wrapper trims down to. Null when the page does not over-fetch, which makes this the
    /// single answer to both "does this page over-fetch" and "by how much", under either paging mechanism.
    /// Deliberately one member rather than a flag plus a size: those could disagree (a probe flag with no
    /// cap to subtract from), and every caller would have had to know which to trust.
    /// </summary>
    /// <remarks>
    /// Yields null rather than a negative count under either paging mechanism, so this never reports a row
    /// count it could not mean. Null therefore means "no over-fetch, or an
    /// incoherent probe spec that an earlier guard has already rejected" — <c>QueryPlanValidator</c> runs
    /// <c>RequireCoherentProbeRow</c> before any read of this member, and <c>Lower</c> rejects the same
    /// combination in options vocabulary, so a caller reaching a read can treat null as the first meaning.
    /// </remarks>
    public int? TrimmedPageSize
    {
        get
        {
            if (OffsetPage is { ProbeExtraRow: true } offset)
            {
                // Clamped like the Top branch below: OffsetSpec.Limit's own guard lives in
                // PlanShapeValidator, which runs after the reads this member serves, so a negative limit
                // would otherwise be reported as a row count while validation is still deciding.
                return offset.Limit >= 0 ? offset.Limit : null;
            }

            // The usable-cap threshold is KeysetPageInvariants' to own: the validators consume those
            // predicates to reject exactly the states this returns null for, so a hand-written `cap >= 1`
            // here would be a second copy the two could drift apart on.
            var incoherent = KeysetPageInvariants.ProbeRowNeedsCap(Top, TopIncludesProbeRow)
                || KeysetPageInvariants.ProbeRowCapTooSmall(Top, TopIncludesProbeRow);

            return TopIncludesProbeRow && !incoherent && Top is { } cap ? cap - 1 : null;
        }
    }

    /// <summary>
    /// The keyset boundary for later pages of an <see cref="ResultShape.IncludesPage"/> stream, or null for
    /// other result shapes.
    /// </summary>
    public IncludeBoundary? IncludeBoundary => (EffectiveShape as ResultShape.IncludesPage)?.Resume;
}
