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
    /// A cap too small to contain both a page and its probe row yields null rather than a negative count, so
    /// this never reports a row count it could not mean. The incoherent spec is still rejected — by
    /// <c>QueryPlanValidator</c>, which names the flag and the cap — but it is rejected there rather than
    /// silently seeding a wrapper from a negative size on the way.
    /// </remarks>
    public int? TrimmedPageSize => OffsetPage is { ProbeExtraRow: true } offset
        ? offset.Limit
        : TopIncludesProbeRow && Top is { } cap && cap >= 1 ? cap - 1 : null;

    /// <summary>
    /// The keyset boundary for later pages of an <see cref="ResultShape.IncludesPage"/> stream, or null for
    /// other result shapes.
    /// </summary>
    public IncludeBoundary? IncludeBoundary => (EffectiveShape as ResultShape.IncludesPage)?.Resume;
}
